# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
"""The real console, validator, form and portable bundle preserve prior-test provenance."""
import copy
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import shutil
import subprocess
import unittest

from pypdf import PdfReader

import self_test_form as forms
import test_declared_gap_form as gap_fixtures


class PriorEvidenceFormTests(unittest.TestCase):
    write_json = staticmethod(gap_fixtures.DeclaredGapFormTests.write_json)
    prepare = gap_fixtures.DeclaredGapFormTests.prepare
    validate = gap_fixtures.DeclaredGapFormTests.validate

    def setUp(self):
        gap_fixtures.DeclaredGapFormTests.setUp(self)
        self.prior_root = self.evidence / 'source'
        self.prior_root.mkdir()
        (self.prior_root / 'evidence').mkdir()
        shutil.copyfile(self.evidence / 'synthetic.txt', self.prior_root / 'evidence/synthetic.txt')
        old_identity = copy.deepcopy(self.identity)
        old_policy = copy.deepcopy(self.policy)
        old_observations = copy.deepcopy(self.observations)
        (self.prior_root / 'policy.json').write_bytes(self.policy_path.read_bytes())
        self.write_json(self.prior_root / 'observations.json', old_observations)
        self.identity['sourceCommit'] = 'a' * 40
        evidence = self.evidence / 'change-analysis.json'
        self.write_json(evidence, {'purpose': 'Synthetic dependency review, not actual hardware evidence'})
        decision = {'schemaVersion': 1, 'sourceIdentity': old_identity,
                    'targetPackageSha256': self.identity['packageSha256'],
                    'targetSourceCommit': self.identity['sourceCommit'],
                    'reviewedUtc': (datetime.now(timezone.utc) - timedelta(minutes=1)).isoformat(),
                    'reviewer': 'Synthetic Reviewer',
                    'decisions': [{'requirementId': r['id'], 'rationale': 'Synthetic unchanged behavior review.',
                        'evidence': [{'relativePath': 'change-analysis.json', 'sha256': forms.sha(evidence.read_bytes())}]}
                        for r in old_policy['requirements'][:2]]}
        self.write_json(self.evidence / 'change-review.json', decision)
        prior = {'identity': old_identity,
                 'policy': {'relativePath': 'source/policy.json', 'sha256': forms.sha((self.prior_root / 'policy.json').read_bytes())},
                 'observations': {'relativePath': 'source/observations.json', 'sha256': forms.sha((self.prior_root / 'observations.json').read_bytes())},
                 'evidenceDirectory': 'source/evidence',
                 'changeReview': {'relativePath': 'change-review.json', 'sha256': forms.sha((self.evidence / 'change-review.json').read_bytes())}}
        for r in self.policy['requirements'][:2]:
            r['priorEvidence'] = prior
        self.write_json(self.policy_path, self.policy)
        self.identity['policySha256'] = forms.sha(self.policy_path.read_bytes())
        self.mapping['policySha256'] = self.identity['policySha256']
        self.candidate['identity'] = self.identity
        self.prepare()
        output = self.root / 'imported.json'
        result = subprocess.run([self.dotnet, str(self.validator), 'submission-import-prior-evidence',
            '--candidate', str(self.candidate_path), '--candidate-sha256', forms.sha(self.candidate_path.read_bytes()),
            '--policy', str(self.policy_path), '--evidence', str(self.evidence), '--output', str(output)],
            capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)
        imported = json.loads(output.read_text())['observations']
        self.observations['observations'] = imported + [self.observations['observations'][-1]]
        for row in self.observations['observations']:
            row['identity'] = self.identity

    def tearDown(self):
        shutil.rmtree(self.root)

    def test_real_import_and_form_distinguish_prior_passes_from_fresh_runs(self):
        rows, identity, raw = self.validate()
        report = forms.write_form(self.source, self.inventory, self.output, 'SYNTHETIC PRIOR REVIEW',
            'Example Developer', rows, identity, False, declared_gaps=True)
        self.assertEqual(report['checkedRequirements'], ['first', 'second'])
        reader = PdfReader(self.output)
        forms.inspect_form(reader, self.inventory, {'First': '/Yes', 'Second': '/Yes'}, report['companionPages'])
        body = '\n'.join(page.extract_text() for page in reader.pages)
        self.assertIn('Reviewed prior evidence', body)
        self.assertIn('not fresh executions', body)
        assessment = json.loads(raw)['assessment']['requirements']
        self.assertEqual([r['status'] for r in assessment], ['VerifiedPriorEvidence', 'VerifiedPriorEvidence', 'VerifiedAgainstPlan'])

    def test_tampered_prior_run_cannot_be_declared_away(self):
        with (self.prior_root / 'observations.json').open('a') as stream:
            stream.write(' ')
        self.gaps = [{'requirementId': key, 'reason': 'Do not allow this to waive changed evidence.'}
                     for key in ('first.a', 'first.b')]
        with self.assertRaises(ValueError):
            self.validate()

    def test_portable_review_bundle_revalidates_sources_after_original_directory_moves(self):
        self.validate()
        archive = self.root / 'review.zip'
        args = [self.dotnet, str(self.validator), 'submission-review-bundle-create',
            '--candidate', str(self.candidate_path), '--candidate-sha256', forms.sha(self.candidate_path.read_bytes()),
            '--package', str(self.package), '--policy', str(self.policy_path), '--template', str(self.template),
            '--observations', str(self.observations_path), '--evidence', str(self.evidence),
            '--mode', 'declared-gaps', '--declarations', str(self.declarations_path),
            '--declarations-sha256', self.declarations_digest, '--output', str(archive)]
        result = subprocess.run(args, capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)
        moved = self.evidence.with_name('original-evidence-moved')
        self.evidence.rename(moved)
        # The archive's extracted tree, not original absolute paths, must supply all provenance.
        scratch = self.root / 'check'
        scratch.mkdir()
        result = subprocess.run([self.dotnet, str(self.validator), 'submission-review-bundle-check',
            '--bundle', str(archive), '--bundle-sha256', forms.sha(archive.read_bytes()),
            '--candidate-sha256', forms.sha(self.candidate_path.read_bytes()), '--mode', 'declared-gaps',
            '--declarations-sha256', self.declarations_digest, '--scratch', str(scratch)],
            capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == '__main__':
    unittest.main()
