# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Offline regressions for incomplete raw evidence; never hardware acceptance."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from build_help import sha
from review_android import audit_runs
import test_audit_android as fixtures


def interrupt(fixture):
    for name in ('completion.json', 'coverage.json', 'TestResult.trx'):
        (fixture.root / name).unlink()
    (fixture.root / 'original-worker-error.txt').write_bytes(b'Original interruption, not a passed test')


def inventory(root):
    return [{'relativePath': p.relative_to(root).as_posix(), 'sha256': sha(p.read_bytes())}
            for p in sorted(root.rglob('*')) if p.is_file()]


class InterruptedAndroidTests(unittest.TestCase):
    def setUp(self):
        self.f = f = fixtures.AndroidEvidenceTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        interrupt(f)
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.path = Path(temporary.name) / 'independent-pins.json'
        self.settings = {'candidate': str(f.root / 'candidate.json'),
                         'androidEvidence': [{'runId': f.run, 'path': str(f.root)}]}
        self.run = {'runId': f.run, 'assembly': f.assembly, 'assemblySha256': f.assembly_hash,
                    'discoverySha256': f.discovery_hash, 'producerManifestSha256': f.manifest_hash,
                    'interruptedFiles': inventory(f.root)}

    def check(self, mode='declared-gaps'):
        self.path.write_text(json.dumps({'schemaVersion': 1, 'candidateSha256': self.f.candidate_hash,
                                        'runs': [self.run]}))
        return audit_runs(self.settings, self.path, sha(self.path.read_bytes()), self.f.candidate_hash, review_mode=mode)

    def repin(self):
        self.run['interruptedFiles'] = inventory(self.f.root)

    def test_preserves_every_file_without_claiming_completion_or_restoration(self):
        result = self.check()['runs'][0]
        self.assertEqual('InterruptedAndroidEvidencePreserved', result['status'])
        self.assertEqual(self.run['interruptedFiles'], result['files'])
        self.assertFalse(result['completePassingRun'])
        self.assertFalse(result['producerAuthenticated'])
        self.assertFalse(result['submissionReady'])
        self.assertIsNone(result['executedTests'])
        self.assertIsNone(result['restorationConfirmed'])
        self.assertEqual([], result['officialRequirementsSatisfied'])

    def test_both_explicit_mode_and_independent_inventory_are_required(self):
        with self.assertRaises(ValueError): self.check('complete')
        del self.run['interruptedFiles']
        with self.assertRaises(ValueError): self.check()

    def test_failed_capture_outcome_is_preserved_without_becoming_a_pass(self):
        self.f.capture['Outcome'] = 'Failed'
        self.f.write('check/observation.json', self.f.capture)
        self.repin()
        self.assertEqual(1, self.check()['runs'][0]['retainedCaptureRecords'])
        self.assertEqual('Failed', json.loads((self.f.root / 'check/observation.json').read_bytes())['Outcome'])

    def test_changed_missing_and_extra_files_are_rejected(self):
        path = self.f.root / 'original-worker-error.txt'
        data = path.read_bytes()
        for action in ('change', 'delete', 'extra'):
            with self.subTest(action=action):
                if action == 'change': path.write_bytes(b'Changed')
                elif action == 'delete': path.unlink()
                else: (self.f.root / 'unlisted.txt').write_bytes(b'Extra')
                with self.assertRaises(ValueError): self.check()
                path.write_bytes(data)
                (self.f.root / 'unlisted.txt').unlink(missing_ok=True)

    def test_present_terminal_records_cannot_be_waived_even_with_new_inventory(self):
        for name in ('completion.json', 'coverage.json', 'Aborted.trx', 'COMPLETION.JSON'):
            with self.subTest(name=name):
                (self.f.root / name).write_bytes(b'Malformed or failed original')
                self.repin()
                with self.assertRaisesRegex(ValueError, 'absent terminal records'): self.check()
                (self.f.root / name).unlink()

    def test_mixed_identity_or_changed_producer_cannot_be_waived_by_repinning_raw_files(self):
        for name in ('context.json', 'assembly/nunit.framework.dll', 'discovery.dump'):
            with self.subTest(name=name):
                path = self.f.root / name
                data = path.read_bytes()
                if name == 'context.json':
                    changed = json.loads(data)
                    changed['RunId'] = '0' * 32
                    path.write_text(json.dumps(changed))
                else: path.write_bytes(b'Changed')
                self.repin()
                with self.assertRaises(ValueError): self.check()
                path.write_bytes(data)

    def test_capture_identity_time_hash_and_schema_guards_survive_repinning(self):
        original = copy.deepcopy(self.f.capture)
        for change in ({'RunId': '0' * 32}, {'StartedUtc': '2027-01-01T00:00:00Z'},
                       {'ScreenshotSha256': '0' * 64}, {'SchemaVersion': 2}, {'Outcome': 'Invented'}):
            with self.subTest(change=change):
                self.f.write('check/observation.json', {**original, **change})
                self.repin()
                with self.assertRaises(ValueError): self.check()

    def test_duplicate_unsafe_or_ambiguous_inventory_is_rejected(self):
        original = copy.deepcopy(self.run['interruptedFiles'])
        for name in ('../outside', '/root', 'C:/outside', 'a\\b', 'CON.txt', 'x.', 'x ', 'a?b', 'context.json', 'CONTEXT.JSON'):
            with self.subTest(name=name):
                self.run['interruptedFiles'] = original + [{'relativePath': name, 'sha256': '0' * 64}]
                with self.assertRaises(ValueError): self.check()

    def test_selected_execution_still_requires_original_independent_selection(self):
        self.f.prepare_selection()
        # Fixture preparation recreates coverage; an interrupted original has none.
        (self.f.root / 'coverage.json').unlink()
        self.run.update(discoverySha256=self.f.discovery_hash, selectionSha256=self.f.selection_hash)
        self.repin()
        self.assertEqual(1, self.check()['runs'][0]['excludedTests'])
        self.run['selectionSha256'] = '0' * 64
        with self.assertRaises(ValueError): self.check()


class InterruptedReviewIntegrationTests(unittest.TestCase):
    def setUp(self):
        import test_declared_gap_review as review_fixtures
        self.f = f = review_fixtures.DeclaredGapReviewTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        f.android_policy()
        f.f.observations['observations'][0]['outcome'] = 'Failed'
        f.gaps.append({'requirementId': 'first.a', 'reason': 'Original run interrupted before terminal results; no passing claim.'})
        f.save()
        self.android, self.options = f.base.android_run()
        interrupt(self.android)
        pins_path = Path(self.options['android_pins'])
        value = json.loads(pins_path.read_bytes())
        value['runs'][0]['interruptedFiles'] = inventory(self.android.root)
        pins_path.write_text(json.dumps(value))
        self.options['android_pins_sha256'] = sha(pins_path.read_bytes())

    def test_public_preparation_freezes_all_originals_and_leaves_android_checkbox_unchecked(self):
        from freeze_review_inputs import load
        from pypdf import PdfReader
        receipt = self.f.run_stage(**self.options)
        self.assertEqual('GapsDeclared', receipt['verificationStatus'])
        self.assertFalse(receipt['submissionReady'])
        self.assertFalse(receipt['deliveryAttempted'])
        output = self.f.output
        frozen = output.with_name(output.name + '.inputs')
        pin = (frozen / 'COMPLETE').read_text().strip()
        _, settings, options = load(frozen, pin, completed=True)
        result = audit_runs(settings, options['androidPins'], options['androidPinsSha256'], options['candidateSha256'],
                            review_mode=options['reviewMode'])
        self.assertEqual('InterruptedAndroidEvidencePreserved', result['runs'][0]['status'])
        for item in inventory(self.android.root):
            self.assertEqual((self.android.root / item['relativePath']).read_bytes(),
                             (frozen / 'android' / self.android.run / item['relativePath']).read_bytes())
        fields = PdfReader(output / 'self-test.review.pdf').get_fields()
        self.assertEqual('/Off', fields['First']['/V'])
        self.assertEqual('', fields['Signature'].get('/V', ''))
        self.assertFalse((frozen / 'android' / self.android.run / 'completion.json').exists())

    def test_interrupted_inventory_cannot_support_passed_android_scope(self):
        self.f.f.observations['observations'][0]['outcome'] = 'Passed'
        self.f.save()
        with self.assertRaisesRegex(ValueError, 'cannot support a Passed'):
            self.f.run_stage(**self.options)
        self.assertFalse(self.f.output.exists())

    def test_missing_gap_or_accepted_interpretation_cannot_claim_incomplete_run(self):
        self.f.gaps[-1]['interpretationReview'] = {'reviewer': 'Synthetic reviewer'}
        self.f.save()
        with self.assertRaisesRegex(ValueError, 'without accepted interpretations'):
            self.f.run_stage(**self.options)
        self.assertFalse(self.f.output.exists())

    def test_changed_raw_supplement_after_bundle_prevents_publication(self):
        import prepare_review as stage
        original = stage.run_process
        def mutate(args, timeout):
            result = original(args, timeout)
            if 'submission-review-bundle-create' in args:
                path = self.f.output.with_name(self.f.output.name + '.inputs') / 'android' / self.android.run / 'original-worker-error.txt'
                path.write_bytes(b'Changed after bundle')
            return result
        with patch.object(stage, 'run_process', mutate), self.assertRaises(ValueError):
            self.f.run_stage(**self.options)
        self.assertFalse(self.f.output.exists())


if __name__ == '__main__':
    unittest.main()
