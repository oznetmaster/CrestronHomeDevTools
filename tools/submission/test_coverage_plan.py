# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""Offline coverage expansion and source/mapping drift guards."""

import json
from pathlib import Path
import tempfile
import unittest

from build_help import sha
from coverage_plan import compile_plan, ui_targets
from self_test_form import decisions


class CoveragePlanTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory()
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.ui = b'<uidefinition><tile/><layouts><layout id="Main"><controls><buttongroup><button id="Action"/></buttongroup></controls></layout></layouts></uidefinition>'
        (self.root / 'ui.xml').write_bytes(self.ui)
        self.inventory = {'schemaVersion': 1, 'requirements': [
            {'id': 'views', 'label': 'Views', 'field': 'First', 'minimumObservationSeconds': 0},
            {'id': 'endurance', 'label': 'Endurance', 'field': 'Second', 'minimumObservationSeconds': 86400}]}
        self.inventory_digest = self.write('inventory.json', self.inventory)
        self.plan = {'schemaVersion': 1, 'status': 'draft', 'inventorySha256': self.inventory_digest, 'sourceHashMode': 'line-endings-lf', 'driver': 'Synthetic fixture',
                     'sources': [{'path': 'ui.xml', 'sha256': sha(self.ui), 'surface': 'device'}], 'requirements': [
                         {'id': 'views', 'checks': [self.check('render', sorted(ui_targets(self.ui, 'device')))]},
                         {'id': 'endurance', 'checks': [self.check('observe', ['$device'], 86400)]}]}

    def write(self, filename, value):
        data = (json.dumps(value, indent=2) + '\n').encode()
        (self.root / filename).write_bytes(data)
        return sha(data)

    @staticmethod
    def check(identifier, targets, minimum=0):
        return {'id': identifier, 'targets': targets, 'method': 'android', 'expectation': 'Synthetic planned assertion',
                'minimumSeconds': minimum, 'responseLimitSeconds': None, 'restore': True}

    def compile(self):
        digest = self.write('plan.json', self.plan)
        return compile_plan(self.root / 'plan.json', digest, self.root / 'inventory.json', self.inventory_digest, self.root)

    def test_expands_each_target_and_preserves_policy_mapping_identity(self):
        policy, mapping, contract = self.compile()
        self.assertEqual(sha((json.dumps(policy, indent=2) + '\n').encode()), mapping['policySha256'])
        self.assertEqual({r['id'] for r in policy['requirements']}, {i for r in mapping['requirements'] for i in r['observationIds']})
        self.assertEqual(len(contract['tasks']), len(policy['requirements']))
        self.assertTrue(all(t['producer'] is None for t in contract['tasks']))
        self.assertFalse(contract['submissionReady'])
        self.assertTrue(contract['policyApprovalRequired'])
        self.assertEqual('1.00:00:00', policy['requirements'][-1]['minimumDuration'])
        self.assertFalse(any(r['allowNotApplicable'] for r in policy['requirements']))

    def test_changed_source_requires_review(self):
        (self.root / 'ui.xml').write_bytes(self.ui + b' ')
        with self.assertRaisesRegex(ValueError, 'source changed'):
            self.compile()

    def test_executable_policy_retains_scope_timing_restoration_and_absence(self):
        self.plan['requirements'][0]['checks'][0]['responseLimitSeconds'] = 2
        policy, _, contract = self.compile()
        for rule, task in zip(policy['requirements'], contract['tasks']):
            execution = rule['execution']
            self.assertEqual(task['target'], execution['target'])
            self.assertEqual(task['method'], execution['method'])
            self.assertEqual(task['responseLimitSeconds'], execution['responseLimitSeconds'])
            self.assertEqual(task['restore'], execution['restore'])
            self.assertEqual(task['requiredOutcome'], execution['requiredOutcome'])
            self.assertIsNone(execution['maximumSampleGapSeconds'])

    def test_checkout_line_endings_do_not_change_coverage_source_identity(self):
        normalized = self.ui + b'\n'
        self.plan['sources'][0]['sha256'] = sha(normalized)
        (self.root / 'ui.xml').write_bytes(normalized.replace(b'\n', b'\r\n'))
        _, _, contract = self.compile()
        self.assertEqual('line-endings-lf', contract['sourceHashMode'])

    def test_generated_mapping_is_consumable_by_form_decisions_with_synthetic_results(self):
        policy, mapping, _ = self.compile()
        # Synthetic structural coverage only: no form is written or real observation claimed.
        results = {'observations': [{'requirementId': r['id'], 'outcome': 'Passed'} for r in policy['requirements']]}
        rows = decisions(self.inventory, self.inventory_digest, mapping, policy, results)
        self.assertEqual({'First', 'Second'}, {row['field'] for row in rows})
        self.assertTrue(all(row['state'] == 'Passed' for row in rows))

    def test_new_target_cannot_hide_behind_updated_source_hash(self):
        changed = self.ui.replace(b'</controls>', b'<button id="New"/></controls>')
        (self.root / 'ui.xml').write_bytes(changed)
        self.plan['sources'][0]['sha256'] = sha(changed)
        with self.assertRaisesRegex(ValueError, 'without any coverage'):
            self.compile()

    def test_unknown_target_is_rejected(self):
        self.plan['requirements'][0]['checks'][0]['targets'].append('device/Main/Missing')
        with self.assertRaisesRegex(ValueError, 'unknown UI'):
            self.compile()

    def test_each_official_item_is_required(self):
        self.plan['requirements'].pop()
        with self.assertRaisesRegex(ValueError, 'every official'):
            self.compile()

    def test_duplicate_expanded_claim_is_rejected(self):
        self.plan['requirements'][0]['checks'] *= 2
        with self.assertRaisesRegex(ValueError, 'Duplicate expanded'):
            self.compile()

    def test_inventory_duration_cannot_be_lowered(self):
        self.plan['requirements'][1]['checks'][0]['minimumSeconds'] = 3600
        with self.assertRaisesRegex(ValueError, 'minimum duration'):
            self.compile()

    def test_response_budget_requires_real_number_not_boolean(self):
        self.plan['requirements'][0]['checks'][0]['responseLimitSeconds'] = True
        with self.assertRaisesRegex(ValueError, 'timing'):
            self.compile()

    def test_absence_requires_its_own_pending_producer(self):
        self.plan['requirements'][0]['checks'].append({**self.check('absence', ['$optional-control']), 'method': 'absence'})
        policy, _, contract = self.compile()
        task = next(t for t in contract['tasks'] if t['method'] == 'absence')
        self.assertEqual('NotApplicable', task['requiredOutcome'])
        self.assertIsNone(task['producer'])
        self.assertTrue(next(r for r in policy['requirements'] if r['id'] == task['observationId'])['allowNotApplicable'])

    def test_no_approved_status_can_be_injected(self):
        self.plan['status'] = 'approved'
        with self.assertRaisesRegex(ValueError, 'draft blueprint'):
            self.compile()

    def test_source_cannot_escape_checkout(self):
        self.plan['sources'][0]['path'] = '../ui.xml'
        with self.assertRaisesRegex(ValueError, 'relative'):
            self.compile()

    def test_dtd_and_duplicate_controls_are_rejected(self):
        for value in (b'<!DOCTYPE uidefinition []>' + self.ui, self.ui.replace(b'</buttongroup>', b'<button id="Action"/></buttongroup>')):
            with self.subTest(value=value), self.assertRaises(ValueError):
                ui_targets(value, 'device')

    def test_reviewed_plan_pin_is_required(self):
        self.write('plan.json', self.plan)
        with self.assertRaisesRegex(ValueError, 'digest'):
            compile_plan(self.root / 'plan.json', '0' * 64, self.root / 'inventory.json', self.inventory_digest, self.root)


if __name__ == '__main__':
    unittest.main()
