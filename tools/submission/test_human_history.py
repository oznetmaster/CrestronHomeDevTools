# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Synthetic firsthand history bindings; no private captures or device actions."""
import copy
import json
import shutil
import unittest
from pathlib import Path

from audit_android import Evidence
from build_help import sha
from human_history import audit_human_history, GAUGE_SCOPE, SUBPAGE_SCOPE
from review_android import audit_runs, validate_observation_bindings
from documentary_android import SCOPE, audit_documentary
from documentary_gauges import LABEL_SCOPE, SCALE_SCOPE
import test_documentary_android as grouping_fixture
import test_documentary_gauges as gauge_fixture


class HumanHistoryTests(unittest.TestCase):
    def setUp(self):
        self.f = grouping_fixture.DocumentaryGroupingTests(); self.f.setUp(); self.addCleanup(self.f.doCleanups)
        self.docs, _ = self.f.audit()
        self.make_entry(SUBPAGE_SCOPE, [SCOPE])

    def make_entry(self, scope, corroborating):
        f = self.f
        self.statement = {'quote': 'I looked at yesterday and it worked.',
            'reportedObservation': 'User reports the previous-day history working.',
            'scopeLimits': ['Instance and package not identified.', 'No repeated button sequence or numeric timing supplied.']}
        self.provenance = {'recordedUtc': '2026-01-03T00:00:00Z',
            'source': 'Synthetic direct-human conversation provenance, not machine results.', 'statements': [self.statement]}
        self.entry = {'requirementId': scope, 'provenance': f.write('human.json',json.dumps(self.provenance).encode()),
            'statementIndex':0,'quote':self.statement['quote'],'reportedBehavior':self.statement['reportedObservation'],
            'reportedIdentity':dict.fromkeys(['deviceId','version','packageSha256']),
            'limitations':self.statement['scopeLimits'][:], 'corroboratingScopes':corroborating,
            'sourceReview':{'reviewer':'Synthetic named reviewer','reviewedUtc':'2026-01-04T00:00:00Z',
                'rationale':'Qualified history observation only; no universal control/timing proof.',
                'currentSourceCommit':'a'*40,
                'originalHistorySource':f.write('old-history.cs', b'// synthetic original history selection'),
                'currentHistorySource':f.write('history.cs', b'// synthetic current history selection')}}
        self.attest()

    def attest(self):
        e=self.entry;r=e['sourceReview']
        value={k:e[k] for k in ('requirementId','provenance','statementIndex','quote','reportedBehavior','reportedIdentity','limitations','corroboratingScopes')}
        value.update({k:r[k] for k in ('reviewer','reviewedUtc','rationale','currentSourceCommit','originalHistorySource','currentHistorySource')})
        r['attestation']=self.f.write('human-review.json', json.dumps(value).encode())
        r['files']=[e['provenance'],r['attestation'],r['originalHistorySource'],r['currentHistorySource']]

    def audit(self,mode='declared-gaps'):
        return audit_human_history(self.f.settings,[self.entry],self.f.candidate_pin,mode,self.docs)

    def inputs(self):
        reports,_=self.audit();files=reports[0]['files']
        a={'runs':[{'runId':'d'*32,'status':'InterruptedAndroidEvidencePreserved','files':[]}],
            'documentaryBindings':[], 'humanObservationBindings':reports,'observationBindings':[]}
        o={'observations':[{'requirementId':self.entry['requirementId'],'outcome':'Partial','files':files}]}
        d={'declarations':[{'requirementId':self.entry['requirementId'],'reason':'Unmeasured universal scope remains.',
            'interpretationReview':{'reviewer':'Synthetic named reviewer','rationale':'Qualified firsthand history only','evidence':files}}]}
        return a,o,d

    def test_exact_quote_unknown_identity_and_no_machine_claim(self):
        reports,files=self.audit();r=reports[0]
        self.assertEqual(self.statement['quote'],r['quote']);self.assertEqual(self.entry['reportedIdentity'],r['reportedIdentity'])
        for field in ['producerAuthenticated','installedPackageAuthenticated','testPassClaimed','completeControlCoverageClaimed','timedDeviceChangeObserved']:
            self.assertFalse(r[field])
        self.assertGreater(len(files),9)
        a,o,d=self.inputs();self.assertEqual({SUBPAGE_SCOPE},set(validate_observation_bindings(a,{SUBPAGE_SCOPE},o,d,self.f.evidence)))

    def test_gauge_history_uses_both_static_audits_without_dynamic_machine_pass(self):
        g=gauge_fixture.DocumentaryGaugeTests();g.setUp();self.addCleanup(g.doCleanups);self.f=g.f
        first=copy.deepcopy(g.f.entry)
        first['sourceReview']['attestation']=g.f.write('labels-review.json',json.dumps({'originalSourceCommit':'c'*40,
            'currentSourceCommit':'a'*40,'requirementId':LABEL_SCOPE,'reviewer':'Synthetic named reviewer',
            'reviewedUtc':'2026-01-02T00:00:00Z','gaugeIds':[['Impact','Share']]}).encode())
        g.f.entry['requirementId']=SCALE_SCOPE;g.attestation()
        self.docs,_=audit_documentary(g.f.settings,[first,g.f.entry],g.f.candidate_pin,'declared-gaps')
        self.make_entry(GAUGE_SCOPE,[LABEL_SCOPE,SCALE_SCOPE]);self.assertEqual(GAUGE_SCOPE,self.audit()[0][0]['requirementId'])
        a,o,d=self.inputs();self.assertEqual({GAUGE_SCOPE},set(validate_observation_bindings(a,{GAUGE_SCOPE},o,d,self.f.evidence)))

    def test_empty_inventory_keeps_ordinary_complete_path_unchanged(self):
        self.assertEqual(([],{}),audit_human_history({},[],None,'complete',[]))
        with self.assertRaises(ValueError):audit_human_history({}, {},None,'complete',[])
        with self.assertRaises(ValueError):self.audit('complete')

    def test_unknown_timing_recovery_endurance_and_repeated_button_scopes_refused(self):
        for scope in ['extension.controls.07.repeated-button','extension.views.06','extension.system.03',SCOPE]:
            with self.subTest(scope=scope):
                self.entry['requirementId']=scope;self.attest()
                with self.assertRaises(ValueError):self.audit()

    def test_duplicate_scope_and_extra_pass_timing_completion_fields_refused(self):
        with self.assertRaises(ValueError):audit_human_history(self.f.settings,[self.entry]*2,self.f.candidate_pin,'declared-gaps',self.docs)
        for key in ['testPassClaimed','responseSeconds','syntheticNUnitCompletion']:
            self.entry[key]=True
            with self.subTest(key=key),self.assertRaises(ValueError):self.audit()
            del self.entry[key]

    def test_rewritten_quote_behavior_limits_or_statement_index_refused(self):
        for k,v in [('quote','Other'),('reportedBehavior','All controls pass'),('limitations',[]),('statementIndex',True),('statementIndex',1)]:
            original=copy.deepcopy(self.entry[k]);self.entry[k]=v;self.attest()
            with self.subTest(k=k),self.assertRaises(ValueError):self.audit()
            self.entry[k]=original
        self.attest()

    def test_nonobservational_request_is_not_promoted_to_performed_work(self):
        self.provenance['statements'].append({'quote':'And item 18?','role':'Question, not performed-work claim'})
        self.entry['provenance']=self.f.write('human.json',json.dumps(self.provenance).encode());self.entry['statementIndex']=1
        self.entry['quote']='And item 18?';self.attest()
        with self.assertRaises(ValueError):self.audit()

    def test_duplicate_json_keys_in_provenance_and_attestation_refused(self):
        for field,name in [('provenance','human.json'),('attestation','human-review.json')]:
            p=self.f.evidence/name;old=p.read_bytes();changed=old[:-1]+b',"quote":"second","quote":"third"}'
            ref=self.f.write(name,changed)
            if field=='provenance':self.entry[field]=ref;self.attest()
            else:self.entry['sourceReview'][field]=ref;self.entry['sourceReview']['files'][1]=ref
            with self.subTest(field=field),self.assertRaises(ValueError):self.audit()
            p.write_bytes(old);self.entry['provenance']=self.f.write('human.json',json.dumps(self.provenance).encode());self.attest()

    def test_inferred_identity_or_version_package_refused(self):
        for field,value in [('deviceId',7),('version','2.0.005.0000'),('packageSha256','a'*64)]:
            self.entry['reportedIdentity'][field]=value;self.attest()
            with self.subTest(field=field),self.assertRaises(ValueError):self.audit()
            self.entry['reportedIdentity'][field]=None

    def test_exact_explicit_human_identity_is_retained_not_current_authenticated(self):
        supplied={'deviceId':17,'version':'1.2.003.0004','packageSha256':None}
        self.statement['reportedIdentity']=supplied
        self.entry['provenance']=self.f.write('human.json',json.dumps(self.provenance).encode());self.entry['reportedIdentity']=supplied.copy();self.attest()
        r=self.audit()[0][0];self.assertEqual(supplied,r['reportedIdentity']);self.assertFalse(r['installedPackageAuthenticated'])

    def test_missing_source_review_refs_attestation_or_bad_chronology_refused(self):
        original=copy.deepcopy(self.entry)
        for field,value in [('reviewer',''),('rationale',''),('currentSourceCommit','c'*40),('reviewedUtc','2025-01-01T00:00:00Z'),('reviewedUtc','2099-01-01T00:00:00Z')]:
            self.entry=copy.deepcopy(original);self.entry['sourceReview'][field]=value;self.attest()
            with self.subTest(field=field),self.assertRaises(ValueError):self.audit()
        self.entry=copy.deepcopy(original);self.attest();self.entry['sourceReview']['files']=self.entry['sourceReview']['files'][:-1]
        with self.assertRaises(ValueError):self.audit()

    def test_repinning_changed_attestation_cannot_disagree_with_binding(self):
        value=json.loads((self.f.evidence/'human-review.json').read_bytes());value['quote']='All tests passed'
        self.entry['sourceReview']['attestation']=self.f.write('human-review.json',json.dumps(value).encode())
        self.entry['sourceReview']['files'][1]=self.entry['sourceReview']['attestation']
        with self.assertRaises(ValueError):self.audit()

    def test_changed_human_original_or_source_after_audit_is_refused(self):
        for name in ['human.json','history.cs','screen.png']:
            a,o,d=self.inputs();p=self.f.evidence/name;old=p.read_bytes();p.write_bytes(old+b'changed')
            with self.subTest(name=name),self.assertRaises(ValueError):validate_observation_bindings(a,{SUBPAGE_SCOPE},o,d,self.f.evidence)
            p.write_bytes(old)

    def test_missing_or_wrong_static_corroboration_refused(self):
        with self.assertRaises(ValueError):audit_human_history(self.f.settings,[self.entry],self.f.candidate_pin,'declared-gaps',[])
        self.entry['corroboratingScopes']=[LABEL_SCOPE];self.attest()
        with self.assertRaises(ValueError):self.audit()

    def test_pass_prior_pass_missing_interpretation_or_wrong_reviewer_refused(self):
        for change in ['Passed','ReviewedPriorPass','NotApplicable','missing','reviewer','observation-ref','review-ref']:
            a,o,d=self.inputs()
            if change in ['Passed','ReviewedPriorPass','NotApplicable']:o['observations'][0]['outcome']=change
            elif change=='missing':d['declarations'][0].pop('interpretationReview')
            elif change=='reviewer':d['declarations'][0]['interpretationReview']['reviewer']='Other'
            elif change=='observation-ref':o['observations'][0]['files']=o['observations'][0]['files'][:-1]
            else:d['declarations'][0]['interpretationReview']['evidence']=d['declarations'][0]['interpretationReview']['evidence'][:-1]
            with self.subTest(change=change),self.assertRaises(ValueError):validate_observation_bindings(a,{SUBPAGE_SCOPE},o,d,self.f.evidence)

    def test_human_binding_does_not_authorize_other_interrupted_scope_or_raw_run(self):
        a,o,d=self.inputs();other='extension.views.06'
        o['observations'].append({'requirementId':other,'outcome':'Partial','files':[]})
        d['declarations'].append({'requirementId':other,'interpretationReview':{'reviewer':'Synthetic named reviewer'}})
        with self.assertRaises(ValueError):validate_observation_bindings(a,{SUBPAGE_SCOPE,other},o,d,self.f.evidence)
        a,o,d=self.inputs();a['observationBindings']=[{'requirementId':SUBPAGE_SCOPE,'runId':'d'*32,'files':[]}]
        with self.assertRaises(ValueError):validate_observation_bindings(a,{SUBPAGE_SCOPE},o,d,self.f.evidence)

    def integrated(self,interrupted=False):
        a,path,_=self.f.integrated_audit(interrupted)
        pins=json.loads(path.read_bytes());pins['humanObservationBindings']=[self.entry];path.write_bytes(json.dumps(pins).encode())
        return a,path,audit_runs(self.f.settings,path,sha(path.read_bytes()),self.f.candidate_pin,review_mode='declared-gaps')

    def test_complete_machine_producer_preserved_and_completion_still_required(self):
        a,path,r=self.integrated();self.assertEqual('AndroidEvidenceAudited',r['runs'][0]['status']);self.assertEqual(1,len(r['humanObservationBindings']))
        (a.root/'completion.json').unlink()
        with self.assertRaises((ValueError,OSError)):audit_runs(self.f.settings,path,sha(path.read_bytes()),self.f.candidate_pin,review_mode='declared-gaps')

    def test_interrupted_inventory_remains_exact_and_mutation_refused(self):
        a,path,r=self.integrated(True);self.assertEqual('InterruptedAndroidEvidencePreserved',r['runs'][0]['status']);self.assertFalse((a.root/'completion.json').exists())
        (a.root/'context.json').write_bytes((a.root/'context.json').read_bytes()+b'changed')
        with self.assertRaises(ValueError):audit_runs(self.f.settings,path,sha(path.read_bytes()),self.f.candidate_pin,review_mode='declared-gaps')

    def test_portable_freeze_retains_and_reaudits_every_human_source_and_raw_file(self):
        from freeze_review_inputs import freeze,load
        a,pins,r=self.integrated(True);f=self.f
        for field,value in [('policy',{'schemaVersion':1,'requirements':[]}),('inventory',{'schemaVersion':1}),('mapping',{'schemaVersion':1})]:
            path=f.root/(field+'.json');path.write_bytes(json.dumps(value).encode());f.settings[field]=str(path)
        template=f.root/'template.pdf';template.write_bytes(b'Synthetic test, not official PDF');f.settings['template']=str(template)
        f.candidate['identity'].update(policySha256=sha(Path(f.settings['policy']).read_bytes()),templateSha256=sha(template.read_bytes()));f.save_candidate()
        value=json.loads(pins.read_bytes());value['candidateSha256']=f.candidate_pin;pins.write_bytes(json.dumps(value).encode())
        files=r['humanObservationBindings'][0]['files'];obs=f.root/'obs.json';obs.write_bytes(json.dumps({'schemaVersion':1,'observations':[{'requirementId':SUBPAGE_SCOPE,'outcome':'Partial','files':files}]}).encode())
        f.settings.update(schemaVersion=1,observations=str(obs),output=str(f.root/'unused'),title='Synthetic',author='Synthetic')
        decl=f.root/'decl.json';decl.write_bytes(b'{"schemaVersion":1,"declarations":[]}');settings=f.root/'settings.json';settings.write_bytes(json.dumps(f.settings).encode());dest=f.root/'frozen'
        result=freeze(settings,dest,f.candidate_pin,sha(Path(f.settings['inventory']).read_bytes()),sha(Path(f.settings['mapping']).read_bytes()),'a'*40,
            android_pins=pins,android_pins_sha256=sha(pins.read_bytes()),review_mode='declared-gaps',declarations=decl,declarations_sha256=sha(decl.read_bytes()))
        _,retained,options=load(dest,result['inputsSha256'],completed=True)
        copied=audit_runs(retained,options['androidPins'],options['androidPinsSha256'],f.candidate_pin,review_mode='declared-gaps')
        self.assertEqual(r['humanObservationBindings'],copied['humanObservationBindings']);self.assertFalse(result['testsExecuted'])
        self.assertFalse((dest/'android'/a.run/'completion.json').exists())
        for ref in files:self.assertEqual((f.evidence/ref['relativePath']).read_bytes(),(dest/'evidence'/ref['relativePath']).read_bytes())
        retained['evidence']=str(dest/'evidence');(dest/'evidence'/'human.json').write_bytes(b'changed')
        with self.assertRaises(ValueError):audit_runs(retained,options['androidPins'],options['androidPinsSha256'],f.candidate_pin,review_mode='declared-gaps')


if __name__=='__main__':unittest.main()
