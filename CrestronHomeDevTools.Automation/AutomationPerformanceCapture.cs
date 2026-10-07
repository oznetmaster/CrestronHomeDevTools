// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

// Additional phase-two observations do not replace an accepted test or its measurements.
internal static class AutomationPerformanceCapture
{
 internal const string Folder="performance-captures";
 internal sealed record Pair(string Name,string TestName,string Series);
 internal sealed record Request(int SchemaVersion,string AttemptId,int Step,string Reason,string StateSha256,
  SubmissionWorkflowReceipt Endurance,SubmissionWorkflowReceipt PostTests,InstalledDriverTestPlan Plan,
  string SourceSha256,string ProfileSha256,Pair[] Pairs);
 internal sealed record Binding(string InputSha256,Request Request);
 internal sealed record Receipt(string InputSha256,SubmissionWorkflowReceipt Binding,SubmissionWorkflowReceipt Tests);
 private static string Prefix(string id)=>Folder+"/"+id+"/";
 private static bool Equal<T>(T a,T b)=>JsonSerializer.SerializeToUtf8Bytes(a,AutomationFiles.Json).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(b,AutomationFiles.Json));
 private static void Check(string root,SubmissionWorkflowReceipt file) {
  if(!SubmissionEvidence.SafeEvidencePath(root,file.RelativePath,out var path)||AutomationFiles.Hash(path)!=file.Sha256)
   throw new InvalidDataException("Retained performance capture evidence changed or is missing.");
 }
 internal static void Validate(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings,Request request,bool collecting) {
  AutomationAppStepRecovery.RequireId(request.AttemptId);
  if(request.SchemaVersion!=1 || request.Step<0 || request.Step>=128 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length>3000 ||
   settings.ResponseComparison is not {Assessment:not null,Limits:null} comparison || request.Pairs is not {Length:>0 and <=32} ||
   request.Pairs.Select(p=>p.Name).Distinct(StringComparer.Ordinal).Count()!=request.Pairs.Length)
   throw new InvalidDataException("Additional capture requires a reviewed qualitative evidence gap and distinct existing response pairs.");
  if(collecting && (context.Checkpoint.Stage!=SubmissionWorkflowStage.FinalizeTests || context.Checkpoint.Status!=SubmissionWorkflowStatus.Waiting ||
   context.Checkpoint.ReasonCode!="performance-assessment-required" || File.Exists(Path.Combine(context.RunDirectory,comparison.Assessment)) ||
   Directory.Exists(Path.Combine(context.RunDirectory,"response-comparison")) || File.Exists(Path.Combine(context.RunDirectory,AutomationFinalTests.ReceiptName))))
   throw new InvalidDataException("Collect additional evidence only at the unfinished phase-two performance assessment boundary.");
  if(!context.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.Endurance,out var endurance) || endurance!=request.Endurance ||
   request.PostTests.RelativePath!="post-endurance/installed-app-tests.json")throw new InvalidDataException("Capture must bind the accepted endurance and post-tests.");
  Check(context.RunDirectory,request.Endurance);Check(context.RunDirectory,request.PostTests);
  AutomationPostEndurance.VerifyRetained(context);
  var original=AutomationFiles.Read<InstalledDriverTestPlan>(Path.Combine(context.RunDirectory,"post-endurance/target-plan.json"));
  string step="";
  if(settings.PostEnduranceAppSteps is {} steps) {
   if(request.Step>=steps.Length || steps[request.Step].OperatorInstructions!=null)throw new InvalidDataException("Physical steps cannot be supplemental captures.");
   original=original with{AndroidTests=original.AndroidTests with{RequiredTests=steps[request.Step].Tests}};
   step="installed-app/steps/"+request.Step.ToString("D3")+"/";
  } else if(request.Step!=0)throw new InvalidDataException("Unsplit post-tests have only step zero.");
  var selected=request.Plan.AndroidTests.RequiredTests;
  if(original.OperatorReadiness!=null || request.Plan.OperatorReadiness!=null || request.Plan.AndroidTests.ManagedChildren.Count!=0 ||
   original.AndroidTests.ManagedChildren.Count!=0 || selected is not {Count:>0} || selected.Distinct(StringComparer.Ordinal).Count()!=selected.Count ||
   original.AndroidTests.RequiredTests==null || selected.Any(n=>!original.AndroidTests.RequiredTests.Contains(n,StringComparer.Ordinal)))
   throw new InvalidDataException("Select only existing ordinary cases, without readiness prompts or provisioning.");
  var normalized=request.Plan with{SourceRoots=original.SourceRoots,AndroidTests=request.Plan.AndroidTests with{Project=original.AndroidTests.Project,RequiredTests=original.AndroidTests.RequiredTests}};
  if(!Equal(original,normalized))throw new InvalidDataException("Capture cannot change candidate, processor, devices, profile, timing or assertions.");
  request.Plan.Validate();
  if(AutomationFiles.Hash(request.Plan.PackagePath)!=request.Plan.PackageSha256 || AutomationFiles.Hash(request.Plan.AndroidTests.ProfilePath)!=request.ProfileSha256)
   throw new InvalidDataException("Capture candidate or profile changed.");
  string producer="post-endurance/"+step+"installed-app/AndroidUI/";
  foreach(var pair in request.Pairs) {
   var expected=comparison.Pairs.SingleOrDefault(p=>p.Name==pair.Name);
   if(expected==null || expected.After!=producer+pair.Series || !selected.Contains(pair.TestName,StringComparer.Ordinal) ||
    string.IsNullOrWhiteSpace(pair.Series) || pair.Series.Contains('\\') || pair.Series.Contains(':') ||
    pair.Series.Split('/').Any(p=>p is "" or "." or ".."))throw new InvalidDataException("Capture pairs must map to the original selected producer and test cases.");
  }
  if(selected.Any(n=>!request.Pairs.Any(p=>p.TestName==n)))throw new InvalidDataException("Every selected case must serve a declared evidence gap.");
  var retained=AutomationPostEndurance.RetainedFiles(context).Where(f=>f.RelativePath.StartsWith("post-endurance/"+step,StringComparison.Ordinal) && f.RelativePath.EndsWith("/discovery.dump",StringComparison.Ordinal)).ToArray();
  foreach(string name in selected) {
   var definitions=retained.SelectMany(f=> {
    using var reader=System.Xml.XmlReader.Create(Path.Combine(context.RunDirectory,f.RelativePath),new(){DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20*1024*1024});
    return XDocument.Load(reader).Descendants("test-case").Where(c=>(string?)c.Attribute("fullname")==name).Select(c=>(string?)c.Attribute("runstate")).ToArray();
   }).ToArray();
   if(definitions.Length==0 || definitions.Any(s=>s!="Runnable"))throw new InvalidDataException("Unproven or Explicit physical cases cannot be collected as supplemental performance evidence.");
  }
 }
 internal static async Task<SubmissionWorkflowStepResult> Collect(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings,Request request,
  Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>> run,Func<string,NetworkCredential> credentials,CancellationToken token) {
  using var gate=new FileStream(Path.Combine(context.RunDirectory,"performance-capture.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  Validate(context,settings,request,true);
  VerifyOtherCaptures(context,request.AttemptId);
  if(await WorkflowEvidence.SourceDigestAsync(request.Plan.SourceRoots,token)!=request.SourceSha256)throw new InvalidDataException("Capture source changed after review.");
  string relative=Prefix(request.AttemptId),folder=Path.Combine(context.RunDirectory,relative),bound=Path.Combine(folder,"binding.json");
  if(File.Exists(Path.Combine(folder,"not-started.json")) || File.Exists(Path.Combine(folder,"closed-failed.json")))throw new InvalidDataException("A closed capture must not be replayed. Select a new explicit attempt.");
  var binding=new Binding(context.Checkpoint.InputSha256,request);
  if(Directory.Exists(folder)) {
   if(!SubmissionEvidence.SafeEvidencePath(context.RunDirectory,relative+"binding.json",out _) || !Equal(AutomationFiles.Read<Binding>(bound),binding))
    throw new InvalidDataException("Capture identity changed; inspect the original attempt without replay.");
  } else {
   Directory.CreateDirectory(folder);AutomationFiles.Write(bound,binding);
  }
  var resolved=AutomationPostEndurance.Resolve(context,settings) with{InstalledAppSteps=null,InstalledAppTests=request.Plan};
  // Advance retains its intent before invoking equipment and will never replay an existing invocation.
  var result=await AutomationInstalledApp.Advance(context with{RunDirectory=folder},resolved,false,run,credentials,token);
  if(result.Status!=SubmissionWorkflowStatus.Completed)return result;
  Validate(context,settings,request,true);
  var outcome=AutomationFiles.Read<InstalledDriverTestResult>(Path.Combine(folder,"installed-app/InstalledDriverTests.json"));
  if(!outcome.Passed || !outcome.CandidateVerified || !outcome.ReservationsReleased || outcome.Tests!.Passed!=request.Plan.AndroidTests.RequiredTests!.Count)
   throw new InvalidDataException("Supplemental cases must pass with restoration, cleanup and verified candidate identity.");
  AutomationFiles.Write(Path.Combine(folder,"receipt.json"),new Receipt(context.Checkpoint.InputSha256,
   new(relative+"binding.json",AutomationFiles.Hash(bound)),new(relative+result.Receipt!.RelativePath,result.Receipt.Sha256)));
  return new(SubmissionWorkflowStatus.Completed,new(relative+"receipt.json",AutomationFiles.Hash(Path.Combine(folder,"receipt.json"))));
 }
 internal static void VerifyOtherCaptures(SubmissionWorkflowStepContext context,string? current=null) {
  string folder=Path.Combine(context.RunDirectory,Folder);if(!Directory.Exists(folder))return;
  if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked performance capture folder.");
  var attempts=Directory.GetDirectories(folder);if(attempts.Length>128)throw new InvalidDataException("Too many performance capture attempts.");
  foreach(string attempt in attempts) {
   string id=Path.GetFileName(attempt);AutomationAppStepRecovery.RequireId(id);if(id==current)continue;
   if(File.Exists(Path.Combine(attempt,"not-started.json"))){VerifyUnstarted(context,attempt);continue;}
   if(File.Exists(Path.Combine(attempt,"closed-failed.json"))){VerifyFailed(context,attempt);continue;}
   if(!SubmissionEvidence.SafeEvidencePath(context.RunDirectory,Prefix(id)+"receipt.json",out var path))
    throw new InvalidDataException("Inspect unfinished supplemental evidence and restoration before proceeding.");
   var receipt=AutomationFiles.Read<Receipt>(path);
   if(receipt.InputSha256!=context.Checkpoint.InputSha256 || receipt.Binding.RelativePath!=Prefix(id)+"binding.json" || receipt.Tests.RelativePath!=Prefix(id)+"installed-app-tests.json")
    throw new InvalidDataException("Additional evidence identity changed.");
   Check(context.RunDirectory,receipt.Binding);Check(context.RunDirectory,receipt.Tests);AutomationInstalledApp.VerifyRetained(attempt);
  }
 }
 private sealed record NotStarted(string InputSha256,string AttemptId,string Reason,Dictionary<string,string> OriginalFiles);
 private static Dictionary<string,string> UnstartedFiles(string folder) {
  if(!Directory.Exists(folder) || (File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0 || Directory.GetDirectories(folder).Length!=0)
   throw new InvalidDataException("A producer or readiness directory exists; inspect its execution and restoration instead.");
  var files=Directory.GetFiles(folder).Where(p=>Path.GetFileName(p)!="not-started.json").Order(StringComparer.Ordinal).ToArray();
  if(files.Any(p=>(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) ||
   !files.Select(Path.GetFileName).Where(n=>n!="app-fixture-settings.json").Order(StringComparer.Ordinal).SequenceEqual(new[]{"binding.json","installed-app-intent.json"}))
   throw new InvalidDataException("Only an unchanged binding, fixture settings and intent can be closed before producer execution.");
  return files.ToDictionary(p=>Path.GetFileName(p)!,p=>AutomationFiles.Hash(p),StringComparer.Ordinal);
 }
 internal static void CloseUnstarted(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings,Request request,string reason) {
  using var gate=new FileStream(Path.Combine(context.RunDirectory,"performance-capture.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  Validate(context,settings,request,true);
  if(string.IsNullOrWhiteSpace(reason)||reason.Length>3000)throw new InvalidDataException("Explain the inspected pre-execution failure.");
  string folder=Path.Combine(context.RunDirectory,Prefix(request.AttemptId));
  var binding=AutomationFiles.Read<Binding>(Path.Combine(folder,"binding.json"));
  if(binding.InputSha256!=context.Checkpoint.InputSha256 || !Equal(binding.Request,request))throw new InvalidDataException("Inspected capture binding changed.");
  var files=UnstartedFiles(folder);
  AutomationFiles.Write(Path.Combine(folder,"not-started.json"),new NotStarted(context.Checkpoint.InputSha256,request.AttemptId,reason,files));
 }
 private static void VerifyUnstarted(SubmissionWorkflowStepContext context,string attempt) {
  var record=AutomationFiles.Read<NotStarted>(Path.Combine(attempt,"not-started.json"));
  if(record.InputSha256!=context.Checkpoint.InputSha256 || record.AttemptId!=Path.GetFileName(attempt) || string.IsNullOrWhiteSpace(record.Reason) ||
   !Equal(record.OriginalFiles,UnstartedFiles(attempt)))throw new InvalidDataException("Inspected unstarted capture changed.");
 }
 private sealed record ClosedFailed(string InputSha256,string AttemptId,string Reason,SubmissionWorkflowReceipt[] Files);
 private static SubmissionWorkflowReceipt[] FailedFiles(string folder) {
  if(!Directory.Exists(folder) || (File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0 ||
   File.Exists(Path.Combine(folder,"receipt.json")) || File.Exists(Path.Combine(folder,"not-started.json")))
   throw new InvalidDataException("Only a terminal failed producer can be closed.");
  var files=AutomationInstalledApp.Inventory(folder).Concat(new[]{"binding.json","installed-app-intent.json"}.Select(name=> {
   if(!SubmissionEvidence.SafeEvidencePath(folder,name,out var path))throw new InvalidDataException("Failed capture identity is missing or unsafe.");
   return new SubmissionWorkflowReceipt(name,AutomationFiles.Hash(path));
  })).OrderBy(f=>f.RelativePath,StringComparer.Ordinal).ToArray();
  var result=AutomationFiles.Read<InstalledDriverTestResult>(Path.Combine(folder,"installed-app/InstalledDriverTests.json"));
  if(result.Passed || result.Tests is not {Failed:>0} || !result.RestorationConfirmed || !result.CleanupConfirmed || !result.CandidateVerified || !result.ReservationsReleased)
   throw new InvalidDataException("A failed capture requires confirmed restoration, cleanup, candidate and reservation release before closure.");
  return files;
 }
 internal static void CloseFailed(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings,Request request,string reason) {
  using var gate=new FileStream(Path.Combine(context.RunDirectory,"performance-capture.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  Validate(context,settings,request,true);
  if(string.IsNullOrWhiteSpace(reason)||reason.Length>3000)throw new InvalidDataException("Explain the inspected terminal failure; closure cannot turn it into a pass.");
  string folder=Path.Combine(context.RunDirectory,Prefix(request.AttemptId));
  var binding=AutomationFiles.Read<Binding>(Path.Combine(folder,"binding.json"));
  if(binding.InputSha256!=context.Checkpoint.InputSha256 || !Equal(binding.Request,request))throw new InvalidDataException("Inspected capture binding changed.");
  AutomationFiles.Write(Path.Combine(folder,"closed-failed.json"),new ClosedFailed(context.Checkpoint.InputSha256,request.AttemptId,reason,FailedFiles(folder)));
 }
 private static ClosedFailed VerifyFailed(SubmissionWorkflowStepContext context,string folder) {
  if(!SubmissionEvidence.SafeEvidencePath(context.RunDirectory,Prefix(Path.GetFileName(folder))+"closed-failed.json",out var closed))throw new InvalidDataException("Unsafe failed capture closure.");
  var record=AutomationFiles.Read<ClosedFailed>(closed);
  if(record.InputSha256!=context.Checkpoint.InputSha256 || record.AttemptId!=Path.GetFileName(folder) || string.IsNullOrWhiteSpace(record.Reason) ||
   !record.Files.SequenceEqual(FailedFiles(folder)))throw new InvalidDataException("Closed failed capture or its original evidence changed.");
  return record;
 }
 internal static SubmissionWorkflowReceipt[] FailedRetention(SubmissionWorkflowStepContext context) {
  string folder=Path.Combine(context.RunDirectory,Folder);if(!Directory.Exists(folder))return [];
  var files=new List<SubmissionWorkflowReceipt>();
  foreach(string attempt in Directory.GetDirectories(folder).Order(StringComparer.Ordinal)) {
   if(!File.Exists(Path.Combine(attempt,"closed-failed.json")))continue;
   var record=VerifyFailed(context,attempt);string prefix=Prefix(record.AttemptId);
   files.Add(new(prefix+"closed-failed.json",AutomationFiles.Hash(Path.Combine(attempt,"closed-failed.json"))));
   files.AddRange(record.Files.Select(f=>new SubmissionWorkflowReceipt(prefix+f.RelativePath.Replace('\\','/'),f.Sha256)));
  }
  return files.ToArray();
 }
 internal static Task<int> Command(AutomationRequest request,string path,string hash,CancellationToken token,string? closeReason=null,bool closeFailed=false) {
  if(AutomationFiles.Hash(path)!=hash)throw new InvalidDataException("Capture request changed after review.");
  var plan=AutomationFiles.Read<Request>(path);var s=request.Settings;
  // One checkpoint lock covers validation and invocation. Reacquiring it after durable intent
  // can collide with an ordinary waiting worker and strand an operation before any input.
  int result=SubmissionWorkflow.WithVerifiedCheckpoint(s.PrivateRoot,s.Release,context=>{
   if(AutomationFiles.Hash(Path.Combine(context.RunDirectory,"state.json"))!=plan.StateSha256)throw new InvalidDataException("Inspected performance state changed.");
   Validate(context,s,plan,true);
   if(closeReason!=null){if(closeFailed)CloseFailed(context,s,plan,closeReason);else CloseUnstarted(context,s,plan,closeReason);return 0;}
   if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
   var bound=DevToolsCredentialBindings.Read(AutomationPostEndurance.Resolve(context,s).CredentialBindings).Resolve(DevToolsCredentialPurpose.Processor,plan.Plan.Host);
   SubmissionAutomationStages.VerifyProcessorPins(s,plan.Plan.Host,bound.CertificateSha256,bound.SshFingerprint);
   if(bound.CertificateSha256!=plan.Plan.CertificateSha256 || bound.SshFingerprint!=plan.Plan.SshFingerprint)throw new InvalidDataException("Capture processor binding differs.");
   var captured=Collect(context,s,plan,async(p,c,r,t)=>{
    await AutomationDriverReadiness.Check(p.Host,p.CertificateSha256,c,new(p.Target.DeviceId,p.Target.Model,p.Target.Version,"Existing"),r+"-readiness",t);
    await AutomationAndroidReadiness.Check(p.AndroidTests.ProfilePath,Path.Combine(r+"-readiness","android"),t);
    return await InstalledDriverTests.RunAsync(p,c,r,t);
   },_=>new(bound.UserName,bound.Password),token).GetAwaiter().GetResult();
   Console.WriteLine(JsonSerializer.Serialize(captured,AutomationFiles.Json));return captured.Status==SubmissionWorkflowStatus.Completed?0:3;
  });
  return Task.FromResult(result);
 }
 // Verifies an explicitly cited additional file; the original timing pair is always retained separately.
 internal sealed record Support(SubmissionWorkflowReceipt[] Files,string Series);
 internal static Support SupportingFile(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings,
  string pairName,SubmissionEvidenceFile file) {
  var parts=file.RelativePath.Split('/');
  if(parts.Length<5 || parts[0]!=Folder)throw new InvalidDataException("Invalid additional performance evidence path.");
  string prefix=Prefix(parts[1]);AutomationAppStepRecovery.RequireId(parts[1]);
  Check(context.RunDirectory,new(file.RelativePath,file.Sha256));
  var receipt=AutomationFiles.Read<Receipt>(Path.Combine(context.RunDirectory,prefix+"receipt.json"));
  if(receipt.InputSha256!=context.Checkpoint.InputSha256 || receipt.Binding.RelativePath!=prefix+"binding.json" || receipt.Tests.RelativePath!=prefix+"installed-app-tests.json")
   throw new InvalidDataException("Additional evidence belongs to another run.");
  Check(context.RunDirectory,receipt.Binding);Check(context.RunDirectory,receipt.Tests);
  var binding=AutomationFiles.Read<Binding>(Path.Combine(context.RunDirectory,receipt.Binding.RelativePath));
  if(binding.InputSha256!=context.Checkpoint.InputSha256 || binding.Request.AttemptId!=parts[1])throw new InvalidDataException("Additional evidence binding differs.");
  Validate(context,settings,binding.Request,false);
  string folder=Path.Combine(context.RunDirectory,prefix);AutomationInstalledApp.VerifyRetained(folder);
  var pair=binding.Request.Pairs.SingleOrDefault(p=>p.Name==pairName)??throw new InvalidDataException("Additional evidence does not cover this response pair.");
  string series=prefix+"installed-app/AndroidUI/"+pair.Series;
  string evidencePrefix=series[..(series.LastIndexOf('/')+1)];
  if(file.RelativePath==series || !file.RelativePath.StartsWith(evidencePrefix,StringComparison.Ordinal))throw new InvalidDataException("Additional support must belong to this pair and extend beyond timing measurements.");
  var files=AutomationInstalledApp.Inventory(folder).ToDictionary(f=>prefix+f.RelativePath.Replace('\\','/'),f=>f.Sha256,StringComparer.Ordinal);
  if(!files.TryGetValue(file.RelativePath,out var hash) || hash!=file.Sha256 || !files.TryGetValue(series,out var seriesHash))throw new InvalidDataException("Additional file is outside the retained producer.");
  // The immutable receipt includes the whole producer, including any unfavorable observations.
  return new([new(prefix+"receipt.json",AutomationFiles.Hash(Path.Combine(folder,"receipt.json"))),receipt.Binding,receipt.Tests,new(series,seriesHash),new(file.RelativePath,file.Sha256)],series);
 }
}
