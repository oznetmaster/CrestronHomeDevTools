# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Firsthand history evidence for named interpretations; never a machine test."""
from datetime import datetime, timezone
import re
import json

from audit_android import Evidence, require, timestamp
from build_help import keys, sha, strict_object
from self_test_form import pinned_json
from documentary_android import reference, SCOPE as GROUP_SCOPE
from documentary_gauges import LABEL_SCOPE, SCALE_SCOPE

GAUGE_SCOPE = 'extension.controls.06.device-change-immediate-gauge'
SUBPAGE_SCOPE = 'extension.controls.18.repeat-all-applicable-controls-on-every-subpage'
SCOPES = {GAUGE_SCOPE, SUBPAGE_SCOPE}


def audit_human_history(settings, entries, candidate_sha256, review_mode, documentary):
    require(isinstance(entries, list), 'Invalid human history inventory')
    if not entries:
        return [], {}
    require(review_mode == 'declared-gaps', 'Human history requires declared-gaps review')
    require(isinstance(entries, list) and len(entries) <= len(SCOPES), 'Invalid human history inventory')
    _, candidate = pinned_json(settings['candidate'], candidate_sha256)
    evidence = Evidence(settings['evidence'])
    corroboration = {r['requirementId']: r for r in documentary}
    reports, all_files, seen = [], {}, set()
    for entry in entries:
        keys(entry, ('requirementId', 'provenance', 'statementIndex', 'quote', 'reportedBehavior',
                     'reportedIdentity', 'limitations', 'corroboratingScopes', 'sourceReview'))
        scope = entry['requirementId']
        require(scope in SCOPES and scope not in seen, 'Unknown or duplicate human history scope')
        seen.add(scope)
        files = {}
        provenance = json.loads(reference(entry['provenance'], evidence, files), object_pairs_hook=strict_object)
        require(isinstance(provenance, dict) and isinstance(provenance.get('source'), str) and
                provenance['source'].strip(), 'Require pinned direct-human provenance')
        statements = provenance.get('statements')
        index = entry['statementIndex']
        require(isinstance(statements, list) and 0 < len(statements) <= 128 and
                type(index) is int and 0 <= index < len(statements), 'Invalid human statement selection')
        statement = statements[index]
        require(isinstance(statement, dict) and isinstance(entry['quote'], str) and entry['quote'].strip() and
                entry['quote'] == statement.get('quote') and
                isinstance(entry['reportedBehavior'], str) and entry['reportedBehavior'].strip() and
                entry['reportedBehavior'] == statement.get('reportedObservation'), 'Human statement was rewritten or not an observation')
        limits = entry['limitations']
        require(isinstance(limits, list) and 0 < len(limits) <= 32 and
                all(isinstance(x, str) and x.strip() for x in limits) and limits == statement.get('scopeLimits'),
                'Retain every original human observation limitation')
        identity = entry['reportedIdentity']
        keys(identity, ('deviceId', 'version', 'packageSha256'))
        # No relationship to the corroborating capture's identity is inferred.
        require(identity == statement.get('reportedIdentity', dict.fromkeys(identity)),
                'Reported identity must be exactly supplied by the human provenance')
        require(identity['deviceId'] is None or type(identity['deviceId']) is int and identity['deviceId'] > 0,
                'Invalid reported device identity')
        require(identity['version'] is None or isinstance(identity['version'], str) and
                re.fullmatch(r'[0-9]+(?:\.[0-9]+){3}', identity['version']), 'Invalid reported version')
        require(identity['packageSha256'] is None or isinstance(identity['packageSha256'], str) and
                re.fullmatch(r'[a-f0-9]{64}', identity['packageSha256']), 'Invalid reported package')
        required = {LABEL_SCOPE, SCALE_SCOPE} if scope == GAUGE_SCOPE else {GROUP_SCOPE}
        require(isinstance(entry['corroboratingScopes'], list) and
                len(entry['corroboratingScopes']) == len(required) and set(entry['corroboratingScopes']) == required,
                'Require independently audited static history corroboration')
        for required_scope in sorted(required):
            require(required_scope in corroboration, 'Missing audited documentary corroboration')
            for ref in corroboration[required_scope]['files']:
                reference(ref, evidence, files)
        review = entry['sourceReview']
        keys(review, ('reviewer', 'reviewedUtc', 'rationale', 'currentSourceCommit', 'originalHistorySource', 'currentHistorySource', 'files', 'attestation'))
        require(all(isinstance(review[k], str) and review[k].strip() for k in ('reviewer', 'rationale')),
                'Named human-history technical review required')
        recorded, reviewed = timestamp(provenance.get('recordedUtc')), timestamp(review['reviewedUtc'])
        require(recorded <= reviewed <= datetime.now(timezone.utc), 'Invalid human history review chronology')
        require(review['currentSourceCommit'] == candidate['identity']['sourceCommit'], 'Review identifies another current source')
        require(isinstance(review['files'], list) and 4 <= len(review['files']) <= 128,
                'Retain independent source review and provenance references')
        review_files = {}
        for ref in review['files']:
            require(ref.get('relativePath') not in review_files, 'Duplicate human source-review reference')
            reference(ref, evidence, review_files)
        for field in ('originalHistorySource', 'currentHistorySource'):
            require(reference(review[field], evidence, files).strip(), 'Empty reviewed history source')
            require(review_files.get(review[field]['relativePath']) == review[field]['sha256'],
                    'Source review must retain both historical and current history sources')
        attestation = json.loads(reference(review['attestation'], evidence, files), object_pairs_hook=strict_object)
        expected = {k: entry[k] for k in ('requirementId', 'provenance', 'statementIndex', 'quote',
                                        'reportedBehavior', 'reportedIdentity', 'limitations', 'corroboratingScopes')}
        expected.update({k: review[k] for k in ('reviewer', 'reviewedUtc', 'rationale', 'currentSourceCommit', 'originalHistorySource', 'currentHistorySource')})
        require(attestation == expected, 'Human history attestation differs from exact statement/review')
        require(review_files.get(entry['provenance']['relativePath']) == entry['provenance']['sha256'] and
                review_files.get(review['attestation']['relativePath']) == review['attestation']['sha256'],
                'Source review must retain exact provenance and attestation')
        for ref in review['files']:
            reference(ref, evidence, files)
        all_files.update(files)
        reports.append({'requirementId': scope, 'status': 'ScopedHumanHistoryObservationReviewed',
                        **{k: entry[k] for k in ('provenance', 'statementIndex', 'quote', 'reportedBehavior',
                                               'reportedIdentity', 'limitations', 'corroboratingScopes', 'sourceReview')},
                        'files': [{'relativePath': n, 'sha256': v} for n, v in sorted(files.items())],
                        'producerAuthenticated': False, 'installedPackageAuthenticated': False,
                        'testPassClaimed': False, 'completeControlCoverageClaimed': False,
                        'timedDeviceChangeObserved': False})
    return reports, all_files


def validate_human_history_bindings(android, android_scopes, observations, declarations, evidence):
    result = {}
    observed = {x['requirementId']: x for x in observations['observations']}
    declared = {x['requirementId']: x for x in declarations.get('declarations', [])}
    for report in android.get('humanObservationBindings', []):
        scope = report['requirementId']
        require(scope in SCOPES and scope in android_scopes and scope not in result and scope in observed,
                'Unknown human history observation scope')
        observation = observed[scope]
        review = declared.get(scope, {}).get('interpretationReview')
        require(observation.get('outcome') in ('Partial', 'Inconclusive') and isinstance(review, dict),
                'Human observation requires a named interpretation, never a raw pass')
        require(isinstance(review.get('rationale'), str) and review['rationale'].strip() and
                review.get('reviewer') == report['sourceReview']['reviewer'], 'Human interpretation reviewer differs')
        obs_refs = {f['relativePath']: f['sha256'] for f in observation['files']}
        review_refs = {f['relativePath']: f['sha256'] for f in review.get('evidence', [])}
        for ref in report['files']:
            require(obs_refs.get(ref['relativePath']) == review_refs.get(ref['relativePath']) == ref['sha256'],
                    'Human interpretation must retain every provenance/source/corroboration reference')
            require(sha(evidence.read(ref['relativePath'])) == ref['sha256'], 'Human evidence changed after audit')
        result[scope] = report
    return result
