# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Synthetic historical review checks; no processor or submission evidence is executed."""
import copy
import json
from pathlib import Path
import unittest
from build_help import sha
from review_android import audit_runs, validate_observation_bindings
import test_declared_gap_review as fixtures


class HistoricalReviewTests(unittest.TestCase):
    def setUp(self):
        self.f = f = fixtures.DeclaredGapReviewTests()
        f.setUp()
        self.addCleanup(f.doCleanups)
        f.android_policy()
        self.a, self.options = f.base.android_run()
        a = self.a
        self.path = Path(self.options['android_pins'])
        self.pins = json.loads(self.path.read_bytes())
        proof = f.f.evidence / 'old-package.pkg'
        proof.write_bytes(b'Synthetic historical package')
        package_hash = sha(proof.read_bytes())
        for name in ('context.json', 'coverage.json', 'completion.json', 'producer-pin.json', 'check/observation.json'):
            value = json.loads((a.root / name).read_bytes())
            value['PackageSha256'] = package_hash
            if 'ReleaseSourceCommit' in value:
                value['ReleaseSourceCommit'] = None
            a.write(name, value)
        (f.f.evidence / 'build-attestation.json').write_text('{"synthetic":true}')
        self.review_files = [{'relativePath': n, 'sha256': sha((f.f.evidence / n).read_bytes())}
                             for n in ('old-package.pkg', 'build-attestation.json')]
        self.historical = {'identity': {'packageSha256': package_hash, 'sourceSha256': a.context['SourceSha256'],
            'releaseSourceCommit': None, 'driverId': a.context['DriverGuid'], 'driverVersion': a.context['DriverVersion'], 'installedDriverId': a.context['InstalledDriverId']},
            'sourceReview': {'reviewer': 'Synthetic named reviewer', 'reviewedUtc': '2026-01-02T00:00:00Z',
                'rationale': 'Synthetic narrow change-impact review; no current execution.', 'sourceCommit': 'f' * 40,
                'package': self.review_files[0], 'files': self.review_files}}
        self.pins['runs'][0]['historicalReview'] = self.historical
        files = list(self.review_files)
        names = ['context.json', 'coverage.json', 'completion.json', 'TestResult.trx',
                 'check/observation.json', 'check/hierarchy.xml', 'check/screen.png']
        for name in names:
            dest = f.f.evidence / 'prior' / name
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes((a.root / name).read_bytes())
            files.append({'relativePath': 'prior/' + name, 'sha256': sha(dest.read_bytes())})
        self.pins['observationBindings'] = [{'requirementId': 'first.a', 'runId': a.run, 'files': files}]
        observation = f.f.observations['observations'][0]
        observation['outcome'] = 'Inconclusive'
        observation['files'].extend(files)
        f.gaps.append({'requirementId': 'first.a', 'reason': 'Historical scope requires interpretation.',
            'interpretationReview': {'reviewer': 'Synthetic named reviewer', 'rationale': 'Exact original assertion and unchanged implementation only.', 'evidence': files}})
        f.save()
        self.save()

    def save(self):
        self.path.write_text(json.dumps(self.pins))
        self.options['android_pins_sha256'] = sha(self.path.read_bytes())

    def audit(self, mode='declared-gaps'):
        self.save()
        return audit_runs(self.f.base.settings, self.path, self.options['android_pins_sha256'], self.f.base.pins[0], review_mode=mode)

    def validate(self):
        return validate_observation_bindings(self.audit(), {'first.a'}, self.f.f.observations,
            {'declarations': self.f.gaps}, self.f.f.evidence)

    def test_null_commit_remains_null_and_external_source_is_separate(self):
        result = self.audit()['runs'][0]
        self.assertEqual('HistoricalAndroidEvidenceAudited', result['status'])
        self.assertIsNone(result['identity']['releaseSourceCommit'])
        self.assertEqual('f' * 40, result['sourceReview']['sourceCommit'])
        self.assertFalse(result['releaseSourceAuthenticated'])
        self.assertEqual(2, result['executedTests'])
        self.validate()
        self.assertIsNone(json.loads((self.a.root / 'context.json').read_bytes())['ReleaseSourceCommit'])

    def test_complete_mode_rejects_historical_review(self):
        with self.assertRaises(ValueError): self.audit('complete')

    def test_original_failures_missing_completion_and_changed_capture_still_fail(self):
        for name, transform in [('completion.json', lambda b: b.replace(b'true', b'false')),
                                ('TestResult.trx', lambda b: b.replace(b'Passed', b'Failed')),
                                ('check/screen.png', lambda b: b + b'changed')]:
            path = self.a.root / name
            original = path.read_bytes()
            with self.subTest(name=name):
                path.write_bytes(transform(original))
                with self.assertRaises(ValueError): self.audit()
                path.write_bytes(original)
        path = self.a.root / 'completion.json'
        original = path.read_bytes(); path.unlink()
        with self.assertRaises((OSError, ValueError)): self.audit()
        path.write_bytes(original)

    def test_raw_null_cannot_be_replaced_by_external_commit(self):
        self.historical['identity']['releaseSourceCommit'] = 'f' * 40
        with self.assertRaises(ValueError): self.audit()

    def test_package_or_source_review_evidence_cannot_change(self):
        for name in ('old-package.pkg', 'build-attestation.json'):
            path = self.f.f.evidence / name; original = path.read_bytes()
            with self.subTest(name=name):
                path.write_bytes(original + b'x')
                with self.assertRaises(ValueError): self.audit()
                path.write_bytes(original)

    def test_historical_run_cannot_create_pass_or_unbound_interpretation(self):
        observation = self.f.f.observations['observations'][0]
        for outcome in ('Passed', 'ReviewedPriorPass', 'Failed'):
            observation['outcome'] = outcome
            with self.subTest(outcome=outcome), self.assertRaises(ValueError): self.validate()
        observation['outcome'] = 'Inconclusive'
        self.pins['observationBindings'] = []
        with self.assertRaises(ValueError): self.validate()

    def test_binding_requires_exact_scope_original_results_and_capture(self):
        entry = self.pins['observationBindings'][0]
        original = copy.deepcopy(entry)
        for field, value in [('runId', 'b' * 32), ('requirementId', 'other.a'), ('files', self.review_files)]:
            with self.subTest(field=field):
                entry[field] = value
                with self.assertRaises(ValueError): self.validate()
                entry.clear(); entry.update(copy.deepcopy(original))
        self.f.gaps[-1]['interpretationReview']['evidence'] = self.review_files
        with self.assertRaises(ValueError): self.validate()

    def test_complete_historical_interpretation_can_coexist_with_unchanged_interrupted_run(self):
        from test_interrupted_android import interrupt, inventory
        current, options = self.f.base.android_run()
        new_pins = json.loads(Path(options['android_pins']).read_bytes())
        current.run = 'b' * 32
        for name in ('context.json', 'producer-pin.json', 'check/observation.json'):
            value = json.loads((current.root / name).read_bytes())
            value['RunId'] = current.run
            current.write(name, value)
        interrupt(current)
        run = new_pins['runs'][0]
        run['runId'] = current.run
        run['interruptedFiles'] = inventory(current.root)
        self.pins['runs'].append(run)
        self.f.base.settings['androidEvidence'] = [
            {'runId': self.a.run, 'path': str(self.a.root)}, {'runId': current.run, 'path': str(current.root)}]
        self.f.f.write_json(self.f.base.settings_path, self.f.base.settings)
        self.save()
        self.validate()
        receipt = self.f.run_stage(**self.options)
        self.assertFalse(receipt['submissionReady'])
        audit = json.loads((self.f.output / 'android-audit.json').read_bytes())
        self.assertEqual({'HistoricalAndroidEvidenceAudited', 'InterruptedAndroidEvidencePreserved'},
                         {r['status'] for r in audit['runs']})
        self.pins['observationBindings'][0]['runId'] = current.run
        with self.assertRaises(ValueError): self.validate()

    def archive_old_run(self):
        from test_interrupted_android import interrupt, inventory
        interrupt(self.a)
        run = self.pins['runs'][0]
        run['archivedReview'] = run.pop('historicalReview')
        run['interruptedFiles'] = inventory(self.a.root)
        return run

    def test_old_interrupted_archive_preserves_identity_and_never_passes(self):
        self.archive_old_run()
        report = self.audit()['runs'][0]
        self.assertEqual('InterruptedAndroidEvidencePreserved', report['status'])
        self.assertTrue(report['archivedEvidence'])
        self.assertEqual(self.historical['identity'], report['identity'])
        self.assertNotEqual(report['identity']['packageSha256'], report['targetIdentity']['packageSha256'])
        self.assertFalse(report['completePassingRun'])
        self.assertIsNone(report['executedTests'])
        self.assertFalse(report['submissionReady'])
        with self.assertRaises(ValueError): self.validate()
        self.pins['observationBindings'] = []
        self.f.gaps[-1].pop('interpretationReview')
        self.validate()
        self.assertIsNone(json.loads((self.a.root / 'context.json').read_bytes())['ReleaseSourceCommit'])

    def test_archived_original_identity_survives_public_review_freeze(self):
        from freeze_review_inputs import load
        self.archive_old_run()
        self.pins['observationBindings'] = []
        self.f.gaps[-1].pop('interpretationReview')
        self.f.save()
        self.save()
        receipt = self.f.run_stage(**self.options)
        self.assertFalse(receipt['submissionReady'])
        frozen = self.f.output.with_name(self.f.output.name + '.inputs')
        _, settings, opts = load(frozen, (frozen / 'COMPLETE').read_text().strip(), completed=True)
        result = audit_runs(settings, opts['androidPins'], opts['androidPinsSha256'],
                            opts['candidateSha256'], review_mode=opts['reviewMode'])
        self.assertEqual('InterruptedAndroidEvidencePreserved', result['runs'][0]['status'])
        self.assertTrue(result['runs'][0]['archivedEvidence'])
        self.assertIsNone(result['runs'][0]['identity']['releaseSourceCommit'])

    def test_archive_rejects_complete_mode_and_missing_interrupted_inventory(self):
        run = self.archive_old_run()
        with self.assertRaises(ValueError): self.audit('complete')
        del run['interruptedFiles']
        with self.assertRaises(ValueError): self.audit()

    def test_archive_rejects_dual_identity_and_changed_original_identity(self):
        run = self.archive_old_run()
        run['historicalReview'] = copy.deepcopy(run['archivedReview'])
        with self.assertRaises(ValueError): self.audit()
        del run['historicalReview']
        for key, value in [('sourceSha256', '1' * 64), ('installedDriverId', 987654),
                           ('driverVersion', '9.0.0.0'), ('releaseSourceCommit', 'f' * 40)]:
            before = self.historical['identity'][key]
            with self.subTest(key=key):
                self.historical['identity'][key] = value
                with self.assertRaises(ValueError): self.audit()
                self.historical['identity'][key] = before

    def test_archive_keeps_terminal_and_tamper_refusals(self):
        from test_interrupted_android import inventory
        run = self.archive_old_run()
        path = self.a.root / 'check/screen.png'
        original = path.read_bytes()
        path.write_bytes(original + b'changed')
        with self.assertRaises(ValueError): self.audit()
        path.write_bytes(original)
        self.a.write('completion.json', {'RestorationConfirmed': False})
        run['interruptedFiles'] = inventory(self.a.root)
        with self.assertRaises(ValueError): self.audit()

    def test_frozen_public_review_checks_only_the_interpreted_item_and_retains_null(self):
        from freeze_review_inputs import load
        from pypdf import PdfReader
        receipt = self.f.run_stage(**self.options)
        self.assertFalse(receipt['submissionReady'])
        fields = PdfReader(self.f.output / 'self-test.review.pdf').get_fields()
        self.assertEqual('/Yes', fields['First']['/V'])
        self.assertEqual('/Off', fields['Second']['/V'])
        frozen = self.f.output.with_name(self.f.output.name + '.inputs')
        _, settings, opts = load(frozen, (frozen / 'COMPLETE').read_text().strip(), completed=True)
        report = audit_runs(settings, opts['androidPins'], opts['androidPinsSha256'], opts['candidateSha256'], review_mode=opts['reviewMode'])
        self.assertIsNone(report['runs'][0]['identity']['releaseSourceCommit'])
        self.assertEqual((self.f.f.evidence / 'build-attestation.json').read_bytes(), (Path(settings['evidence']) / 'build-attestation.json').read_bytes())


if __name__ == '__main__':
    unittest.main()
