// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using CrestronHomeNUnit.Android;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

// Explicit evidence acceptance, never an equipment operation or automatic retry.
internal static class AutomationRemovalReconciliation
{
 internal const string AcceptancePath="removal/reconciliation/accepted.json";
 internal sealed record Request(int SchemaVersion,string StateSha256,string OriginalEvidenceSha256,
  string EvidenceDirectory,string EvidenceSha256,string VerifiedBy,string ProducerSource,string ProducerSha256);
 internal sealed record FilePin(string Path,string Sha256);
 private sealed record Acceptance(string InputSha256,string OperationId,string OriginalEvidenceSha256,
  string EvidenceSha256,string ProducerSha256,string VerifiedBy,DateTimeOffset AcceptedUtc);
 internal sealed record Owner(string OwnerId,string Host,bool RemovalRequested);
 private sealed record OriginalOwnership(string Owner,string Host,bool RemovalRequested);
 private sealed record ReadIntent(DateTimeOffset Utc,string Owner,bool ReadOnly,bool RemovalReplay,string OriginalPlanSha256,string OriginalFailureSha256);
 private sealed record Completion(DateTimeOffset Utc,string Owner,bool OriginalFailurePreserved,bool RemovalReplay,int PhysicalActions,
  bool RemovalConfirmed,bool OtherDevicesPreserved,bool UiAbsenceConfirmed,bool HomeRestored,bool ReservationsReleased,
  bool LogComparable,bool NoNewErrorsOrExceptions,bool Passed);
 private sealed record RemovalIntent(DriverRemovalTarget Target,DateTimeOffset Utc);
 private sealed record Stop(bool RemovalAttempted,string? ErrorType,DateTimeOffset Utc,bool InspectBeforeRetry);
 private sealed record Candidate(InstalledDriverTestTarget Target,DriverPayloadMatch Payload);
 private sealed record Summary(string[] Expected,string[] Observed,bool FullVerticalTraversal);
 private static bool Equal<T>(T a,T b)=>JsonSerializer.Serialize(a,AutomationFiles.Json)==JsonSerializer.Serialize(b,AutomationFiles.Json);
 private static bool Pin(string a,string b)=>a.Equals(b,StringComparison.OrdinalIgnoreCase);
 private static void Require(bool condition,string reason){if(!condition)throw new InvalidDataException(reason);}
 internal static FilePin[] Inventory(string root)
 {
  Require(Path.IsPathFullyQualified(root)&&Directory.Exists(root),"Evidence requires an existing absolute directory.");
  var pending=new Stack<DirectoryInfo>();pending.Push(new(root));var files=new List<FilePin>();int count=0;long bytes=0;
  while(pending.Count>0) {
   var dir=pending.Pop();Require((dir.Attributes&FileAttributes.ReparsePoint)==0,"Evidence links are not accepted.");
   foreach(var item in dir.EnumerateFileSystemInfos()) {
    Require(++count<=8192&&(item.Attributes&FileAttributes.ReparsePoint)==0,"Evidence tree is oversized or contains a link.");
    if(item is DirectoryInfo child)pending.Push(child);
    else {var f=(FileInfo)item;Require(f.Length<=64L*1024*1024&&(bytes+=f.Length)<=512L*1024*1024,"Evidence size exceeded.");
     files.Add(new(Path.GetRelativePath(root,f.FullName).Replace(Path.DirectorySeparatorChar,'/'),AutomationFiles.Hash(f.FullName)));}
   }
  }
  return files.OrderBy(f=>f.Path,StringComparer.Ordinal).ToArray();
 }
 internal static string InventoryHash(string root)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Inventory(root),AutomationFiles.Json)));
 private static T Read<T>(string root,string path) {
  if(!SubmissionEvidence.SafeEvidencePath(root,path,out var full))throw new InvalidDataException("Missing or unsafe reconciliation evidence: "+path);
  return AutomationFiles.Read<T>(full);
 }
 // Recompute the substantive conclusions from retained producer evidence. Success flags alone are insufficient.
 internal static DriverRemovalWorkflowResult ValidateEvidence(string original,string evidence,DriverRemovalWorkflowPlan plan)
 {
  Require(!File.Exists(Path.Combine(original,"result.json")),"Completed original removal must use its original result.");
  _=Read<JsonElement>(original,"failure.json");
  Require(Equal(Read<DriverRemovalWorkflowPlan>(original,"plan.json"),plan),"Removal scope changed.");
  Require(Pin(AutomationFiles.Hash(Path.Combine(original,"candidate.pkg")),plan.PackageSha256),"Candidate changed.");
  var candidate=Read<Candidate>(original,"before-candidate.json");
  Require(Equal(candidate.Target,plan.Target)&&Pin(candidate.Payload.PackageSha256,plan.PackageSha256)&&
   candidate.Payload.CatalogueId==plan.Target.CatalogueId&&candidate.Payload.Package.Model==plan.Target.Model&&
   Version.Parse(candidate.Payload.Package.Version)==Version.Parse(plan.Target.Version)&&candidate.Payload.Files.Count>0,
   "Original candidate association was not verified.");
  var own=Read<OriginalOwnership>(original,"ownership.json");
  var opening=Read<AndroidRunContext>(original,"app-opening/context.json");
  Require(own.RemovalRequested&&own.Host==plan.Host&&own.Owner==opening.RunId&&opening.ProcessorAddress==plan.Host&&opening.InstalledDriverId==plan.Target.DeviceId&&Pin(opening.PackageSha256,plan.PackageSha256),
   "Original ownership differs from the processor.");
  var pins=Read<FilePin[]>(evidence,"original-inventory.json");var actual=Inventory(original);
  Require(pins.Length==actual.Length&&pins.OrderBy(p=>p.Path,StringComparer.Ordinal).Zip(actual).All(p=>p.First.Path==p.Second.Path&&Pin(p.First.Sha256,p.Second.Sha256)),
   "Original failure evidence changed or is incomplete.");
  var intent=Read<ReadIntent>(evidence,"intent.json");var done=Read<Completion>(evidence,"completion.json");
  Require(intent.ReadOnly&&!intent.RemovalReplay&&intent.Owner==own.Owner&&Pin(intent.OriginalPlanSha256,AutomationFiles.Hash(Path.Combine(original,"plan.json")))&&
   Pin(intent.OriginalFailureSha256,AutomationFiles.Hash(Path.Combine(original,"failure.json"))),"Reconciliation was not bound to this original failure.");
  Require(done.Owner==own.Owner&&done.OriginalFailurePreserved&&!done.RemovalReplay&&done.PhysicalActions==0&&done.RemovalConfirmed&&done.OtherDevicesPreserved&&
   done.UiAbsenceConfirmed&&done.HomeRestored&&done.ReservationsReleased&&done.LogComparable&&done.NoNewErrorsOrExceptions&&done.Passed&&
   done.Utc>=intent.Utc&&intent.Utc!=default&&done.Utc<=DateTimeOffset.UtcNow.AddMinutes(1),"Reconciliation is incomplete or required physical replay.");
  var t=plan.Target;var expected=new DriverRemovalTarget(plan.Host,t.DeviceId,t.ParentDeviceId,t.Name,t.Model,t.Version,t.LocationId);
  var removal=Read<RemovalIntent>(original,"removal/removal-intent.json");
  Require(Equal(removal.Target,expected)&&removal.Utc<intent.Utc&&Read<Stop>(original,"removal/stopped.json").RemovalAttempted,
   "Original removal was not attempted before the read-only reconciliation.");
  var before=Read<DriverRemovalDevice[]>(original,"removal/before-inventory.json");var selected=Read<DriverRemovalDevice[]>(original,"removal/removal-scope.json");
  Require(before.Length<=4096&&before.Select(d=>d.Id).Distinct().Count()==before.Length,"Invalid original inventory.");
  var root=before.SingleOrDefault(d=>d.Id==t.DeviceId);
  Require(root!=null&&root.ParentDeviceId==t.ParentDeviceId&&root.Name==t.Name&&root.Model==t.Model&&root.LocationId==t.LocationId&&
   Version.TryParse(root.Version,out var originalVersion)&&originalVersion==Version.Parse(t.Version)&&root.LoadingStatus=="Loaded","Original installed driver identity differs.");
  var scope=new HashSet<int>{t.DeviceId};bool changed;
  do{changed=false;foreach(var d in before)if(d.ParentDeviceId is int p&&scope.Contains(p)&&scope.Add(d.Id))changed=true;}while(changed);
  Require(selected.OrderBy(d=>d.Id).SequenceEqual(before.Where(d=>scope.Contains(d.Id)).OrderBy(d=>d.Id)),"Removal scope omitted or added devices.");
  DriverRemovalAppObserver.ValidateScope(plan.App,selected);
  Require(Read<DriverRemovalUiOutcome>(original,"removal/before-ui.json") is {Passed:true,HomeRestored:true},"Original before-removal observation did not pass.");
  CheckUi(Path.Combine(original,"removal/ui-before"),plan.App,false);
  var preserved=before.Where(d=>!scope.Contains(d.Id)).ToArray();
  void CheckInventory(DriverRemovalDevice[] all) {
   Require(all.Length<=4096&&all.Select(d=>d.Id).Distinct().Count()==all.Length&&preserved.All(all.Contains)&&
    !all.Any(d=>scope.Contains(d.Id)||d.ParentDeviceId is int p&&scope.Contains(p)),"Removed tree remains or unrelated device identity changed.");
  }
  CheckInventory(Read<DriverRemovalDevice[]>(original,"removal/after-inventory.json"));
  CheckInventory(Read<DriverRemovalDevice[]>(evidence,"before-reconciliation.json"));
  CheckInventory(Read<DriverRemovalDevice[]>(evidence,"after-reconciliation.json"));
  CheckUi(Path.Combine(evidence,"ui-after"),plan.App,true);
  var oldLog=Read<ProcessorErrorLogSnapshot>(original,"removal/before-log.json");var newLog=Read<ProcessorErrorLogSnapshot>(evidence,"after-log.json");
  Require(oldLog.Host==plan.Host&&newLog.Host==plan.Host&&oldLog.ObservedUtc<=removal.Utc&&newLog.RequestSentUtc>=intent.Utc&&newLog.ObservedUtc<=done.Utc,
   "Logs do not bracket the original removal and reconciliation.");
  var interval=ProcessorErrorLog.Compare(oldLog,newLog);
  Require(interval.Comparable&&interval.NoNewErrorsOrExceptions&&Equal(interval,Read<ProcessorErrorLogInterval>(evidence,"log-interval.json")),
   "The complete original-to-reconciliation log interval did not pass.");
  return new(true,true,true,new(true,true,true,true,true,interval),null);
 }
 private static void CheckUi(string root,DriverRemovalAppPlan plan,bool removed) {
  Require(Equal(Read<DriverRemovalAppPlan>(root,"plan.json"),plan)&&Read<DriverRemovalUiOutcome>(root,"outcome.json") is {Passed:true,HomeRestored:true},"UI scope or outcome differs.");
  Require(!File.Exists(Path.Combine(root,"observation-failure.json")),"UI producer retained an assertion failure.");
  var files=Inventory(root);
  void Capture(string prefix) {
   foreach(string file in new[]{prefix+"/hierarchy.xml",prefix+"/screen.png"})Require(files.Any(f=>f.Path==file),"UI capture missing: "+file);
  }
  void Summary(string name,string[] expected) {
   var summary=Read<Summary>(root,name+"-summary.json");
   Require(summary.FullVerticalTraversal&&summary.Expected.Order().SequenceEqual(expected.Order())&&summary.Observed.Order().SequenceEqual(expected.Order()),"UI membership or full traversal was not verified.");
   Capture(name+"-up-0");Capture(name+"-down-0");
  }
  Summary("home",removed?[]:plan.Tiles.Where(t=>t.OnHome).Select(t=>t.Name).Distinct().ToArray());
  foreach(var group in plan.Tiles.GroupBy(t=>t.RoomName)) {
   string index=Array.IndexOf(plan.Tiles,group.First()).ToString(System.Globalization.CultureInfo.InvariantCulture);
   Summary("room-"+index,removed?[]:group.Where(t=>!t.NativeLight).Select(t=>t.Name).ToArray());
   if(group.Any(t=>t.NativeLight)) {
    if(removed&&Directory.Exists(Path.Combine(root,"lights-"+index+"-unavailable")))Capture("lights-"+index+"-unavailable");
    else Summary("lights-"+index,removed?[]:group.Where(t=>t.NativeLight).Select(t=>t.Name).ToArray());
   }
  }
  Capture("home-restored");
 }
 internal static DriverRemovalWorkflowResult VerifyAccepted(SubmissionWorkflowStepContext c) {
  string folder=Path.Combine(c.RunDirectory,"removal/reconciliation");
  var saved=Read<Acceptance>(c.RunDirectory,AcceptancePath);
  var intent=Read<AutomationRemoval.Intent>(c.RunDirectory,"removal/intent.json");
  Require(saved.InputSha256==c.Checkpoint.InputSha256&&saved.OperationId==intent.OperationId&&intent.InputSha256==c.Checkpoint.InputSha256&&
   saved.OriginalEvidenceSha256==OriginalHash(c.RunDirectory)&&saved.EvidenceSha256==InventoryHash(Path.Combine(folder,"evidence"))&&
   saved.ProducerSha256==AutomationFiles.Hash(Path.Combine(folder,"producer.cs")),"Accepted reconciliation evidence changed.");
  var result=ValidateEvidence(Path.Combine(c.RunDirectory,"removal/operation"),Path.Combine(folder,"evidence"),intent.Plan);
  Require(Equal(result,Read<DriverRemovalWorkflowResult>(folder,"reconciled-result.json")),"Reconciled result differs from its evidence.");
  return result;
 }
 internal static string OriginalHash(string root) {
  var files=AutomationRemoval.Inventory(root).Where(f=>f.RelativePath!=AutomationRemoval.ObservationPath&&!f.RelativePath.StartsWith("removal/reconciliation/",StringComparison.Ordinal)).ToArray();
  return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(files,AutomationFiles.Json)));
 }
 internal static void RequireStopped(SubmissionWorkflowStepContext c) {
  Require(c.Checkpoint.SchemaVersion==2&&c.Checkpoint.Stage==SubmissionWorkflowStage.FinalizeTests&&
   c.Checkpoint.Status is not (SubmissionWorkflowStatus.Running or SubmissionWorkflowStatus.Completed),"Reconciliation requires stopped final tests.");
  var opening=Read<AndroidRunContext>(c.RunDirectory,"removal/operation/app-opening/context.json");
  Require(opening.Machine.Equals(Environment.MachineName,StringComparison.OrdinalIgnoreCase),"Inspect original worker on its recorded host.");
  try {using var process=Process.GetProcessById(opening.CoordinatorPid);
   Require(process.HasExited||process.StartTime.ToUniversalTime().Ticks!=opening.CoordinatorStartUtcTicks,"Original worker is still running.");}
  catch(ArgumentException) { }
 }
 internal static int Command(AutomationRequest request,string planPath,string planPin) {
  Require(AutomationFiles.Hash(planPath)==planPin,"Reconciliation request changed.");var plan=AutomationFiles.Read<Request>(planPath);
  var s=request.Settings;string root=Path.Combine(s.PrivateRoot,SubmissionWorkflow.RunKey(s.Release));
  return SubmissionWorkflow.WithVerifiedCheckpoint(s.PrivateRoot,s.Release,c=> {
  RequireStopped(c);
  Require(plan.SchemaVersion==1&&AutomationFiles.Hash(Path.Combine(root,"state.json"))==plan.StateSha256&&
   !string.IsNullOrWhiteSpace(plan.VerifiedBy)&&plan.VerifiedBy.Length<=2000,"Request does not bind the inspected stopped state.");
  _=AutomationRemoval.Validate(s);AutomationPostEndurance.VerifyRetained(c);
  var intent=Read<AutomationRemoval.Intent>(root,"removal/intent.json");
  Require(intent.InputSha256==c.Checkpoint.InputSha256&&intent.OperationId==c.Checkpoint.OperationId&&
   c.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.Endurance,out var endurance)&&intent.Endurance==endurance&&
   SubmissionEvidence.SafeEvidencePath(root,endurance.RelativePath,out var endurancePath)&&AutomationFiles.Hash(endurancePath)==endurance.Sha256&&
   intent.PostEnduranceSha256==AutomationFiles.Hash(Path.Combine(root,"post-endurance/installed-app-tests.json"))&&Equal(intent.Plan,AutomationRemoval.Resolve(c,s)),"Original removal no longer binds the completed tests and settings.");
  string folder=Path.Combine(root,"removal/reconciliation");
  if(File.Exists(Path.Combine(root,"removal-evidence.json"))) {
   AutomationRemoval.VerifyRetained(c);Console.WriteLine("Removal reconciliation already accepted; no equipment action.");return 0;
  }
  Require(OriginalHash(root)==plan.OriginalEvidenceSha256,"Original removal evidence changed.");
  if(!Directory.Exists(folder)) {
   Require(InventoryHash(plan.EvidenceDirectory)==plan.EvidenceSha256&&AutomationFiles.Hash(plan.ProducerSource)==plan.ProducerSha256,"Inspected diagnostic or producer changed.");
   _=ValidateEvidence(Path.Combine(root,"removal/operation"),plan.EvidenceDirectory,intent.Plan);
   Directory.CreateDirectory(folder);AutomationFiles.Write(Path.Combine(folder,"request.json"),plan);
   File.Copy(plan.ProducerSource,Path.Combine(folder,"producer.cs"),false);
   foreach(var file in Inventory(plan.EvidenceDirectory)) {
    string destination=Path.Combine(folder,"evidence",file.Path);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.Copy(Path.Combine(plan.EvidenceDirectory,file.Path),destination,false);
   }
  } else Require(Equal(Read<Request>(folder,"request.json"),plan),"A different or incomplete reconciliation is retained; inspect it without resetting.");
  Require(OriginalHash(root)==plan.OriginalEvidenceSha256&&InventoryHash(Path.Combine(folder,"evidence"))==plan.EvidenceSha256&&
   AutomationFiles.Hash(Path.Combine(folder,"producer.cs"))==plan.ProducerSha256,"Retained reconciliation snapshot differs.");
  var result=ValidateEvidence(Path.Combine(root,"removal/operation"),Path.Combine(folder,"evidence"),intent.Plan);
  if(!File.Exists(Path.Combine(folder,"reconciled-result.json")))AutomationFiles.Write(Path.Combine(folder,"reconciled-result.json"),result);
  if(!File.Exists(Path.Combine(root,AcceptancePath)))AutomationFiles.Write(Path.Combine(root,AcceptancePath),new Acceptance(c.Checkpoint.InputSha256,c.Checkpoint.OperationId!,plan.OriginalEvidenceSha256,
   plan.EvidenceSha256,plan.ProducerSha256,plan.VerifiedBy,DateTimeOffset.UtcNow));
  _=VerifyAccepted(c);
  Require(!File.Exists(Path.Combine(root,AutomationRemoval.ObservationPath)),"A partial observation commit needs inspection; do not replace it.");
  var outcome=AutomationRemoval.RecordOutcome(c,s,intent,result);
  Console.WriteLine(JsonSerializer.Serialize(outcome,AutomationFiles.Json));
  // This only accepts evidence. The ordinary NUnit final-tests case advances the stage.
  return outcome.Status==SubmissionWorkflowStatus.Completed?0:3;
  });
 }
}
