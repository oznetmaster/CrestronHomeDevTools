// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationReviewSelectionTests
{
 private string root=null!;
 private SubmissionAutomationSettings settings=null!;
 private SubmissionWorkflowStepContext context=null!;
 private string P(string name)=>Path.Combine(root,name);
 private string Hash(string name)=>AutomationFiles.Hash(P(name));
 private void Write<T>(string name,T value)=>AutomationReview.WriteDocument(P(name),value);
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"review-supplement-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  using var package=SubmissionPackageTests.Package();File.WriteAllBytes(P("ExampleDeveloper_Test_Example_IP.pkg"),package.ToArray());
  File.WriteAllText(P("template.pdf"),"%PDF-1.7\nSynthetic template; not a rendered form");File.WriteAllText(P("trace.txt"),"Synthetic observation, not device evidence");
  Write("inventory.json",new{});Write("mapping.json",new{});
  Write("policy.json",new SubmissionEvidencePolicy(1,[new("endurance",TimeSpan.Zero),new("sensor.event",TimeSpan.Zero)]));
  var release=new SubmissionWorkflowRelease("example/driver",2,"v1",new('a',40),Hash("ExampleDeveloper_Test_Example_IP.pkg"),new('b',64),new('c',64));
  var identity=new SubmissionEvidenceIdentity(release.PackageSha256,release.SourceCommit,Hash("policy.json"),Hash("template.pdf"));
  var requirements=new SubmissionPackageRequirements("1286a404-144e-4d77-b96b-1d1272f21c64","1.2.003.0000",PortalSubmissionKind.NewDriver,"ExampleDeveloper","support@example.com");
  Write("candidate.json",new SubmissionCandidate(1,identity,requirements));
  var now=DateTimeOffset.UtcNow.AddMinutes(-1);
  var first=new SubmissionObservation("endurance",identity,SubmissionEvidenceOutcome.Passed,now,now,[new("trace.txt",Hash("trace.txt"))]);
  Write("original.json",new SubmissionEvidenceDocument(1,[first]));
  Write("supplement.json",new SubmissionEvidenceDocument(1,[first,first with{RequirementId="sensor.event"}]));
  Write("declarations.json",new SubmissionGapDeclarations(1,identity,SubmissionReviewMode.DeclaredGaps,[new("sensor.event","Hardware arrives later") ]));
  var preliminary=SubmissionReviewFiles.Check(P("candidate.json"),Hash("candidate.json"),P("ExampleDeveloper_Test_Example_IP.pkg"),P("policy.json"),P("template.pdf"),P("original.json"),root,
   P("declarations.json"),Hash("declarations.json"),SubmissionReviewMode.DeclaredGaps,DateTimeOffset.UtcNow);
  Assert.That(preliminary.ReadyForReview,Is.True,JsonSerializer.Serialize(preliminary));
  foreach(string folder in new[]{"review","review-revisions/sensors"}) {
   Directory.CreateDirectory(P(folder));
   if(folder=="review") {
    var result=SubmissionBundle.CreateReview(P(folder+"/evidence.zip"),P("candidate.json"),Hash("candidate.json"),P("ExampleDeveloper_Test_Example_IP.pkg"),P("policy.json"),P("template.pdf"),P("original.json"),root,
     P("declarations.json"),Hash("declarations.json"),SubmissionReviewMode.DeclaredGaps,DateTimeOffset.UtcNow);
    Assert.That(result.ReadyForReview,Is.True);
   } else {
    var result=SubmissionBundle.Create(P(folder+"/evidence.zip"),P("candidate.json"),Hash("candidate.json"),P("ExampleDeveloper_Test_Example_IP.pkg"),P("policy.json"),P("template.pdf"),P("supplement.json"),root,DateTimeOffset.UtcNow);
    Assert.That(result.ValidationChecksPassed,Is.True);
   }
   File.WriteAllText(P(folder+"/self-test.review.pdf"),"Synthetic unsigned form");Write(folder+"/form-report.json",new{});
   File.Copy(P("inventory.json"),P(folder+"/inventory.json"));File.Copy(P("mapping.json"),P(folder+"/mapping.json"));
   var receipt=new Dictionary<string,object>{["state"]=folder=="review"?"UnsignedReviewWithDeclaredGapsPrepared":"UnsignedReviewPrepared",
    ["sourceCommit"]=release.SourceCommit,["candidateSha256"]=Hash("candidate.json"),["inventorySha256"]=Hash("inventory.json"),["mappingSha256"]=Hash("mapping.json"),
    ["bundleSha256"]=Hash(folder+"/evidence.zip"),["formSha256"]=Hash(folder+"/self-test.review.pdf"),["formReportSha256"]=Hash(folder+"/form-report.json"),
    ["signingCopy"]=true,["submissionReady"]=false,["deliveryAttempted"]=false};
   if(folder=="review"){receipt["reviewMode"]="DeclaredGaps";receipt["declarationsSha256"]=Hash("declarations.json");}
   Write(folder+"/review-receipt.json",receipt);File.WriteAllText(P(folder+"/COMPLETE"),Hash(folder+"/review-receipt.json"));
  }
  AutomationFiles.Write(P("review-evidence.json"),new AutomationReview.Receipt(new('d',64),Hash("review/review-receipt.json"),
   Directory.GetFiles(P("review")).Select(f=>new SubmissionWorkflowReceipt(Path.GetRelativePath(root,f),AutomationFiles.Hash(f))).ToArray()));
  var input=new SubmissionAutomationInput(P("policy.json"),Hash("policy.json"));
  var plan=new SubmissionAutomationProtectedPlan(P("unused-credentials.json"),new(P("approvals/sign.json"),P("approvals/sign.sha256")),new(P("approvals/send.json"),P("approvals/send.sha256")))
   {ReviewRevision=new("review-revisions/sensors",Hash("review/review-receipt.json"),Hash("review-revisions/sensors/review-receipt.json"))};
  settings=new(1,root,release,root,requirements,"unused",new WorkflowPlan{Host="synthetic",CertificateSha256=new('e',64),SshFingerprint="synthetic",SourceRoots=[root],LocalTests=[],TestPackage=new("unused.csproj","unused.pkg","fixture",1),ProcessorSuites=[]},
   Mode:SubmissionAutomationMode.Submit,Review:new(input,input,input,input,new(root,[]),"Synthetic","Fixture",[]),Protected:plan);
  // Approval channels are outside the run, as required by the real signing adapter.
  settings=settings with{Protected=plan with{SigningApproval=new(root+"-sign.json",root+"-sign.sha256")}};
  context=new(root,new(1,new('d',64),release,SubmissionWorkflowStage.SignReview,SubmissionWorkflowStatus.Running,"fixture-operation",null,[],DateTimeOffset.UtcNow));
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 [Test]public async Task AdditivePacketUsesNewSigningRequestWithoutChangingOriginalEvidenceOrGrantingAuthority() {
  string before=Hash("review-evidence.json");int executions=0;
  var result=await AutomationSigning.Advance(context,settings,false,default,(_,_,_,_)=>{executions++;return Task.FromResult(0);});
  Assert.That(result.ReasonCode,Is.EqualTo("signing-authorization-required"));Assert.That(executions,Is.Zero);
  Assert.That(Hash("review-evidence.json"),Is.EqualTo(before));AutomationReview.VerifyRetained(root);
  var request=AutomationFiles.Read<JsonElement>(P("signing-request-"+settings.Protected!.ReviewRevision!.ReviewSha256+".json"));
  Assert.That(request.GetProperty("ReviewDirectory").GetString(),Is.EqualTo(Path.GetFullPath(P("review-revisions/sensors"))));
  Assert.That(request.GetProperty("SignatureAuthorized").GetBoolean(),Is.False);
 }
 [TestCase("sourceCommit")][TestCase("candidateSha256")][TestCase("inventorySha256")][TestCase("mappingSha256")]
 public void RepinnedSupplementCannotChangeFrozenIdentity(string field) {
  string name="review-revisions/sensors/review-receipt.json";var changed=JsonNode.Parse(File.ReadAllText(P(name)))!;changed[field]=new string('f',field=="sourceCommit"?40:64);
  File.WriteAllText(P(name),changed.ToJsonString());File.WriteAllText(P("review-revisions/sensors/COMPLETE"),Hash(name));
  settings=settings with{Protected=settings.Protected! with{ReviewRevision=settings.Protected!.ReviewRevision! with{ReviewSha256=Hash(name)}}};
  Assert.Throws<InvalidDataException>(()=>AutomationReviewSelection.Resolve(context,settings,default));
 }
 [Test]public void ChangedSupplementArtifactIsRejected() {
  File.AppendAllText(P("review-revisions/sensors/self-test.review.pdf"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationReviewSelection.Resolve(context,settings,default));
 }
 [TestCase("omitted")][TestCase("changed")][TestCase("duplicate")][TestCase("unchanged")]
 public void LaterPassCannotEraseOrReplaceEarlierObservation(string kind) {
  using var original=JsonDocument.Parse("{\"observations\":[{\"requirementId\":\"one\",\"outcome\":\"Failed\"}]}");
  string observations=kind switch{
   "omitted"=>"{\"requirementId\":\"two\",\"outcome\":\"Passed\"}",
   "changed"=>"{\"requirementId\":\"one\",\"outcome\":\"Passed\"},{\"requirementId\":\"two\"}",
   "duplicate"=>"{\"requirementId\":\"one\",\"outcome\":\"Failed\"},{\"requirementId\":\"one\",\"outcome\":\"Passed\"}",
   _=>"{\"requirementId\":\"one\",\"outcome\":\"Failed\"}"};
  using var revised=JsonDocument.Parse("{\"observations\":["+observations+"]}");
  Assert.Throws<InvalidDataException>(()=>AutomationReviewSelection.RequireAdditive(original.RootElement,revised.RootElement));
 }
}

