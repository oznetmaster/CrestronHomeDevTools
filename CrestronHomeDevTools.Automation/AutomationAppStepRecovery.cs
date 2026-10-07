// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

// An explicit replacement is a new operation, not a reset of the failed one.
internal static class AutomationAppStepRecovery
{
 internal const string Attempts="recovery-attempts";
 internal sealed record Request(int SchemaVersion,string Phase,int Step,string AttemptId,string StateSha256,
  string OriginalEvidenceSha256,string FailedOutcome,InstalledDriverTestPlan Replacement,
  string SourceSha256,string ProfileSha256,Reconciliation? Restoration=null,AutomationAppScopeRevision.Plan? ScopeRevision=null) {
  [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public AutomationAppCaseRecovery.Plan? CaseRecovery {get;init;}
 }
 internal sealed record Reconciliation(string OriginalEvidenceSha256,string VerifiedBy,DateTimeOffset VerifiedUtc,
  bool OriginalStateRestored,bool CleanupConfirmed,bool ReservationsReleased,SubmissionWorkflowReceipt[] Evidence);
 internal sealed record Inspection(string StateSha256,string OriginalEvidenceSha256,string FailedOutcome,
  bool RestorationRequired,InstalledDriverTestPlan Original);
 internal sealed record Completion(string AttemptId,string ProducerPrefix,string OriginalEvidenceSha256,
  string[]? ProducerPrefixes=null,Dictionary<string,string>? ObservationSources=null) {
  [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public Dictionary<string,string>? RetainedProducerReceipts {get;init;}
  [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public AutomationAppCaseRecovery.Provenance? CaseRecovery {get;init;}
 }
 private sealed record Intent(string OperationId,string InputSha256,string SourceDigest,string ProfileSha256);
 private sealed record Binding(Request Request,string InputSha256,Dictionary<string,string> Tools);
 private sealed record ToolRepair(string OriginalBindingSha256,Dictionary<string,string> Tools,string Reason,DateTimeOffset RecordedUtc);
 private static Dictionary<string,string> CurrentTools()=>new[]{typeof(AutomationAppStepRecovery).Assembly,typeof(InstalledDriverTests).Assembly,typeof(DriverPayloadInspection).Assembly}
  .ToDictionary(a=>a.GetName().Name!,a=>AutomationFiles.AssemblyHash(a));
 // Explicit, append-only repair is limited to an attempt that never entered an invocation.
 // It cannot replace fixture/candidate inputs or replay a physical operation.
 internal static void AuthorizeUnstartedToolRepair(string root,Request request,string originalBindingSha256,string reason) {
  RequireId(request.AttemptId);
  string attempt=Path.Combine(root,Prefix(request.AttemptId)),bound=Path.Combine(attempt,"attempt.json");
  if(string.IsNullOrWhiteSpace(reason) || reason.Length>3000 || !File.Exists(bound) || AutomationFiles.Hash(bound)!=originalBindingSha256)
   throw new InvalidDataException("Tool repair requires the exact inspected binding and an explanation.");
  var previous=AutomationFiles.Read<Binding>(bound);
  if(!Equal(previous.Request,request) || request.ScopeRevision==null ||
   Directory.GetDirectories(attempt).Length!=0 ||
   !Directory.GetFiles(attempt).Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(new[]{"attempt.json","original-evidence.json"},StringComparer.Ordinal) ||
   EvidenceHash(root,request.AttemptId)!=request.OriginalEvidenceSha256)
   throw new InvalidDataException("Only an unchanged, uninvoked scope revision can receive a tool repair.");
  AutomationFiles.Write(Path.Combine(attempt,"tool-repair.json"),new ToolRepair(originalBindingSha256,CurrentTools(),reason,DateTimeOffset.UtcNow));
 }
 private static bool BindingMatches(string attempt,Binding previous,Binding current) {
  if(Equal(previous,current))return true;
  string repairPath=Path.Combine(attempt,"tool-repair.json");
  if(!Equal(previous.Request,current.Request) || previous.InputSha256!=current.InputSha256 || !File.Exists(repairPath))return false;
  var repair=AutomationFiles.Read<ToolRepair>(repairPath);
  return repair.OriginalBindingSha256==AutomationFiles.Hash(Path.Combine(attempt,"attempt.json")) &&
   !string.IsNullOrWhiteSpace(repair.Reason) && repair.RecordedUtc!=default && Equal(repair.Tools,current.Tools);
 }
 private static byte[] Bytes<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,AutomationFiles.Json);
 private static bool Equal<T>(T left,T right)=>Bytes(left).AsSpan().SequenceEqual(Bytes(right));
 private static string Prefix(string id)=>"installed-app/"+Attempts+"/"+id+"/";
 internal static void RequireId(string id) {
  if(!Guid.TryParseExact(id,"N",out _) || id.Any(char.IsUpper))throw new InvalidDataException("Use a new lowercase GUID attempt ID.");
 }
 internal static SubmissionWorkflowReceipt[] Inventory(string root,string? excludedAttempt=null)=>AutomationInstalledApp.Inventory(root)
  .Where(f=>excludedAttempt==null || !f.RelativePath.Replace('\\','/').StartsWith(Prefix(excludedAttempt),StringComparison.Ordinal)).ToArray();
 internal static string EvidenceHash(string root,string? excludedAttempt=null)=>Convert.ToHexStringLower(SHA256.HashData(Bytes(Inventory(root,excludedAttempt))));
 internal static InstalledDriverTestResult Failure(string root,string relative) {
  if(!relative.EndsWith("/InstalledDriverTests.json",StringComparison.Ordinal) ||
   !SubmissionEvidence.SafeEvidencePath(root,relative,out var path) || new FileInfo(path).Length>65536)
   throw new InvalidDataException("Select a retained installed-app failure outcome.");
  using var doc=JsonDocument.Parse(File.ReadAllBytes(path));
  if(doc.RootElement.TryGetProperty("State",out _))throw new InvalidDataException("Unfinished preparation requires inspection, not a failed-test replacement.");
  var result=JsonSerializer.Deserialize<InstalledDriverTestResult>(doc.RootElement.GetRawText());
  if(result==null || result.Passed || result.Tests==null)throw new InvalidDataException("Replacement requires a retained failed test invocation.");
  return result;
 }
 internal static void RequireLatestFailure(string root,Request request) {
  string attempts=Path.Combine(root,"installed-app",Attempts);
  if(!Directory.Exists(attempts))return;
  var links=new List<(string Parent,string Outcome)>();
  var directories=Directory.GetDirectories(attempts);
  if(directories.Length>128)throw new InvalidDataException("Too many app replacement attempts.");
  foreach(string directory in directories) {
   string id=Path.GetFileName(directory);RequireId(id);if(id==request.AttemptId)continue;
   string prefix=Prefix(id);
   if(!SubmissionEvidence.SafeEvidencePath(root,prefix+"attempt.json",out var bound))throw new InvalidDataException("Unresolved replacement intent requires inspection.");
   var previous=AutomationFiles.Read<Binding>(bound);
   string outcome=prefix+"installed-app/InstalledDriverTests.json";
   _=Failure(root,outcome); // An interrupted or successful uncommitted attempt must be reconciled, not replaced.
   links.Add((previous.Request.FailedOutcome,outcome));
  }
  if(links.Count==0)return;
  var leaves=links.Select(l=>l.Outcome).Except(links.Select(l=>l.Parent),StringComparer.Ordinal).ToArray();
  if(links.GroupBy(l=>l.Parent).Any(g=>g.Count()!=1) || leaves.Length!=1 || request.FailedOutcome!=leaves[0])
   throw new InvalidDataException("Replacement must follow the latest failed attempt, without branching or bypassing an unresolved attempt.");
 }
 internal static void CheckRestoration(string root,Request request,InstalledDriverTestResult failure) {
  if(failure.RestorationConfirmed && failure.CleanupConfirmed && failure.ReservationsReleased)return;
  var proof=request.Restoration;
  if(proof==null || proof.OriginalEvidenceSha256!=request.OriginalEvidenceSha256 ||
   string.IsNullOrWhiteSpace(proof.VerifiedBy) || proof.VerifiedUtc==default || proof.VerifiedUtc>DateTimeOffset.UtcNow.AddMinutes(1) ||
   !proof.OriginalStateRestored || !proof.CleanupConfirmed || !proof.ReservationsReleased || proof.Evidence is not {Length:>0 and <=64})
   throw new InvalidDataException("Verify original physical/app state, cleanup and reservation release, and provide pinned reconciliation evidence before replacement.");
  foreach(var file in proof.Evidence) {
   if(!SubmissionEvidence.SafeEvidencePath(root,file.RelativePath.Replace('\\','/'),out var path) || AutomationFiles.Hash(path)!=file.Sha256)
    throw new InvalidDataException("Restoration reconciliation evidence changed or is unavailable.");
  }
 }
 internal static void ValidateReplacement(InstalledDriverTestPlan original,Request request) {
  var next=request.Replacement;
  next.Validate();
  // Only fixture code locations and a same-version catalogue discriminator may differ.
  var normalized=next with {SourceRoots=original.SourceRoots,
   Target=next.Target with {CatalogueId=original.Target.CatalogueId},
   AndroidTests=next.AndroidTests with {Project=original.AndroidTests.Project}};
  if(!Equal(original,normalized) || AutomationFiles.Hash(next.PackagePath)!=next.PackageSha256 ||
   AutomationFiles.Hash(next.AndroidTests.ProfilePath)!=request.ProfileSha256 ||
   (next.Target.CatalogueId!=original.Target.CatalogueId &&
    DriverPayloadInspection.StorageKey(next.Target.CatalogueId,next.Target.Version)!=DriverPayloadInspection.StorageKey(original.Target.CatalogueId,original.Target.Version)))
   throw new InvalidDataException("A replacement cannot change candidate, processor, devices, profile, selected tests, timing or readiness requirements.");
 }
 internal static async Task<SubmissionWorkflowStepResult> Execute(SubmissionWorkflowStepContext context,
  SubmissionAutomationSettings settings,Request request,
  Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>> run,
  NetworkCredential credential,CancellationToken token,
  Func<AutomationAppScopeRevision.Invocation,NetworkCredential>? revisedCredentials=null) {
  RequireId(request.AttemptId);
  if(request.SchemaVersion is not (1 or 2 or 3) || (request.SchemaVersion==2)!=(request.ScopeRevision!=null) || (request.SchemaVersion==3)!=(request.CaseRecovery!=null))throw new InvalidDataException("Unsupported recovery schema.");
  string root=context.RunDirectory,attempt=Path.Combine(root,Prefix(request.AttemptId));
  string completed=Path.Combine(root,"installed-app-tests.json");
  if(File.Exists(completed)) {
   AutomationInstalledApp.VerifyRetained(root);
   var accepted=AutomationFiles.Read<Completion>(Path.Combine(root,"installed-app","replacement.json"));
   if(accepted.AttemptId!=request.AttemptId)throw new InvalidDataException("This step already completed with another attempt.");
   return new(SubmissionWorkflowStatus.Completed,new("installed-app-tests.json",AutomationFiles.Hash(completed)));
  }
  var original=settings.InstalledAppTests??throw new InvalidDataException("Missing original test plan.");
  var intent=AutomationFiles.Read<Intent>(Path.Combine(root,"installed-app-intent.json"));
  if(intent.InputSha256!=context.Checkpoint.InputSha256 || intent.OperationId!=context.Checkpoint.OperationId ||
   intent.SourceDigest!=await WorkflowEvidence.SourceDigestAsync(original.SourceRoots,token) ||
   intent.ProfileSha256!=AutomationFiles.Hash(original.AndroidTests.ProfilePath))throw new InvalidDataException("Original frozen inputs changed.");
  AutomationAppFixture.Check(root,settings,false);
  if(request.ScopeRevision!=null) await AutomationAppScopeRevision.Validate(settings,request,token);
  else if(request.CaseRecovery!=null) AutomationAppCaseRecovery.Validate(root,settings,request);
  else ValidateReplacement(original,request);
  if(EvidenceHash(root,request.AttemptId)!=request.OriginalEvidenceSha256 ||
   await WorkflowEvidence.SourceDigestAsync(request.Replacement.SourceRoots,token)!=request.SourceSha256)
   throw new InvalidDataException("Inspected evidence or replacement fixture changed.");
  RequireLatestFailure(root,request);
  var failure=Failure(root,request.FailedOutcome);
  CheckRestoration(root,request,failure);
  bool started=Directory.Exists(attempt);
  var tools=CurrentTools();
  var binding=new Binding(request,context.Checkpoint.InputSha256,tools);
  if(started) {
   if(!SubmissionEvidence.SafeEvidencePath(root,Prefix(request.AttemptId)+"attempt.json",out var bound) ||
    !BindingMatches(attempt,AutomationFiles.Read<Binding>(bound),binding))throw new InvalidDataException("Started replacement inputs changed; no replay is permitted.");
  } else {
   Directory.CreateDirectory(attempt);
   AutomationFiles.Write(Path.Combine(attempt,"attempt.json"),binding);
   AutomationFiles.Write(Path.Combine(attempt,"original-evidence.json"),Inventory(root,request.AttemptId));
   if(request.Restoration is {} reconciled) {
    string proofRoot=Path.Combine(attempt,"reconciliation");Directory.CreateDirectory(proofRoot);
    for(int i=0;i<reconciled.Evidence.Length;i++) {
     var evidence=reconciled.Evidence[i];
     if(!SubmissionEvidence.SafeEvidencePath(root,evidence.RelativePath.Replace('\\','/'),out var source))throw new InvalidDataException("Unsafe reconciliation evidence.");
     string retained=Path.Combine(proofRoot,i.ToString("D3")+".evidence");File.Copy(source,retained,false);
     if(AutomationFiles.Hash(retained)!=evidence.Sha256)throw new InvalidDataException("Reconciliation evidence changed while retaining it.");
    }
   }
  }
  if(request.ScopeRevision!=null)
   return await AutomationAppScopeRevision.Execute(context,settings,request,attempt,run,
    revisedCredentials??throw new InvalidDataException("Revised scopes require independently resolved credentials."),token);
  AutomationAppFixture.Check(attempt,settings,true);
  var next=request.Replacement;
  // The failed attempt's Ready acknowledgement must never authorize this new attempt.
  if(next.OperatorReadiness is {} ready)next=next with {OperatorReadiness=ready with {Step="app-replacement-"+request.AttemptId+".ready"}};
  string output=Path.Combine(attempt,"installed-app"),resultPath=Path.Combine(output,"InstalledDriverTests.json");
  if(!started)await run(next,credential,output,token);
  if(!File.Exists(resultPath))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-app-replacement-no-replay");
  using var doc=JsonDocument.Parse(File.ReadAllBytes(resultPath));
  if(doc.RootElement.TryGetProperty("State",out _))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-app-replacement-no-replay");
  var result=JsonSerializer.Deserialize<InstalledDriverTestResult>(doc.RootElement.GetRawText());
  if(result?.Passed!=true)return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"app-replacement-failed-inspect-retained-evidence");
  if(EvidenceHash(root,request.AttemptId)!=request.OriginalEvidenceSha256 ||
   await WorkflowEvidence.SourceDigestAsync(next.SourceRoots,token)!=request.SourceSha256 ||
   AutomationFiles.Hash(next.AndroidTests.ProfilePath)!=request.ProfileSha256 ||
   intent.SourceDigest!=await WorkflowEvidence.SourceDigestAsync(original.SourceRoots,token))
   throw new InvalidDataException("Evidence or fixture changed during replacement.");
  AutomationAppFixture.Check(root,settings,false);AutomationAppFixture.Check(attempt,settings,false);
  var completion=request.CaseRecovery==null ? new Completion(request.AttemptId,
   Prefix(request.AttemptId)+"installed-app/AndroidUI/",request.OriginalEvidenceSha256) : AutomationAppCaseRecovery.Complete(root,settings,request,attempt);
  AutomationFiles.Write(Path.Combine(root,"installed-app","replacement.json"),completion);
  return AutomationFiles.Complete(context,"installed-app-tests.json",new{context.Checkpoint.InputSha256,Files=AutomationInstalledApp.Inventory(root)});
 }

 internal static (string Run,string Step,SubmissionAutomationSettings Settings,SubmissionWorkflowCheckpoint State) Select(
  AutomationRequest request,string phase,int index) {
  var all=request.Settings;var state=SubmissionWorkflow.Read(all.PrivateRoot,all.Release);
  var expected=phase=="post-endurance"?
   (state.SchemaVersion==1?SubmissionWorkflowStage.PrepareReview:SubmissionWorkflowStage.FinalizeTests):SubmissionWorkflowStage.AppTests;
  if(state.Stage!=expected || state.Status is not (SubmissionWorkflowStatus.Failed or SubmissionWorkflowStatus.NeedsInput or SubmissionWorkflowStatus.OutcomeUnknown))
   throw new InvalidDataException("Inspect a stopped app phase before explicitly replacing a failed step.");
  string root=Path.Combine(all.PrivateRoot,SubmissionWorkflow.RunKey(all.Release));
  string phaseRoot=phase switch {"main"=>root,"pre-endurance" or "post-endurance"=>Path.Combine(root,phase),_=>throw new InvalidDataException("Select main, pre-endurance or post-endurance.")};
  var settings=phase switch {"main"=>all,"pre-endurance"=>AutomationInitialAdditionalTests.Settings(all),_=>AutomationPostEndurance.Settings(all)};
  // Recovery must use the same verified managed IDs as the original invocation.
  settings=AutomationPostEndurance.ResolveTarget(new(root,state),all,settings,false);
  if(phase=="pre-endurance")AutomationInstalledApp.VerifyRetained(root);
  if(!SubmissionEvidence.SafeEvidencePath(root,Path.GetRelativePath(root,Path.Combine(phaseRoot,"target-plan.json")).Replace('\\','/'),out var target))
   throw new InvalidDataException("Missing retained app target plan.");
  var plan=AutomationFiles.Read<InstalledDriverTestPlan>(target);
  var steps=settings.InstalledAppSteps;
  if(steps==null) {
   if(index!=0)throw new InvalidDataException("An unsplit app phase has only step zero.");

   settings=settings with{InstalledAppTests=plan};
   AutomationInstalledApp.Validate(settings);
   return(root,phaseRoot,settings,state);
  }
  if(index<0 || index>=steps.Length)throw new InvalidDataException("Select an existing app step.");
  AutomationAppSteps.Validate(settings);
  for(int i=0;i<index;i++)AutomationInstalledApp.VerifyRetained(Path.Combine(phaseRoot,"installed-app","steps",i.ToString("D3")));
  string stepRoot=Path.Combine(phaseRoot,"installed-app","steps",index.ToString("D3"));
  var step=steps[index];
  var child=settings with {InstalledAppSteps=null,InstalledAppTests=plan with {
   AndroidTests=plan.AndroidTests with {RequiredTests=step.Tests},
   OperatorReadiness=step.OperatorInstructions!=null?AutomationAppSteps.Prepared(settings.OperatorInbox!,phaseRoot,index,step):null}};
  AutomationInstalledApp.Validate(child);
  return(root,stepRoot,child,state);
 }
 internal static async Task<int> Command(AutomationRequest request,string phase,int index,string? recoveryPath,string? recoveryHash,CancellationToken token) {
  var selected=Select(request,phase,index);
  string? stateHash=null;
  using(var gate=new FileStream(Path.Combine(selected.Run,"run.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
   stateHash=AutomationFiles.Hash(Path.Combine(selected.Run,"state.json"));
   if(recoveryPath==null) {
    var outcomes=Inventory(selected.Step).Where(f=>f.RelativePath.Replace('\\','/').EndsWith("/InstalledDriverTests.json",StringComparison.Ordinal))
     .Select(f=>f.RelativePath.Replace('\\','/')).Where(f=>{try{Failure(selected.Step,f);return true;}catch(InvalidDataException){return false;}}).ToArray();
    if(outcomes.Length==0)throw new InvalidDataException("No retained failed test outcome is available.");
    var reports=outcomes.Select(f=>{var failed=Failure(selected.Step,f);return new Inspection(stateHash,EvidenceHash(selected.Step),f,
     !failed.RestorationConfirmed || !failed.CleanupConfirmed || !failed.ReservationsReleased,selected.Settings.InstalledAppTests!);}).ToArray();
    Console.WriteLine(JsonSerializer.Serialize(reports,AutomationFiles.Json));return 0;
   }
   if(AutomationFiles.Hash(recoveryPath)!=recoveryHash)throw new InvalidDataException("Recovery plan changed after review.");
   var plan=AutomationFiles.Read<Request>(recoveryPath);
   if(plan.StateSha256!=stateHash || plan.Phase!=phase || plan.Step!=index)throw new InvalidDataException("Recovery plan does not bind the inspected state and step.");
   if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
   var saved=DevToolsCredentialBindings.Read(plan.ScopeRevision?.Invocations[0].CredentialBindings??selected.Settings.CredentialBindings).Resolve(DevToolsCredentialPurpose.Processor,plan.Replacement.Host);
   SubmissionAutomationStages.VerifyProcessorPins(request.Settings,plan.Replacement.Host,saved.CertificateSha256,saved.SshFingerprint);
   var result=await Execute(new(selected.Step,selected.State),selected.Settings,plan,
    async(p,c,r,t)=>{
     await AutomationDriverReadiness.Check(p.Host,p.CertificateSha256,c,new(p.Target.DeviceId,p.Target.Model,p.Target.Version,"Existing"),r+"-readiness",t);
     await AutomationAndroidReadiness.Check(p.AndroidTests.ProfilePath,Path.Combine(r+"-readiness","android"),t);
     return await InstalledDriverTests.RunAsync(p,c,r,t);
    },new(saved.UserName,saved.Password),token,invocation=>{
     if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
     var bound=DevToolsCredentialBindings.Read(invocation.CredentialBindings).Resolve(DevToolsCredentialPurpose.Processor,invocation.Tests.Host);
     SubmissionAutomationStages.VerifyProcessorPins(request.Settings,invocation.Tests.Host,bound.CertificateSha256,bound.SshFingerprint);
     if(bound.CertificateSha256!=invocation.Tests.CertificateSha256 || bound.SshFingerprint!=invocation.Tests.SshFingerprint)
      throw new InvalidDataException("Revised processor pins differ from the saved binding.");
     return new(bound.UserName,bound.Password);
    });
   Console.WriteLine(JsonSerializer.Serialize(result,AutomationFiles.Json));
   if(result.Status!=SubmissionWorkflowStatus.Completed)return 3;
  }
  // The legacy boundary must be explicitly migrated after its postchecks pass.
  // Repairing its evidence does not make phase three ready.
  if(selected.State.SchemaVersion==2) SubmissionWorkflow.RequestRecovery(request.Settings.PrivateRoot,request.Settings.Release,stateHash);
  return 0;
 }
}
