# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Synthetic documentary grouping guards; no device or submission execution."""
import copy
import io
import json
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile
from PIL import Image

from build_help import sha
from documentary_android import SCOPE, audit_documentary, validate_documentary_bindings
from review_android import validate_observation_bindings
from audit_android import Evidence


class DocumentaryGroupingTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name); self.evidence = self.root / 'evidence'; self.evidence.mkdir()
        self.ui = b'<uidefinition><layouts><layout id="Energy"><controls><buttongroup><button id="Back"/><button id="Today"/><button id="Forward"/></buttongroup></controls></layout></layouts></uidefinition>'
        self.package = self.root / 'driver.pkg'
        self.package_ui(self.ui)
        self.settings = {'evidence': str(self.evidence), 'package': str(self.package), 'candidate': str(self.root / 'candidate.json')}
        self.candidate = {'identity': {'packageSha256': sha(self.package.read_bytes()), 'sourceCommit': 'a'*40}}
        self.save_candidate()
        self.identity = {'deviceId': 7, 'version': '1.1.007.0001', 'owner': 'b'*32, 'completedUtc': '2026-01-01T00:00:00Z',
                         'installedPackageSha256': None, 'testPassClaimed': False, 'candidateAccepted': False}
        image = io.BytesIO(); Image.new('RGB', (300,100)).save(image, format='PNG')
        self.png = image.getvalue()
        self.rects = ['[0,0][100,100]', '[100,0][200,100]', '[200,0][300,100]']
        self.hierarchy = ('<hierarchy><node package="app" password="false">' + ''.join(
            '<node package="app" password="false" resource-id="app:id/button" bounds="'+b+'"/>' for b in self.rects) + '</node></hierarchy>').encode()
        self.result = {'purpose': 'Factual help figures only', **{k:v for k,v in self.identity.items() if k != 'installedPackageSha256'},
                       'originalStateRestored': True, 'originalHomeRestorationPending': False, 'reservationsReleased': True,
                       'selectedIdentityUnchanged': True, 'screenshots': [
                           {'name':'screen.png', 'sha256':sha(self.png),'bytes':len(self.png)},
                           {'name':'hierarchy.xml','sha256':sha(self.hierarchy),'bytes':len(self.hierarchy)}]}
        self.result['screenshots'].extend([
            {'name':'restore-original-home.png','sha256':sha(self.png),'bytes':len(self.png)},
            {'name':'restore-original-home.masked.xml','sha256':sha(self.hierarchy),'bytes':len(self.hierarchy)}])
        self.entry = {'requirementId': SCOPE, 'identity': self.identity,
                      'archivePrefix':'documentary/',
                      'restoration':{'screenshot':self.write('restore-original-home.png',self.png),
                                     'hierarchy':self.write('restore-original-home.masked.xml',self.hierarchy)},
                      'captureResult': self.write('result.json', json.dumps(self.result).encode()),
                      'sourceReview': {'reviewer': 'Synthetic named reviewer', 'reviewedUtc': '2026-01-02T00:00:00Z',
                          'rationale':'Static grouping only; installed original package not authenticated.',
                          'originalSourceCommit':'c'*40,'currentSourceCommit':'a'*40,
                          'originalUiDefinition':self.write('old.xml',self.ui),'currentUiDefinition':self.write('current.xml',self.ui),
                          'attestation':self.write('source-review.json',json.dumps({'originalSourceCommit':'c'*40,'currentSourceCommit':'a'*40}).encode())},
                      'groups':[{'pageId':'Energy','groupIndex':1,'controlIds':['Back','Today','Forward'],
                                 'screenshot':self.write('screen.png',self.png),'hierarchy':self.write('hierarchy.xml',self.hierarchy),
                                 'appPackage':'app','resourceId':'app:id/button','controlBounds':self.rects}]}
        self.save_archive()

    def write(self, name, data):
        (self.evidence/name).write_bytes(data)
        return {'relativePath':name,'sha256':sha(data)}

    def package_ui(self, ui):
        with ZipFile(self.package,'w') as z:z.writestr('UiDefinitions/UiDefinition.xml',ui)

    def save_candidate(self):
        (self.root/'candidate.json').write_text(json.dumps(self.candidate))
        self.candidate_pin=sha((self.root/'candidate.json').read_bytes())

    def save_archive(self):
        archive=io.BytesIO()
        with ZipFile(archive,'w') as z:
            z.writestr('documentary/result.json',(self.evidence/'result.json').read_bytes())
            for x in self.result['screenshots']:
                z.writestr('documentary/'+x['name'],(self.evidence/x['name']).read_bytes())
        self.entry['captureArchive']=self.write('original.zip',archive.getvalue())

    def result_ref(self):
        self.entry['captureResult']=self.write('result.json',json.dumps(self.result).encode())
        self.save_archive()

    def audit(self, mode='declared-gaps'):
        return audit_documentary(self.settings,[self.entry],self.candidate_pin,mode)

    def validation_inputs(self):
        reports,_=self.audit(); files=reports[0]['files']
        android={'runs':[{'runId':'d'*32,'status':'InterruptedAndroidEvidencePreserved','files':[]}],
                 'documentaryBindings':reports,'observationBindings':[]}
        observations={'observations':[{'requirementId':SCOPE,'outcome':'Partial','files':files}]}
        declarations={'declarations':[{'requirementId':SCOPE,'reason':'Documentary static fact only.',
            'interpretationReview':{'reviewer':'Synthetic named reviewer','rationale':'All defined groups visually reviewed; no new execution.', 'evidence':files}}]}
        return android,observations,declarations

    def test_static_documentary_review_preserves_original_identity_and_no_authentication(self):
        reports,files=self.audit()
        self.assertEqual(9,len(files)); self.assertIsNone(reports[0]['identity']['installedPackageSha256'])
        self.assertFalse(reports[0]['producerAuthenticated']);self.assertFalse(reports[0]['testPassClaimed'])
        self.assertFalse(reports[0]['installedPackageAuthenticated'])
        a,o,d=self.validation_inputs()
        self.assertEqual({SCOPE},set(validate_observation_bindings(a,{SCOPE},o,d,self.evidence)))

    def test_complete_mode_refuses_and_absent_entries_do_not_change_complete_audit(self):
        with self.assertRaises(ValueError):self.audit('complete')
        self.assertEqual(([],{}),audit_documentary({},[],None,'complete'))

    def test_unknown_dynamic_timing_recovery_endurance_gauge_scopes_refused(self):
        for scope in ['extension.controls.06.every-gauge-labels','extension.views.06','extension.system.03','extension.controls.09.selector']:
            with self.subTest(scope=scope):
                self.entry['requirementId']=scope
                with self.assertRaises(ValueError):self.audit()

    def test_duplicate_scope_and_extra_schema_fields_refused(self):
        with self.assertRaises(ValueError):audit_documentary(self.settings,[self.entry,self.entry],self.candidate_pin,'declared-gaps')
        self.entry['syntheticNUnitCompletion']={}
        with self.assertRaises(ValueError):self.audit()

    def test_original_claims_cannot_be_changed_to_pass_or_package_authentication(self):
        for k,v in [('testPassClaimed',True),('candidateAccepted',True),('installedPackageSha256','a'*64)]:
            before=self.identity[k];self.identity[k]=v
            with self.subTest(k=k),self.assertRaises(ValueError):self.audit()
            self.identity[k]=before

    def test_original_result_identity_purpose_and_time_are_required(self):
        for k,v in [('deviceId',8),('owner','c'*32),('version','2.0.5.0'),('purpose','Acceptance test'),('completedUtc','2099-01-01T00:00:00Z')]:
            before=self.result[k];self.result[k]=v;self.result_ref()
            with self.subTest(k=k),self.assertRaises(ValueError):self.audit()
            self.result[k]=before;self.result_ref()

    def test_each_restoration_and_release_flag_required(self):
        for k in ['originalStateRestored','reservationsReleased','selectedIdentityUnchanged','originalHomeRestorationPending']:
            before=self.result[k];self.result[k]=not before;self.result_ref()
            with self.subTest(k=k),self.assertRaises(ValueError):self.audit()
            self.result[k]=before;self.result_ref()

    def test_review_requires_named_reviewer_rationale_valid_time_and_current_commit(self):
        review=self.entry['sourceReview']
        for k,v in [('reviewer',''),('rationale',''),('reviewedUtc','2099-01-01T00:00:00Z'),('reviewedUtc','2025-01-01T00:00:00Z'),('originalSourceCommit','bad'),('currentSourceCommit','f'*40)]:
            before=review[k];review[k]=v
            with self.subTest(k=k),self.assertRaises(ValueError):self.audit()
            review[k]=before

    def test_package_current_ui_and_attestation_bound_to_candidate(self):
        before=self.candidate['identity']['packageSha256'];self.candidate['identity']['packageSha256']='0'*64;self.save_candidate()
        with self.assertRaises(ValueError):self.audit()
        self.candidate['identity']['packageSha256']=before;self.save_candidate()
        self.entry['sourceReview']['currentUiDefinition']=self.write('current.xml',self.ui.replace(b'Today',b'Other'))
        with self.assertRaises(ValueError):self.audit()
        self.entry['sourceReview']['currentUiDefinition']=self.write('current.xml',self.ui)
        self.entry['sourceReview']['attestation']=self.write('source-review.json',b'{}')
        with self.assertRaises(ValueError):self.audit()

    def test_old_page_change_is_not_static_current_evidence(self):
        self.entry['sourceReview']['originalUiDefinition']=self.write('old.xml',self.ui.replace(b'Today',b'Other'))
        with self.assertRaises(ValueError):self.audit()

    def test_all_current_groups_must_be_explicit_and_known(self):
        groups=self.entry['groups']
        for replacement in [[],[copy.deepcopy(groups[0]),copy.deepcopy(groups[0])]]:
            self.entry['groups']=replacement
            with self.assertRaises(ValueError):self.audit()
        self.entry['groups']=groups
        for k,v in [('pageId','Other'),('groupIndex',2),('controlIds',['Back','Wrong','Forward'])]:
            before=groups[0][k];groups[0][k]=v
            with self.subTest(k=k),self.assertRaises(ValueError):self.audit()
            groups[0][k]=before

    def test_each_reference_must_match_and_exist(self):
        reports,files=self.audit()
        for name in files:
            p=self.evidence/name;before=p.read_bytes();p.write_bytes(before+b'changed')
            with self.subTest(name=name),self.assertRaises(ValueError):self.audit()
            p.write_bytes(before)
            p.unlink()
            with self.subTest(missing=name),self.assertRaises((OSError,ValueError)):self.audit()
            p.write_bytes(before)

    def test_capture_hash_in_original_receipt_not_just_new_reference(self):
        self.entry['groups'][0]['screenshot']=self.write('screen.png',self.png+b'changed')
        with self.assertRaises(ValueError):self.audit()

    def test_group_bounds_are_unique_in_capture_and_adjacent(self):
        g=self.entry['groups'][0];original=g['controlBounds']
        for values in [[original[0]]*3,[original[2],original[1],original[0]],['[0,0][400,100]',*original[1:]]]:
            g['controlBounds']=values
            with self.assertRaises(ValueError):self.audit()
        g['controlBounds']=original

    def update_hierarchy(self, data):
        self.entry['groups'][0]['hierarchy']=self.write('hierarchy.xml',data)
        self.result['screenshots'][1].update(sha256=sha(data),bytes=len(data));self.result_ref()

    def test_group_controls_must_share_parent_same_application_and_nonpassword(self):
        for data in [self.hierarchy.replace(b'<node package="app" password="false">',b'<node><node/></node><node package="wrong" password="false">'),
                     self.hierarchy.replace(b'password="false" resource-id',b'password="true" resource-id'),
                     self.hierarchy.replace(b'package="app" password="false" resource-id',b'package="other" password="false" resource-id'),
                     self.hierarchy.replace(b'</node></hierarchy>',b'</node><node package="app" password="false" resource-id="app:id/button" bounds="[0,0][100,100]"/></hierarchy>')]:
            self.update_hierarchy(data)
            with self.assertRaises(ValueError):self.audit()
        self.update_hierarchy(self.hierarchy)
        split=b'<hierarchy>'+b''.join(b'<node>'+n+b'</node>' for n in [
            ('<node package="app" password="false" resource-id="app:id/button" bounds="'+b+'"/>').encode() for b in self.rects])+b'</hierarchy>'
        self.update_hierarchy(split)
        with self.assertRaises(ValueError):self.audit()

    def test_binding_never_accepts_pass_failed_na_or_priorpass(self):
        for outcome in ['Passed','ReviewedPriorPass','Failed','NotApplicable']:
            a,o,d=self.validation_inputs();o['observations'][0]['outcome']=outcome
            with self.subTest(outcome=outcome),self.assertRaises(ValueError):validate_observation_bindings(a,{SCOPE},o,d,self.evidence)

    def test_binding_requires_every_reference_in_observation_and_named_interpretation(self):
        for change in ['observation','review','reviewer','missing-review']:
            a,o,d=self.validation_inputs()
            if change=='observation':o['observations'][0]['files']=o['observations'][0]['files'][:-1]
            elif change=='review':d['declarations'][0]['interpretationReview']['evidence']=d['declarations'][0]['interpretationReview']['evidence'][:-1]
            elif change=='reviewer':d['declarations'][0]['interpretationReview']['reviewer']='Other'
            else:del d['declarations'][0]['interpretationReview']
            with self.subTest(change=change),self.assertRaises(ValueError):validate_observation_bindings(a,{SCOPE},o,d,self.evidence)

    def test_documentary_does_not_authorize_other_interrupted_scope_or_synthetic_run_binding(self):
        a,o,d=self.validation_inputs();other='extension.views.06'
        o['observations'].append({'requirementId':other,'outcome':'Partial','files':[]})
        d['declarations'].append({'requirementId':other,'reason':'Missing','interpretationReview':{'reviewer':'Synthetic named reviewer','rationale':'Not actually observed','evidence':[]}})
        with self.assertRaises(ValueError):validate_observation_bindings(a,{SCOPE,other},o,d,self.evidence)
        a,o,d=self.validation_inputs()
        a['observationBindings']=[{'requirementId':SCOPE,'runId':'d'*32,'files':[]}]
        with self.assertRaises(ValueError):validate_observation_bindings(a,{SCOPE},o,d,self.evidence)

    def test_changed_evidence_between_audit_and_binding_refused(self):
        a,o,d=self.validation_inputs();p=self.evidence/'screen.png';p.write_bytes(p.read_bytes()+b'changed')
        with self.assertRaises(ValueError):validate_observation_bindings(a,{SCOPE},o,d,self.evidence)

    def integrated_audit(self, interrupted=False):
        from test_audit_android import AndroidEvidenceTests
        from test_interrupted_android import interrupt, inventory
        from review_android import audit_runs
        a=AndroidEvidenceTests();a.setUp();self.addCleanup(a.doCleanups)
        self.candidate={'schemaVersion':1,'identity':{**a.identity,'packageSha256':sha(self.package.read_bytes()),'sourceCommit':'a'*40},
                        'packageRequirements':a.candidate['packageRequirements']}
        self.save_candidate()
        for name in ['context.json','coverage.json','completion.json','producer-pin.json','check/observation.json']:
            value=json.loads((a.root/name).read_bytes())
            value['PackageSha256']=self.candidate['identity']['packageSha256']
            if 'ReleaseSourceCommit' in value:value['ReleaseSourceCommit']='a'*40
            a.write(name,value)
        run={'runId':a.run,'assembly':a.assembly,'assemblySha256':a.assembly_hash,
             'discoverySha256':a.discovery_hash,'producerManifestSha256':a.manifest_hash}
        if interrupted:
            interrupt(a);run['interruptedFiles']=inventory(a.root)
        pins={'schemaVersion':1,'candidateSha256':self.candidate_pin,'runs':[run],'documentaryBindings':[self.entry]}
        path=self.root/'pins.json';path.write_text(json.dumps(pins))
        self.settings['androidEvidence']=[{'runId':a.run,'path':str(a.root)}]
        return a,path,audit_runs(self.settings,path,sha(path.read_bytes()),self.candidate_pin,review_mode='declared-gaps')

    def test_real_complete_producer_audit_and_documentary_files_coexist_without_auth_claim(self):
        from review_android import audit_runs
        a,path,report=self.integrated_audit()
        self.assertEqual('AndroidEvidenceAudited',report['runs'][0]['status'])
        self.assertEqual(9,len(report['reviewFiles']))
        self.assertFalse(report['producerAuthenticated'])
        self.assertEqual([],report['officialRequirementsSatisfied'])
        (a.root/'completion.json').unlink()
        with self.assertRaises((OSError,ValueError)):
            audit_runs(self.settings,path,sha(path.read_bytes()),self.candidate_pin,review_mode='declared-gaps')

    def test_real_interrupted_audit_retains_originals_and_documentary_scope_only(self):
        a,path,report=self.integrated_audit(interrupted=True)
        self.assertEqual('InterruptedAndroidEvidencePreserved',report['runs'][0]['status'])
        self.assertFalse((a.root/'completion.json').exists())
        files=report['documentaryBindings'][0]['files']
        obs={'observations':[{'requirementId':SCOPE,'outcome':'Partial','files':files}]}
        decl={'declarations':[{'requirementId':SCOPE,'reason':'Static capture only','interpretationReview':{
            'reviewer':'Synthetic named reviewer','rationale':'Exact static grouping reviewed','evidence':files}}]}
        self.assertEqual({SCOPE},set(validate_observation_bindings(report,{SCOPE},obs,decl,self.evidence)))
        (a.root/'context.json').write_bytes((a.root/'context.json').read_bytes()+b'changed')
        from review_android import audit_runs
        with self.assertRaises(ValueError):
            audit_runs(self.settings,path,sha(path.read_bytes()),self.candidate_pin,review_mode='declared-gaps')

    def test_archive_result_inventory_and_restoration_cannot_be_substituted(self):
        original=self.entry['captureArchive']
        for mutate in ['result','capture']:
            out=io.BytesIO()
            with ZipFile(self.evidence/'original.zip') as old,ZipFile(out,'w') as new:
                for item in old.infolist():
                    data=old.read(item.filename)
                    if item.filename==('documentary/result.json' if mutate=='result' else 'documentary/hierarchy.xml'):data+=b'changed'
                    new.writestr(item.filename,data)
            self.entry['captureArchive']=self.write('altered.zip',out.getvalue())
            with self.subTest(mutate=mutate),self.assertRaises(ValueError):self.audit()
        self.entry['captureArchive']=original
        self.entry['restoration']['screenshot']=self.entry['groups'][0]['screenshot']
        with self.assertRaises(ValueError):self.audit()

    def test_portable_freeze_copies_documentary_archive_sources_restoration_and_reaudits(self):
        from freeze_review_inputs import freeze,load
        from review_android import audit_runs
        a,pins_path,report=self.integrated_audit(interrupted=True)
        for field,value in [('policy',{'schemaVersion':1,'requirements':[]}),('inventory',{'schemaVersion':1}),('mapping',{'schemaVersion':1})]:
            path=self.root/(field+'.json');path.write_text(json.dumps(value));self.settings[field]=str(path)
        template=self.root/'template.pdf';template.write_bytes(b'Synthetic freeze input, not an official PDF');self.settings['template']=str(template)
        self.candidate['identity']['policySha256']=sha(Path(self.settings['policy']).read_bytes())
        self.candidate['identity']['templateSha256']=sha(template.read_bytes());self.save_candidate()
        pins=json.loads(pins_path.read_bytes());pins['candidateSha256']=self.candidate_pin;pins_path.write_text(json.dumps(pins))
        files=report['documentaryBindings'][0]['files']
        observations=self.root/'observations.json';observations.write_text(json.dumps({'schemaVersion':1,'observations':[{
            'requirementId':SCOPE,'outcome':'Partial','files':files}]}));self.settings['observations']=str(observations)
        declarations=self.root/'declarations.json';declarations.write_text(json.dumps({'schemaVersion':1,'declarations':[]}))
        self.settings.update(schemaVersion=1,output=str(self.root/'unused-output'),title='Synthetic retention',author='Synthetic reviewer')
        settings=self.root/'settings.json';settings.write_text(json.dumps(self.settings));dest=self.root/'frozen'
        result=freeze(settings,dest,self.candidate_pin,sha(Path(self.settings['inventory']).read_bytes()),
            sha(Path(self.settings['mapping']).read_bytes()),'a'*40,android_pins=pins_path,android_pins_sha256=sha(pins_path.read_bytes()),
            review_mode='declared-gaps',declarations=declarations,declarations_sha256=sha(declarations.read_bytes()))
        self.assertFalse(result['testsExecuted']);self.assertFalse(result['submissionReady'])
        _,retained,options=load(dest,result['inputsSha256'],completed=True)
        copied=audit_runs(retained,options['androidPins'],options['androidPinsSha256'],self.candidate_pin,review_mode='declared-gaps')
        self.assertEqual('InterruptedAndroidEvidencePreserved',copied['runs'][0]['status'])
        self.assertEqual(report['documentaryBindings'],copied['documentaryBindings'])
        for ref in files:self.assertEqual((self.evidence/ref['relativePath']).read_bytes(),(dest/'evidence'/ref['relativePath']).read_bytes())


if __name__=='__main__':unittest.main()
