# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Static gauge labels and declared percentage scale; never dynamic device evidence."""
import io
import json
import re
from zipfile import ZipFile
from audit_android import require, xml
from build_help import keys, strict_object, sha

LABEL_SCOPE = 'extension.controls.06.every-gauge-labels'
SCALE_SCOPE = 'extension.controls.06.device-min-max'
GAUGE_SCOPES = {LABEL_SCOPE, SCALE_SCOPE}
RESOURCES = ('customdevice_radialgauge_title', 'customdevice_radialgauge_gauge',
             'customdevice_radialgauge_label_one', 'customdevice_radialgauge_label_two',
             'customdevice_radialgauge_label_three')


def declared_gauges(data):
    root = xml(data)
    result = {}
    for page in root.findall('./layouts/layout'):
        for gauge in page.findall('.//radialgauge'):
            key = (page.get('id'), gauge.get('id'))
            require(all(isinstance(k, str) and k for k in key) and key not in result,
                    'Require uniquely named documentary gauges in named pages')
            prop = re.fullmatch(r'\{([A-Za-z_][A-Za-z0-9_]*)\}', gauge.get('radialvalue', ''))
            require(prop is not None and all(gauge.get(k) for k in
                    ('label', 'line1label', 'line2label', 'line3label')),
                    'Documentary gauge requires one property and four defined labels')
            result[key] = (prop.group(1), gauge, page)
    require(result and len(result) <= 128 and len(root.findall('.//radialgauge')) == len(result),
            'Every bounded documentary gauge must belong to a named page')
    return result


def percentage_property(data, prop):
    source = data.decode('utf-8-sig')
    # Keep quoted literals, but remove comments so commented metadata cannot qualify.
    source = re.sub(r'"(?:\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/',
                    lambda m: m.group(0) if m.group(0).startswith('"') else '', source,
                    flags=re.DOTALL)
    blocks = re.findall(r'\[EntityProperty\s*\(([^()]*)\)\]\s*'
                        r'(?:\[[^\]]*\]\s*)*public\s+(?:int|double|float|decimal)\s+\w+\b', source)
    found = []
    for block in blocks:
        attrs = re.findall(r'(\w+)\s*=\s*("[^"\n]*"|-?\d+(?:\.\d+)?)', block)
        require(len({k for k, _ in attrs}) == len(attrs), 'Duplicate gauge property metadata')
        values = dict(attrs)
        if values.get('Id') == '"' + prop + '"': found.append(values)
    require(len(found) == 1 and found[0].get('RangeMinimum') == '0' and
            found[0].get('RangeMaximum') == '100' and found[0].get('Units') == '"Percent"',
            'Static gauge scale requires exactly one declared 0..100 Percent property')


def audit_gauges(entry, old_data, current_data, review, attestation, evidence, files,
                 originals, package_bytes, reference, page_bytes, bounds):
    old, current = declared_gauges(old_data), declared_gauges(current_data)
    gauges = entry['gauges']
    require(isinstance(gauges, list) and len(gauges) == len(current),
            'Every current gauge must be documented')
    require(attestation.get('requirementId') == entry['requirementId'] and
            attestation.get('reviewer') == review['reviewer'] and
            attestation.get('reviewedUtc') == review['reviewedUtc'] and
            attestation.get('gaugeIds') == [list(k) for k in current],
            'Require new scope-specific named gauge attestation for every declared gauge')
    old_trans = reference(review['originalTranslations'], evidence, files)
    translations = reference(review['currentTranslations'], evidence, files)
    path = review['translationPackagePath']
    require(isinstance(path, str) and re.fullmatch(r'translations/[A-Za-z0-9_-]+\.json', path),
            'Require exact packaged gauge translation path')
    with ZipFile(io.BytesIO(package_bytes)) as archive:
        members = [i for i in archive.infolist() if i.filename == path]
        require(len(members) == 1 and members[0].file_size <= 4 * 1024 * 1024,
                'Require one bounded packaged gauge translation')
        require(archive.read(members[0]) == translations, 'Gauge translation differs from package')
    require(old_trans == translations, 'Gauge translation implementation changed')
    language = json.loads(translations, object_pairs_hook=strict_object)
    require(isinstance(language, dict), 'Gauge translations must be a dictionary')
    old_props = reference(review['originalPropertyDefinitions'], evidence, files)
    props = reference(review['currentPropertyDefinitions'], evidence, files)
    seen, selected = set(), set()
    for gauge in gauges:
        keys(gauge, ('pageId', 'controlId', 'propertyId', 'screenshot', 'hierarchy', 'appPackage',
                     'containerBounds', 'gaugeBounds', 'labelBounds', 'labelText', 'scale'))
        key = (gauge['pageId'], gauge['controlId'])
        require(key in current and key in old and key not in seen, 'Unknown or duplicate gauge')
        seen.add(key)
        require(gauge['propertyId'] == current[key][0] == old[key][0] and
                page_bytes(old[key][2]) == page_bytes(current[key][2]),
                'Documentary gauge page/property implementation changed')
        scale = gauge['scale']; keys(scale, ('minimum', 'maximum', 'units'))
        require(type(scale['minimum']) is int and type(scale['maximum']) is int and
                scale == {'minimum': 0, 'maximum': 100, 'units': 'Percent'},
                'Only static declared percentage scale is supported')
        percentage_property(old_props, gauge['propertyId']); percentage_property(props, gauge['propertyId'])
        title = current[key][1].get('label')
        if title.startswith('^'): title = language.get(title[1:])
        require(isinstance(title, str) and title.strip(), 'Missing defined gauge title')
        texts = gauge['labelText']
        require(isinstance(texts, list) and len(texts) == 4 and texts[0] == title and
                all(isinstance(v, str) and v.strip() for v in texts), 'Gauge labels incomplete or undefined')
        png = reference(gauge['screenshot'], evidence, files)
        hierarchy = reference(gauge['hierarchy'], evidence, files)
        from pathlib import Path
        for ref, data in ((gauge['screenshot'], png), (gauge['hierarchy'], hierarchy)):
            require(originals.get(Path(ref['relativePath']).name) == (sha(data), len(data)),
                    'Gauge capture differs from original archive inventory')
        require(png.startswith(b'\x89PNG\r\n\x1a\n') and len(png) > 24 and png[12:16] == b'IHDR',
                'Documentary gauge requires PNG')
        import struct
        width, height = struct.unpack('>II', png[16:24]); tree = xml(hierarchy); app = gauge['appPackage']
        require(tree.tag == 'hierarchy' and isinstance(app, str) and app, 'Invalid gauge application')
        container_rect = bounds(gauge['containerBounds'], width, height)
        containers = [n for n in tree.iter('node') if n.get('bounds') == gauge['containerBounds']]
        require(len(containers) == 1, 'Gauge container missing or ambiguous')
        container = containers[0]
        require(container.get('class') == 'android.view.ViewGroup' and
                container.get('resource-id', '') == '' and
                all(n.get('package') == app and n.get('password') == 'false' for n in
                    [container, *container.iterancestors('node'), *container.iterdescendants('node')]),
                'Gauge path crosses application/password boundary or unknown container')
        label_bounds = gauge['labelBounds']
        require(isinstance(label_bounds, list) and len(label_bounds) == 4, 'Gauge label bounds incomplete')
        ordered_bounds = [label_bounds[0], gauge['gaugeBounds'], *label_bounds[1:]]
        require(len(set(ordered_bounds)) == 5 and len(container) == 5, 'Require exact five-child native gauge')
        expected_texts = [texts[0], '', *texts[1:]]
        for node, resource, rect, text in zip(container, RESOURCES, ordered_bounds, expected_texts):
            require(node.tag == 'node' and len(node) == 0 and node.get('resource-id') == app + ':id/' + resource and
                    node.get('bounds') == rect and node.get('text', '') == text,
                    'Gauge native labels, bounds or child order differs')
            b = bounds(rect, width, height)
            require(container_rect[0] <= b[0] < b[2] <= container_rect[2] and
                    container_rect[1] <= b[1] < b[3] <= container_rect[3], 'Gauge child outside container')
        require(sum(n.get('resource-id') == app + ':id/' + RESOURCES[1] for n in tree.iter('node')) ==
                sum(k[0] == key[0] for k in current), 'Every native gauge on the page must be documented')
        selection = (gauge['hierarchy']['sha256'], gauge['containerBounds'])
        require(selection not in selected, 'One native gauge cannot support multiple declarations')
        selected.add(selection)
    require(seen == set(current), 'Gauge enumeration incomplete')
    return {'gauges': gauges, 'status': 'StaticDocumentaryGaugeLabelsReviewed' if entry['requirementId'] == LABEL_SCOPE
            else 'StaticDocumentaryGaugeScaleReviewed', 'dynamicDeviceChangeObserved': False,
            'physicalExtremesObserved': False, 'staticDeclaredScaleOnly': True}
