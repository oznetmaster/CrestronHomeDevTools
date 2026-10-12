# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Scoped static documentary grouping review; never authenticate an executed test."""
from copy import deepcopy
from datetime import datetime, timezone
from pathlib import Path
import io
import re
import struct
from zipfile import ZipFile

from audit_android import Evidence, digest, require, timestamp, xml
from build_help import keys, sha
from self_test_form import pinned_json

SCOPE = 'extension.controls.08.all-declared-button-groups'


def reference(ref, evidence, files):
    keys(ref, ('relativePath', 'sha256'))
    name, value = ref['relativePath'], digest(ref['sha256'])
    data = evidence.read(name)
    require(sha(data) == value, 'Documentary evidence changed')
    require(name not in files or files[name] == value, 'Conflicting documentary evidence')
    files[name] = value
    return data


def named_review(review):
    require(all(isinstance(review.get(k), str) and review[k].strip() for k in ('reviewer', 'rationale')),
            'Named documentary source review required')
    require(timestamp(review['reviewedUtc']) <= datetime.now(timezone.utc), 'Documentary review is in the future')


def page_bytes(page):
    page = deepcopy(page)
    for node in page.iter():
        if node.text is not None and not node.text.strip(): node.text = None
        if node.tail is not None and not node.tail.strip(): node.tail = None
    from lxml.etree import tostring
    return tostring(page, method='c14n', with_comments=False)


def declared_groups(root):
    result = {}
    for page in root.findall('./layouts/layout'):
        page_id = page.get('id')
        groups = page.findall('.//buttongroup')
        for index, group in enumerate(groups, 1):
            children = list(group)
            ids = [node.get('id') for node in children]
            require(page_id and children and all(node.tag == 'button' for node in children) and
                    all(isinstance(v, str) and v for v in ids) and len(set(ids)) == len(ids),
                    'Documentary scope requires named button groups only')
            key = (page_id, index)
            require(key not in result, 'Duplicate documentary layout identity')
            result[key] = (ids, page)
    require(result and len(result) <= 128 and len(root.findall('.//buttongroup')) == len(result),
            'Require bounded groups wholly declared in named layouts')
    return result


def bounds(value, width, height):
    match = re.fullmatch(r'\[(\d+),(\d+)\]\[(\d+),(\d+)\]', value or '')
    require(match is not None, 'Invalid documentary control bounds')
    x1, y1, x2, y2 = map(int, match.groups())
    require(0 <= x1 < x2 <= width and 0 <= y1 < y2 <= height, 'Documentary bounds outside capture')
    return x1, y1, x2, y2


def audit_documentary(settings, entries, candidate_sha256, review_mode):
    require(isinstance(entries, list) and len(entries) <= 128, 'Invalid documentary bindings')
    if not entries: return [], {}
    require(review_mode == 'declared-gaps', 'Documentary review requires declared-gaps mode')
    _, candidate = pinned_json(settings['candidate'], candidate_sha256)
    package = Path(settings['package'])
    Evidence.check_path(package)
    require(package.is_file() and package.stat().st_size <= 64 * 1024 * 1024, 'Missing or oversized documentary candidate package')
    package_bytes = package.read_bytes()
    require(sha(package_bytes) == digest(candidate['identity']['packageSha256']), 'Documentary candidate package changed')
    with ZipFile(io.BytesIO(package_bytes)) as archive:
        matches = [i for i in archive.infolist() if Path(i.filename).name.casefold() == 'uidefinition.xml']
        require(len(matches) == 1 and matches[0].file_size <= 32 * 1024 * 1024, 'Require one bounded packaged UI definition')
        packaged_ui = archive.read(matches[0])
    evidence, all_files, reports, scopes = Evidence(settings['evidence']), {}, [], set()
    for entry in entries:
        keys(entry, ('requirementId', 'identity', 'captureResult', 'captureArchive', 'archivePrefix', 'restoration', 'sourceReview', 'groups'))
        scope = entry['requirementId']
        require(scope == SCOPE and scope not in scopes, 'Documentary review supports only one static grouping scope')
        scopes.add(scope)
        files = {}
        identity = entry['identity']
        keys(identity, ('deviceId', 'version', 'owner', 'completedUtc', 'installedPackageSha256', 'testPassClaimed', 'candidateAccepted'))
        require(type(identity['deviceId']) is int and identity['deviceId'] > 0 and
                isinstance(identity['version'], str) and re.fullmatch(r'\d+\.\d+\.\d+\.\d+', identity['version']) and
                isinstance(identity['owner'], str) and re.fullmatch(r'[a-f0-9]{32}', identity['owner']),
                'Invalid original documentary identity')
        require(identity['installedPackageSha256'] is None and identity['testPassClaimed'] is False and
                identity['candidateAccepted'] is False, 'Documentary review cannot authenticate installed bytes or test acceptance')
        import json
        from build_help import strict_object
        result_bytes = reference(entry['captureResult'], evidence, files)
        result = json.loads(result_bytes, object_pairs_hook=strict_object)
        require(result.get('purpose') == 'Factual help figures only' and
                all(result.get(k) == identity[k] for k in ('deviceId', 'version', 'owner', 'completedUtc')) and
                result.get('testPassClaimed') is False and result.get('candidateAccepted') is False,
                'Original documentary purpose or identity differs')
        require(all(result.get(k) is True for k in ('originalStateRestored', 'reservationsReleased', 'selectedIdentityUnchanged')) and
                result.get('originalHomeRestorationPending') is False, 'Original documentary restoration is unverified')
        require(timestamp(identity['completedUtc']) <= datetime.now(timezone.utc), 'Documentary capture is in the future')
        review = entry['sourceReview']
        keys(review, ('reviewer', 'reviewedUtc', 'rationale', 'originalSourceCommit', 'currentSourceCommit',
                      'originalUiDefinition', 'currentUiDefinition', 'attestation'))
        named_review(review)
        require(timestamp(review['reviewedUtc']) >= timestamp(identity['completedUtc']), 'Documentary review predates capture')
        require(isinstance(review['originalSourceCommit'], str) and re.fullmatch(r'[a-f0-9]{40}', review['originalSourceCommit']) and
                review['currentSourceCommit'] == candidate['identity']['sourceCommit'], 'Documentary source identity differs')
        old_data = reference(review['originalUiDefinition'], evidence, files)
        current_data = reference(review['currentUiDefinition'], evidence, files)
        require(current_data == packaged_ui, 'Documentary current layout is not the exact packaged UI')
        attestation = json.loads(reference(review['attestation'], evidence, files), object_pairs_hook=strict_object)
        require(attestation.get('originalSourceCommit') == review['originalSourceCommit'] and
                attestation.get('currentSourceCommit') == review['currentSourceCommit'], 'Documentary attestation identifies other sources')
        old, current = declared_groups(xml(old_data)), declared_groups(xml(current_data))
        require(isinstance(entry['groups'], list) and len(entry['groups']) == len(current), 'Every current group must be documented')
        capture_inventory = result.get('screenshots')
        require(isinstance(capture_inventory, list) and 0 < len(capture_inventory) <= 4096, 'Missing original documentary capture inventory')
        originals = {}
        for item in capture_inventory:
            keys(item, ('name', 'sha256', 'bytes'))
            require(isinstance(item['name'], str) and item['name'] not in originals and type(item['bytes']) is int and item['bytes'] > 0,
                    'Invalid documentary original capture entry')
            originals[item['name']] = (digest(item['sha256']), item['bytes'])
        archive_bytes = reference(entry['captureArchive'], evidence, files)
        prefix = entry['archivePrefix']
        require(isinstance(prefix, str) and re.fullmatch(r'[A-Za-z0-9_-]+/', prefix), 'Invalid documentary archive prefix')
        with ZipFile(io.BytesIO(archive_bytes)) as archive:
            members = archive.infolist()
            require(len(members) <= 4096 and len({m.filename.casefold() for m in members}) == len(members) and
                    sum(m.file_size for m in members) <= 128 * 1024 * 1024 and
                    all(m.file_size <= 32 * 1024 * 1024 for m in members), 'Oversized or ambiguous documentary archive')
            require(archive.read(prefix + 'result.json') == result_bytes, 'Original archived documentary result differs')
            for name, (value, length) in originals.items():
                require(Path(name).name == name and '\\' not in name and ':' not in name, 'Unsafe original capture name')
                data = archive.read(prefix + name)
                require((sha(data), len(data)) == (value, length), 'Original archived capture inventory changed')
        restoration = entry['restoration']
        keys(restoration, ('screenshot', 'hierarchy'))
        for kind, expected_name in (('screenshot', 'restore-original-home.png'), ('hierarchy', 'restore-original-home.masked.xml')):
            data = reference(restoration[kind], evidence, files)
            require(Path(restoration[kind]['relativePath']).name == expected_name and
                    originals.get(expected_name) == (sha(data), len(data)), 'Missing exact original Home restoration capture')
        seen = set()
        for group in entry['groups']:
            keys(group, ('pageId', 'groupIndex', 'controlIds', 'screenshot', 'hierarchy', 'appPackage', 'resourceId', 'controlBounds'))
            key = (group['pageId'], group['groupIndex'])
            require(type(group['groupIndex']) is int and key in current and key not in seen and key in old,
                    'Unknown or duplicate documentary group')
            seen.add(key)
            require(group['controlIds'] == current[key][0] == old[key][0] and page_bytes(old[key][1]) == page_bytes(current[key][1]),
                    'Documentary page/group implementation changed')
            png = reference(group['screenshot'], evidence, files)
            hierarchy = reference(group['hierarchy'], evidence, files)
            for ref, data in ((group['screenshot'], png), (group['hierarchy'], hierarchy)):
                require(originals.get(Path(ref['relativePath']).name) == (sha(data), len(data)), 'Capture differs from original documentary receipt')
            require(png.startswith(b'\x89PNG\r\n\x1a\n') and len(png) > 24 and png[12:16] == b'IHDR', 'Documentary capture requires PNG')
            width, height = struct.unpack('>II', png[16:24])
            tree = xml(hierarchy)
            require(tree.tag == 'hierarchy' and isinstance(group['appPackage'], str) and group['appPackage'] and
                    isinstance(group['resourceId'], str) and group['resourceId'].startswith(group['appPackage'] + ':id/'),
                    'Invalid documentary hierarchy/application binding')
            require(isinstance(group['controlBounds'], list) and len(group['controlBounds']) == len(group['controlIds']) and
                    len(set(group['controlBounds'])) == len(group['controlBounds']), 'Require exact group control bounds')
            nodes, rects = [], []
            for value in group['controlBounds']:
                rects.append(bounds(value, width, height))
                matched = [n for n in tree.iter('node') if n.get('bounds') == value and n.get('resource-id') == group['resourceId'] and
                           n.get('package') == group['appPackage'] and n.get('password') == 'false']
                require(len(matched) == 1, 'Documentary grouped control is missing or ambiguous')
                nodes.append(matched[0])
            require(all(ancestor.get('package') == group['appPackage'] and ancestor.get('password') == 'false'
                        for node in nodes for ancestor in node.iterancestors('node')),
                    'Documentary group container path crosses application/password boundary')
            require(nodes[0].getparent() is not None and all(n.getparent() is nodes[0].getparent() for n in nodes),
                    'Documentary controls are not in one group container')
            require(all(a[1] == b[1] and a[3] == b[3] and a[2] <= b[0] for a, b in zip(rects, rects[1:])),
                    'Documentary group is not an ordered adjacent row')
        require(seen == set(current), 'Documentary group enumeration incomplete')
        all_files.update(files)
        reports.append({'requirementId': scope, 'status': 'StaticDocumentaryGroupingReviewed', 'identity': identity,
                        'sourceReview': review, 'groups': entry['groups'],
                        'files': [{'relativePath': k, 'sha256': v} for k, v in sorted(files.items())],
                        'producerAuthenticated': False, 'installedPackageAuthenticated': False, 'testPassClaimed': False})
    return reports, all_files


def validate_documentary_bindings(android, android_scopes, observations, declarations, evidence):
    result = {}
    observed = {x['requirementId']: x for x in observations['observations']}
    declared = {x['requirementId']: x for x in declarations.get('declarations', [])}
    for report in android.get('documentaryBindings', []):
        scope = report['requirementId']
        require(scope == SCOPE and scope in android_scopes and scope not in result and scope in observed,
                'Unknown documentary observation scope')
        observation = observed[scope]
        review = declared.get(scope, {}).get('interpretationReview')
        require(observation.get('outcome') in ('Partial', 'Inconclusive') and isinstance(review, dict),
                'Documentary evidence requires a named interpretation, never a raw pass')
        require(isinstance(review.get('rationale'), str) and review['rationale'].strip() and
                review.get('reviewer') == report['sourceReview']['reviewer'], 'Documentary interpretation reviewer differs')
        observation_refs = {f['relativePath']: digest(f['sha256']) for f in observation['files']}
        review_refs = {f['relativePath']: digest(f['sha256']) for f in review.get('evidence', [])}
        for f in report['files']:
            require(observation_refs.get(f['relativePath']) == review_refs.get(f['relativePath']) == digest(f['sha256']),
                    'Documentary interpretation must retain every exact original/source reference')
            require(sha(evidence.read(f['relativePath'])) == digest(f['sha256']), 'Documentary evidence changed after audit')
        result[scope] = report
    return result
