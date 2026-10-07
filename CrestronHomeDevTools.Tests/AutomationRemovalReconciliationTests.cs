// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Android;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationRemovalReconciliationTests
{
 private string root=null!,original=null!,evidence=null!;
 private DriverRemovalWorkflowPlan plan=null!;
 private DateTimeOffset start;
 private const string Owner="11111111111111111111111111111111";
 private DriverRemovalDevice[] before=null!,selected=null!,after=null!;
 private ProcessorErrorLogSnapshot oldLog=null!,newLog=null!;
 private void Write(string folder,string path,object value) {var full=Path.Combine(folder,path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);File.WriteAllText(full,JsonSerializer.Serialize(value,AutomationFiles.Json));}
 private void Capture(string folder,string path) {var full=Path.Combine(folder,path);Directory.CreateDirectory(full);File.WriteAllText(Path.Combine(full,"hierarchy.xml"),"<hierarchy/>");File.WriteAllBytes(Path.Combine(full,"screen.png"),[137,80,78,71]);}
 private void Ui(string folder,bool removed) {
  Write(folder,"plan.json",plan.App);Write(folder,"outcome.json",new DriverRemovalUiOutcome(true,true));
  foreach(string name in new[]{"home","room-0"}) {
   string[] expected=removed||name=="home"?[]:["Demo Child"];
   Write(folder,name+"-summary.json",new {Expected=expected,Observed=expected,FullVerticalTraversal=true});
   Capture(folder,name+"-up-0");Capture(folder,name+"-down-0");
  }
  Capture(folder,"home-restored");
 }
 private ProcessorErrorLogSnapshot Log(string extra,int seconds)=>ProcessorErrorLog.Parse("processor",
  "Persistent log contents during current boot:\nNotice: ctpd # time # Boot retained\n"+extra+"\nCP4-R>","CP4-R>",start.AddSeconds(seconds),start.AddSeconds(seconds+1));
 private void PinOriginal()=>Write(evidence,"original-inventory.json",AutomationRemovalReconciliation.Inventory(original));
 [SetUp] public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"removal-reconcile-"+Guid.NewGuid().ToString("N"));
  original=Path.Combine(root,"removal/operation");evidence=Path.Combine(root,"removal/reconciliation/evidence");Directory.CreateDirectory(original);Directory.CreateDirectory(evidence);
  start=DateTimeOffset.UtcNow.AddMinutes(-5);
  File.WriteAllText(Path.Combine(original,"candidate.pkg"),"synthetic pinned candidate");
  var profile=new AndroidSessionProfile("adb","device","com.crestron.phoenix.app","Home",Path.Combine(root,"android.lock"));
  plan=new("processor",new('a',64),"ssh",Path.Combine(original,"candidate.pkg"),AutomationFiles.Hash(Path.Combine(original,"candidate.pkg")),
   new(10,-1,"Platform","Model",1,"1.0.0.0","catalogue","Author","IP"),new(profile,[new(11,"Demo Child",1,"Room",false)],[10]));
  Write(original,"plan.json",plan);Write(original,"failure.json",new {ErrorType="System.Threading.Tasks.TaskCanceledException",Message="Original timeout retained"});
  Write(original,"ownership.json",new {Owner,Host=plan.Host,RemovalRequested=true});
  Write(original,"app-opening/context.json",new AndroidRunContext(1,Owner,Environment.MachineName,int.MaxValue,1,plan.Host,10,"guid","1.0.0.0",plan.PackageSha256,new('b',64),profile,original));
  Write(original,"before-candidate.json",new {plan.Target,Payload=new{Package=new{DriverId="guid",Model="Model",Manufacturer="Maker",Version="1.0.0.0"},plan.PackageSha256,CatalogueId="catalogue",Directory="/synthetic",Files=new Dictionary<string,DriverPayloadFile>{{"fixture.dll",new(1,new('c',64))}},ObservedUtc=start}});
  before=[new(10,-1,"Platform","Model",1,"1.0.0.0","Loaded"),new(11,10,"Demo Child","Child",1,"1.0.0.0","Loaded"),new(20,-1,"Unrelated","Other",2,"2.0.0.0","Loaded")];
  selected=before[..2];after=[before[2]];
  Write(original,"removal/before-inventory.json",before);Write(original,"removal/removal-scope.json",selected);Write(original,"removal/after-inventory.json",after);
  Write(original,"removal/removal-intent.json",new {Target=new DriverRemovalTarget(plan.Host,10,-1,"Platform","Model","1.0.0.0",1),Utc=start.AddSeconds(10)});
  Write(original,"removal/stopped.json",new{RemovalAttempted=true,ErrorType="TaskCanceledException",Utc=start.AddSeconds(20),InspectBeforeRetry=true});Write(original,"removal/before-ui.json",new DriverRemovalUiOutcome(true,true));Ui(Path.Combine(original,"removal/ui-before"),false);
  oldLog=Log("",0);newLog=Log("Ok: App # time # Removed",40);Write(original,"removal/before-log.json",oldLog);
  Write(evidence,"intent.json",new {Utc=start.AddSeconds(30),Owner,ReadOnly=true,RemovalReplay=false,OriginalPlanSha256=AutomationFiles.Hash(Path.Combine(original,"plan.json")),OriginalFailureSha256=AutomationFiles.Hash(Path.Combine(original,"failure.json"))});
  Done();Write(evidence,"before-reconciliation.json",after);Write(evidence,"after-reconciliation.json",after);Ui(Path.Combine(evidence,"ui-after"),true);
  Write(evidence,"after-log.json",newLog);Write(evidence,"log-interval.json",ProcessorErrorLog.Compare(oldLog,newLog));PinOriginal();
 }
 private void Done(bool released=true,bool replay=false)=>Write(evidence,"completion.json",new {Utc=start.AddSeconds(45),Owner,OriginalFailurePreserved=true,RemovalReplay=replay,PhysicalActions=replay?1:0,RemovalConfirmed=true,OtherDevicesPreserved=true,UiAbsenceConfirmed=true,HomeRestored=true,ReservationsReleased=released,LogComparable=true,NoNewErrorsOrExceptions=true,Passed=true});
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 private DriverRemovalWorkflowResult Validate()=>AutomationRemovalReconciliation.ValidateEvidence(original,evidence,plan);
 [Test]public void ReadOnlyReconciliationPreservesOriginalFailureAndDoesNotCreateOriginalResult() {
  var before=AutomationRemovalReconciliation.Inventory(original);var result=Validate();
  Assert.That(result.Passed,Is.True);Assert.That(AutomationRemovalReconciliation.Inventory(original),Is.EqualTo(before));
  Assert.That(File.Exists(Path.Combine(original,"result.json")),Is.False);
 }
 [TestCase("failure.json")][TestCase("candidate.pkg")][TestCase("removal/before-log.json")]
 public void ChangedOriginalCannotBeAccepted(string path) {File.AppendAllText(Path.Combine(original,path)," changed");Assert.Catch<Exception>(()=>Validate());}
 [Test]public void EquivalentZeroPaddedProcessorVersionUsesTheExistingNumericVersionRule() {
  before[0]=before[0] with{Version="1.0.000.0000"};selected[0]=before[0];Write(original,"removal/before-inventory.json",before);Write(original,"removal/removal-scope.json",selected);PinOriginal();Assert.That(Validate().Passed,Is.True);
 }
 [Test]public void MissingOriginalSnapshotEntryCannotBeAccepted() {Write(evidence,"original-inventory.json",AutomationRemovalReconciliation.Inventory(original).Skip(1).ToArray());Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void ExistingOriginalResultCannotBeOverridden() {Write(original,"result.json",new{Passed=false});PinOriginal();Assert.Throws<InvalidDataException>(()=>Validate());}
 [TestCase(true,true)][TestCase(false,false)]public void ReplayedOrUnreleasedDiagnosticCannotPass(bool replay,bool released) {Done(released,replay);Assert.Throws<InvalidDataException>(()=>Validate());}
 [TestCase("before-reconciliation.json")][TestCase("after-reconciliation.json")]
 public void RemovedChildOrUnrelatedChangesCannotPass(string file) {Write(evidence,file,after.Append(selected[1]).ToArray());Assert.Throws<InvalidDataException>(()=>Validate());Write(evidence,file,Array.Empty<DriverRemovalDevice>());Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void UnknownDescendantCannotBeIgnored() {Write(evidence,"after-reconciliation.json",after.Append(new DriverRemovalDevice(50,10,"Leftover","Child",1,"1.0.0.0","Loaded")).ToArray());Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void MissingFullUiTraversalCannotBeReplacedWithPassFlag() {Write(evidence,"ui-after/room-0-summary.json",new{Expected=Array.Empty<string>(),Observed=Array.Empty<string>(),FullVerticalTraversal=false});Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void RemainingUiTileCannotPass() {Write(evidence,"ui-after/room-0-summary.json",new{Expected=Array.Empty<string>(),Observed=new[]{"Demo Child"},FullVerticalTraversal=true});Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void MissingHomeCaptureCannotPass() {File.Delete(Path.Combine(evidence,"ui-after/home-restored/hierarchy.xml"));Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void WrongAppHomeCannotPass() {Write(evidence,"ui-after/plan.json",plan.App with{Profile=plan.App.Profile with{ExpectedHomeText="Wrong"}});Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void NewLogErrorIsRecomputedEvenWhenSummarySaysPassed() {Write(evidence,"after-log.json",Log("Error: App # time # Unload failure",40));Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void LostLogBaselineCannotPass() {Write(evidence,"after-log.json",newLog with{Entries=["Notice: ctpd # time # Different boot"]});Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void LogFromDifferentProcessorCannotPass() {Write(evidence,"after-log.json",newLog with{Host="other"});Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void OriginalBaselineFailureCannotBeAccepted() {Write(original,"removal/before-ui.json",new DriverRemovalUiOutcome(false,true));PinOriginal();Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void NoOriginalRemovalIntentCannotBeReplacedByCurrentAbsence() {Write(original,"removal/stopped.json",new{RemovalAttempted=false,ErrorType="InvalidDataException",Utc=start.AddSeconds(20),InspectBeforeRetry=true});PinOriginal();Assert.Throws<InvalidDataException>(()=>Validate());}
 [Test]public void AcceptedEvidenceIsRevalidatedAndStillKeepsOriginalResultAbsent() {
  var release=new SubmissionWorkflowRelease("test/repo",1,"v1",new('a',40),plan.PackageSha256,new('b',64),new('c',64));
  var checkpoint=new SubmissionWorkflowCheckpoint(2,new('d',64),release,SubmissionWorkflowStage.FinalizeTests,SubmissionWorkflowStatus.Waiting,"operation","recovery-requested",[],DateTimeOffset.UtcNow);
  var c=new SubmissionWorkflowStepContext(root,checkpoint);var end=new SubmissionWorkflowReceipt("endurance.json",new('e',64));
  Write(root,"removal/intent.json",new AutomationRemoval.Intent(checkpoint.InputSha256,"operation",end,new('f',64),plan,start));
  var result=Validate();string folder=Path.GetDirectoryName(evidence)!;File.WriteAllText(Path.Combine(folder,"producer.cs"),"synthetic producer");
  Write(folder,"reconciled-result.json",result);Write(root,AutomationRemovalReconciliation.AcceptancePath,new {checkpoint.InputSha256,checkpoint.OperationId,OriginalEvidenceSha256=AutomationRemovalReconciliation.OriginalHash(root),EvidenceSha256=AutomationRemovalReconciliation.InventoryHash(evidence),ProducerSha256=AutomationFiles.Hash(Path.Combine(folder,"producer.cs")),VerifiedBy="test",AcceptedUtc=DateTimeOffset.UtcNow});
  Assert.That(AutomationRemoval.ReadResult(c).Passed,Is.True);Assert.That(AutomationRemoval.ReadResult(c with{Checkpoint=checkpoint with{Stage=SubmissionWorkflowStage.PrepareReview,OperationId="review-operation"}}).Passed,Is.True);Assert.That(File.Exists(Path.Combine(original,"result.json")),Is.False);
  File.AppendAllText(Path.Combine(evidence,"ui-after/home-restored/hierarchy.xml")," changed");Assert.Throws<InvalidDataException>(()=>AutomationRemoval.ReadResult(c));
 }
 [Test]public void LiveOriginalWorkerCannotBeAccepted() {
  using var process=Process.GetCurrentProcess();var context=JsonSerializer.Deserialize<AndroidRunContext>(File.ReadAllText(Path.Combine(original,"app-opening/context.json")),AutomationFiles.Json)!;
  Write(original,"app-opening/context.json",context with{CoordinatorPid=process.Id,CoordinatorStartUtcTicks=process.StartTime.ToUniversalTime().Ticks});
  var release=new SubmissionWorkflowRelease("test/repo",1,"v1",new('a',40),plan.PackageSha256,new('b',64),new('c',64));
  var state=new SubmissionWorkflowCheckpoint(2,new('d',64),release,SubmissionWorkflowStage.FinalizeTests,SubmissionWorkflowStatus.Waiting,"operation",null,[],DateTimeOffset.UtcNow);
  Assert.Throws<InvalidDataException>(()=>AutomationRemovalReconciliation.RequireStopped(new(root,state)));
 }
}
