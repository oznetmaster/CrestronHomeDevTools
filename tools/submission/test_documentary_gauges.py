# Copyright (c) 2026 Neil Colvin. MIT licensed.
"""Portable synthetic static-gauge review guards; no private captures or device calls."""
import copy
import io
import json
import unittest
from pathlib import Path
from zipfile import ZipFile
from lxml import etree
from build_help import sha
from documentary_gauges import LABEL_SCOPE, SCALE_SCOPE, RESOURCES
from documentary_android import audit_documentary
from review_android import validate_observation_bindings
import test_documentary_android as grouping_fixture


class DocumentaryGaugeTests(unittest.TestCase):
    def setUp(self):
        self.f = grouping_fixture.DocumentaryGroupingTests(); self.f.setUp(); self.addCleanup(self.f.doCleanups)
        f = self.f
        self.ui = b'<uidefinition><layouts><layout id="Impact"><controls><radialgauge id="Share" label="^ShareLabel" radialvalue="{sharePercent}" line1label="{summary}" line2label="{sources}" line3label="{grid}"/></controls></layout></layouts></uidefinition>'
        self.trans = b'{"ShareLabel":"Share"}'
        self.props = b'[EntityProperty (Id = "sharePercent", RangeMinimum = 0, RangeMaximum = 100, Units = "Percent")]\n[EntityPropertyMetadata (ExtensionUiProperty = true)]\npublic int Share { get; private set; }'
        self.package(self.ui, self.trans)
        f.candidate['identity']['packageSha256'] = sha(f.package.read_bytes()); f.save_candidate()
        e = f.entry; del e['groups']; e['requirementId'] = LABEL_SCOPE
        r = e['sourceReview']; r['rationale'] = 'New static gauge label and declared percentage review only.'
        r.update(originalUiDefinition=f.write('old.xml', self.ui), currentUiDefinition=f.write('current.xml', self.ui),
                 originalTranslations=f.write('old-language.json', self.trans), currentTranslations=f.write('language.json', self.trans),
                 translationPackagePath='translations/en-US.json', originalPropertyDefinitions=f.write('old-properties.cs', self.props),
                 currentPropertyDefinitions=f.write('properties.cs', self.props))
        self.labels = ['Share','25%','Solar: 20% Battery: 5%','Grid: 75%']
        self.rects = ['[0,0][300,20]','[100,20][200,95]','[100,30][200,40]','[30,45][270,55]','[100,60][200,70]']
        self.tree = etree.Element('hierarchy'); container = etree.SubElement(self.tree,'node',package='app',password='false')
        container.set('class','android.view.ViewGroup');container.set('bounds','[0,0][300,100]');container.set('resource-id','')
        for resource, rect, text in zip(RESOURCES,self.rects,[self.labels[0],'',*self.labels[1:]]):
            n=etree.SubElement(container,'node',package='app',password='false');n.set('resource-id','app:id/'+resource);n.set('bounds',rect);n.set('text',text)
        e['gauges'] = [{'pageId':'Impact','controlId':'Share','propertyId':'sharePercent',
            'screenshot':f.write('screen.png',f.png),'hierarchy':{},'appPackage':'app','containerBounds':'[0,0][300,100]',
            'gaugeBounds':self.rects[1],'labelBounds':[self.rects[0],*self.rects[2:]],'labelText':self.labels,
            'scale':{'minimum':0,'maximum':100,'units':'Percent'}}]
        self.hierarchy(self.tree); self.attestation()

    def package(self, ui, trans):
        with ZipFile(self.f.package,'w') as z:
            z.writestr('UiDefinitions/UiDefinition.xml',ui);z.writestr('translations/en-US.json',trans)

    def hierarchy(self, tree):
        f=self.f;data=etree.tostring(tree);f.entry['gauges'][0]['hierarchy']=f.write('hierarchy.xml',data)
        f.result['screenshots'][1].update(sha256=sha(data),bytes=len(data));f.result_ref()

    def attestation(self):
        e=self.f.entry;r=e['sourceReview']
        value={'originalSourceCommit':r['originalSourceCommit'],'currentSourceCommit':r['currentSourceCommit'],
               'requirementId':e['requirementId'],'reviewer':r['reviewer'],'reviewedUtc':r['reviewedUtc'],
               'gaugeIds':[['Impact','Share']]}
        r['attestation']=self.f.write('source-review.json',json.dumps(value).encode())

    def audit(self):return audit_documentary(self.f.settings,[self.f.entry],self.f.candidate_pin,'declared-gaps')

    def inputs(self):
        reports,_=self.audit();scope=self.f.entry['requirementId'];files=reports[0]['files']
        a={'runs':[{'runId':'d'*32,'status':'InterruptedAndroidEvidencePreserved','files':[]}],
           'documentaryBindings':reports,'observationBindings':[]}
        o={'observations':[{'requirementId':scope,'outcome':'Partial','files':files}]}
        d={'declarations':[{'requirementId':scope,'reason':'Original static fact, no device-change test.',
            'interpretationReview':{'reviewer':'Synthetic named reviewer','rationale':'Exact static scope only','evidence':files}}]}
        return a,o,d

    def test_labels_and_scale_have_distinct_static_no_auth_reports(self):
        for scope,status in [(LABEL_SCOPE,'StaticDocumentaryGaugeLabelsReviewed'),(SCALE_SCOPE,'StaticDocumentaryGaugeScaleReviewed')]:
            self.f.entry['requirementId']=scope;self.attestation();reports,files=self.audit();r=reports[0]
            self.assertEqual(status,r['status']);self.assertFalse(r['producerAuthenticated']);self.assertFalse(r['testPassClaimed'])
            self.assertIsNone(r['identity']['installedPackageSha256']);self.assertFalse(r['dynamicDeviceChangeObserved'])
            self.assertFalse(r['physicalExtremesObserved']);self.assertTrue(r['staticDeclaredScaleOnly'])
            a,o,d=self.inputs();self.assertEqual({scope},set(validate_observation_bindings(a,{scope},o,d,self.f.evidence)))

    def test_both_static_scopes_can_coexist_without_becoming_dynamic(self):
        first=copy.deepcopy(self.f.entry);self.f.entry['requirementId']=SCALE_SCOPE;self.attestation();second=copy.deepcopy(self.f.entry)
        # Separate attestation references must remain exact, not overwritten.
        first['sourceReview']['attestation']=self.f.write('labels-review.json',json.dumps({'originalSourceCommit':'c'*40,'currentSourceCommit':'a'*40,'requirementId':LABEL_SCOPE,'reviewer':'Synthetic named reviewer','reviewedUtc':'2026-01-02T00:00:00Z','gaugeIds':[['Impact','Share']]}).encode())
        reports,_=audit_documentary(self.f.settings,[first,second],self.f.candidate_pin,'declared-gaps')
        self.assertEqual({LABEL_SCOPE,SCALE_SCOPE},{r['requirementId'] for r in reports})

    def test_dynamic_unknown_complete_mode_and_group_schema_refused(self):
        for scope in ['extension.controls.06.device-change-immediate-gauge','extension.views.06', 'extension.system.03']:
            self.f.entry['requirementId']=scope
            with self.subTest(scope=scope),self.assertRaises(ValueError):self.audit()
        self.f.entry['requirementId']=LABEL_SCOPE
        with self.assertRaises(ValueError):audit_documentary(self.f.settings,[self.f.entry],self.f.candidate_pin,'complete')
        self.f.entry['groups']=[]
        with self.assertRaises(ValueError):self.audit()

    def test_group_attestation_and_wrong_scope_reviewer_time_enumeration_refused(self):
        for field,value in [('requirementId','extension.controls.08.all-declared-button-groups'),('reviewer','Other'),('reviewedUtc','2026-01-03T00:00:00Z'),('gaugeIds',[])]:
            self.attestation();p=self.f.evidence/'source-review.json';a=json.loads(p.read_bytes());a[field]=value
            self.f.entry['sourceReview']['attestation']=self.f.write('source-review.json',json.dumps(a).encode())
            with self.subTest(field=field),self.assertRaises(ValueError):self.audit()

    def test_missing_duplicate_unknown_gauge_or_property_mapping_refused(self):
        original=copy.deepcopy(self.f.entry['gauges'])
        for gauges in [[],original*2,[{**original[0],'controlId':'Other'}],[{**original[0],'propertyId':'other'}]]:
            self.f.entry['gauges']=gauges
            with self.subTest(gauges=gauges),self.assertRaises(ValueError):self.audit()
        self.f.entry['gauges']=original

    def test_all_current_declared_gauges_cannot_be_omitted(self):
        extra = self.ui.replace(b'</controls>', b'<radialgauge id="Other" label="^ShareLabel" radialvalue="{sharePercent}" line1label="{summary}" line2label="{sources}" line3label="{grid}"/></controls>')
        self.package(extra,self.trans);self.f.candidate['identity']['packageSha256']=sha(self.f.package.read_bytes());self.f.save_candidate()
        r=self.f.entry['sourceReview'];r['originalUiDefinition']=self.f.write('old.xml',extra);r['currentUiDefinition']=self.f.write('current.xml',extra)
        with self.assertRaises(ValueError):self.audit()

    def test_extra_undeclared_native_gauge_and_substituted_container_refused(self):
        tree=copy.deepcopy(self.tree);tree.append(copy.deepcopy(tree[0]));tree[1].set('bounds','[0,1][300,100]');self.hierarchy(tree)
        with self.assertRaises(ValueError):self.audit()
        tree=copy.deepcopy(self.tree);tree[0].set('resource-id','app:id/other');self.hierarchy(tree)
        with self.assertRaises(ValueError):self.audit()

    def test_omitted_extra_reordered_or_nested_native_children_refused(self):
        for kind in ['omit','extra','reverse','nested']:
            tree=copy.deepcopy(self.tree);c=tree[0]
            if kind=='omit':c.remove(c[-1])
            elif kind=='extra':etree.SubElement(c,'node',package='app',password='false')
            elif kind=='reverse':c.insert(0,c[-1])
            else:
                node=c[0];c.remove(node);w=etree.SubElement(c,'node',package='app',password='false');w.append(node)
            self.hierarchy(tree)
            with self.subTest(kind=kind),self.assertRaises(ValueError):self.audit()

    def test_same_app_nonpassword_container_ancestor_and_child_required(self):
        for where in ['container','child','ancestor']:
            for attr,value in [('package','other'),('password','true')]:
                tree=copy.deepcopy(self.tree);target=tree[0] if where=='container' else tree[0][0]
                if where=='ancestor':
                    c=tree[0];tree.remove(c);target=etree.SubElement(tree,'node',package='app',password='false');target.append(c)
                target.set(attr,value);self.hierarchy(tree)
                with self.subTest(where=where,attr=attr),self.assertRaises(ValueError):self.audit()

    def test_label_text_bounds_and_exact_title_translation_required(self):
        original=copy.deepcopy(self.f.entry['gauges'][0]);g=self.f.entry['gauges'][0]
        for field,value in [('labelText',['Wrong',*self.labels[1:]]),('labelText',self.labels[:-1]),('labelText',['Share','','sources','grid']),('gaugeBounds','[0,0][500,100]'),('labelBounds',[self.rects[0]]*4)]:
            g[field]=value
            with self.subTest(field=field),self.assertRaises(ValueError):self.audit()
            g.clear();g.update(copy.deepcopy(original))
        tree=copy.deepcopy(self.tree);tree[0][2].set('text','Other');self.hierarchy(tree)
        with self.assertRaises(ValueError):self.audit()

    def test_translation_and_page_must_be_original_and_packaged_exact(self):
        r=self.f.entry['sourceReview'];r['currentTranslations']=self.f.write('language.json',b'{"ShareLabel":"Other"}')
        with self.assertRaises(ValueError):self.audit()
        r['currentTranslations']=self.f.write('language.json',self.trans)
        r['originalUiDefinition']=self.f.write('old.xml',self.ui.replace(b'line2label="{sources}"',b'line2label="{other}"'))
        with self.assertRaises(ValueError):self.audit()

    def test_scale_is_static_percent_not_physical_extremes_and_both_sources_match(self):
        g=self.f.entry['gauges'][0]
        for v in [{'minimum':True,'maximum':100,'units':'Percent'},{'minimum':0,'maximum':99,'units':'Percent'},{'minimum':0,'maximum':100,'units':'Watts'}]:
            g['scale']=v
            with self.subTest(scale=v),self.assertRaises(ValueError):self.audit()
        g['scale']={'minimum':0,'maximum':100,'units':'Percent'}
        for key in ['originalPropertyDefinitions','currentPropertyDefinitions']:
            before=self.f.entry['sourceReview'][key]
            self.f.entry['sourceReview'][key]=self.f.write('wrong-'+key+'.cs',self.props.replace(b'RangeMaximum = 100',b'RangeMaximum = 99'))
            with self.subTest(key=key),self.assertRaises(ValueError):self.audit()
            self.f.entry['sourceReview'][key]=before

    def test_commented_duplicate_missing_or_ambiguous_property_metadata_refused(self):
        for data in [b'/*'+self.props+b'*/',b'//'+self.props.replace(b'\n',b'\n//'),self.props+self.props,self.props.replace(b'Id = "sharePercent"',b'Id = "wrong"'),self.props.replace(b'RangeMinimum = 0',b'RangeMinimum = 0, RangeMinimum = 1')]:
            self.f.entry['sourceReview']['currentPropertyDefinitions']=self.f.write('properties.cs',data)
            with self.subTest(data=data),self.assertRaises(ValueError):self.audit()

    def test_original_null_claims_archive_restoration_and_all_refs_stay_strict(self):
        for flag in ['testPassClaimed','candidateAccepted']:
            self.f.entry['identity'][flag]=True
            with self.subTest(flag=flag),self.assertRaises(ValueError):self.audit()
            self.f.entry['identity'][flag]=False
        before=self.f.entry['restoration']['screenshot'];self.f.entry['restoration']['screenshot']=self.f.entry['gauges'][0]['screenshot']
        with self.assertRaises(ValueError):self.audit()
        self.f.entry['restoration']['screenshot']=before
        reports,_=self.audit()
        for r in reports[0]['files']:
            p=self.f.evidence/r['relativePath'];data=p.read_bytes();p.write_bytes(data+b'changed')
            with self.subTest(path=r['relativePath']),self.assertRaises(ValueError):self.audit()
            p.write_bytes(data)

    def test_binding_refuses_pass_priorpass_missing_refs_and_other_reviewer(self):
        for outcome in ['Passed','ReviewedPriorPass','Failed','NotApplicable']:
            a,o,d=self.inputs();o['observations'][0]['outcome']=outcome
            with self.subTest(outcome=outcome),self.assertRaises(ValueError):validate_observation_bindings(a,{LABEL_SCOPE},o,d,self.f.evidence)
        for field in ['observation','review','reviewer']:
            a,o,d=self.inputs()
            if field=='observation':o['observations'][0]['files']=o['observations'][0]['files'][:-1]
            elif field=='review':d['declarations'][0]['interpretationReview']['evidence']=d['declarations'][0]['interpretationReview']['evidence'][:-1]
            else:d['declarations'][0]['interpretationReview']['reviewer']='Other'
            with self.subTest(field=field),self.assertRaises(ValueError):validate_observation_bindings(a,{LABEL_SCOPE},o,d,self.f.evidence)

    def test_portable_freeze_reaudits_exact_static_scope_with_interrupted_run(self):
        from freeze_review_inputs import freeze,load
        from review_android import audit_runs
        f=self.f;a,pins_path,report=f.integrated_audit(interrupted=True)
        for name,value in [('policy',{'schemaVersion':1,'requirements':[]}),('inventory',{'schemaVersion':1}),('mapping',{'schemaVersion':1})]:
            p=f.root/(name+'.json');p.write_text(json.dumps(value));f.settings[name]=str(p)
        template=f.root/'template.pdf';template.write_bytes(b'Synthetic retention input');f.settings['template']=str(template)
        f.candidate['identity']['policySha256']=sha(Path(f.settings['policy']).read_bytes());f.candidate['identity']['templateSha256']=sha(template.read_bytes());f.save_candidate()
        pins=json.loads(pins_path.read_bytes());pins['candidateSha256']=f.candidate_pin;pins_path.write_text(json.dumps(pins))
        files=report['documentaryBindings'][0]['files'];o=f.root/'observations.json';o.write_text(json.dumps({'schemaVersion':1,'observations':[{'requirementId':LABEL_SCOPE,'outcome':'Partial','files':files}]}));f.settings['observations']=str(o)
        d=f.root/'declarations.json';d.write_text(json.dumps({'schemaVersion':1,'declarations':[]}));f.settings.update(schemaVersion=1,output=str(f.root/'unused'),title='Synthetic',author='Synthetic')
        settings=f.root/'settings.json';settings.write_text(json.dumps(f.settings));dest=f.root/'frozen-gauge'
        result=freeze(settings,dest,f.candidate_pin,sha(Path(f.settings['inventory']).read_bytes()),sha(Path(f.settings['mapping']).read_bytes()),'a'*40,android_pins=pins_path,android_pins_sha256=sha(pins_path.read_bytes()),review_mode='declared-gaps',declarations=d,declarations_sha256=sha(d.read_bytes()))
        self.assertFalse(result['testsExecuted']);self.assertFalse(result['submissionReady']);_,retained,options=load(dest,result['inputsSha256'],completed=True)
        copied=audit_runs(retained,options['androidPins'],options['androidPinsSha256'],f.candidate_pin,review_mode='declared-gaps')
        self.assertEqual(report['documentaryBindings'],copied['documentaryBindings'])
        for ref in files:self.assertEqual((f.evidence/ref['relativePath']).read_bytes(),(dest/'evidence'/ref['relativePath']).read_bytes())
        changed=dest/'evidence'/'language.json';changed.write_bytes(changed.read_bytes()+b'changed')
        with self.assertRaises(ValueError):audit_runs(retained,options['androidPins'],options['androidPinsSha256'],f.candidate_pin,review_mode='declared-gaps')


if __name__=='__main__':unittest.main()
