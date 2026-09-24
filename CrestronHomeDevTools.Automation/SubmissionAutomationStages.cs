// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

/// <summary>Production candidate/NUnit/endurance bindings. Missing later-stage bindings stop explicitly.
/// Run on the configured Windows worker, never under an identity with unrelated signing/mail secrets.</summary>
public sealed class SubmissionAutomationStages : ISubmissionWorkflowSteps
{
 private readonly SubmissionAutomationSettings settings;
 private readonly Func<WorkflowPlan,NetworkCredential,string,CancellationToken,Task<ProcessorWorkflowResult>> runNUnit;
 private readonly Func<string,NetworkCredential> credential;
 private readonly string settingsDigest;
 private readonly SubmissionAutomationWorkerRole role;
 private readonly Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>> runInstalledApp;
 private string? protectedDigest;
 /// <summary>Create a protected adapter only from an independently pinned installed configuration,
 /// outside build-writable run storage. Per-run settings cannot choose its tools or authority.</summary>
 public static SubmissionAutomationStages CreateProtected(SubmissionAutomationSettings settings,string settingsSha256,string installedPath,string installedSha256)=>
  new(settings,settingsSha256,SubmissionAutomationWorkerRole.Protected,AutomationProtectedWorker.Load(installedPath,installedSha256));
 public SubmissionAutomationStages(SubmissionAutomationSettings settings,string settingsSha256,SubmissionAutomationWorkerRole role=SubmissionAutomationWorkerRole.Evidence)
  :this(settings,settingsSha256,(p,c,r,t)=>WorkflowRunner.RunAsync(p,c,r,token:t),host=> {
   if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
   var saved=DevToolsCredentialBindings.Read(settings.CredentialBindings).Resolve(DevToolsCredentialPurpose.Processor,host);
   if(saved.CertificateSha256!=settings.NUnit.CertificateSha256 || saved.SshFingerprint!=settings.NUnit.SshFingerprint)
    throw new InvalidDataException("The saved processor trust pins differ from the reviewed plan.");
   return new(saved.UserName,saved.Password);
  },role) { }
 internal SubmissionAutomationStages(SubmissionAutomationSettings settings,string digest,SubmissionAutomationWorkerRole role,AutomationProtectedWorker? installed)
  :this(role==SubmissionAutomationWorkerRole.Protected?(installed??throw new InvalidDataException("Protected role requires its independently pinned installed configuration.")).Bind(settings):settings,digest,role)
  {protectedDigest=installed?.Sha256;}
 internal SubmissionAutomationStages(SubmissionAutomationSettings settings,string digest,
  Func<WorkflowPlan,NetworkCredential,string,CancellationToken,Task<ProcessorWorkflowResult>> run,
  Func<string,NetworkCredential> credentials,SubmissionAutomationWorkerRole role=SubmissionAutomationWorkerRole.Evidence,
  Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>>? installedApp=null) {
   this.settings=settings;settingsDigest=digest;runNUnit=run;credential=credentials;this.role=role;
   runInstalledApp=installedApp??((p,c,r,t)=>InstalledDriverTests.RunAsync(p,c,r,t));
  }

 public Task<SubmissionWorkflowStepResult> ExecuteAsync(SubmissionWorkflowStepContext c,CancellationToken t)=>Advance(c,false,t);
 public Task<SubmissionWorkflowStepResult> RecoverAsync(SubmissionWorkflowStepContext c,CancellationToken t)=>Advance(c,true,t);
 private async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext c,bool recover,CancellationToken token)
 {
  if(settings.SchemaVersion!=1 || !Enum.IsDefined(settings.Mode) || c.Checkpoint.Release!=settings.Release || settingsDigest.Length!=64 || !settingsDigest.All(char.IsAsciiHexDigit))
   throw new InvalidDataException("Automation settings do not match the workflow.");
  if(role==SubmissionAutomationWorkerRole.Protected) {
   if(protectedDigest==null)throw new InvalidDataException("Protected role requires its independently pinned installed configuration.");
   AutomationFiles.Write(Path.Combine(c.RunDirectory,"protected-worker-binding.json"),new{Sha256=protectedDigest});
  }
  AutomationFiles.Write(Path.Combine(c.RunDirectory,"automation-binding.json"),new { SettingsSha256=settingsDigest, c.Checkpoint.InputSha256 });
  if(c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.WindowsTests)) VerifyRetainedNUnit(c.RunDirectory);
  if(settings.InstalledAppTests!=null && c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.AppTests))
   AutomationInstalledApp.VerifyRetained(c.RunDirectory);
  if(c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.PrepareReview)) AutomationReview.VerifyRetained(c.RunDirectory);
  if(c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.SignReview)) AutomationSigning.VerifyRetained(c.RunDirectory);
  if(c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.Endurance)) {
   var plan=settings.Endurance?.Plan??throw new InvalidDataException("Completed endurance plan is missing.");
   var observation=SubmissionEndurance.Export(Path.Combine(c.RunDirectory,"endurance","observations"),plan,DateTimeOffset.UtcNow);
   AutomationFiles.Write(Path.Combine(c.RunDirectory,"endurance-evidence.json"),new{EvidenceDirectory="endurance/observations",Observation=observation});
  }
  // Enforced inside both execute and recovery, before any signing/provider adapter is selected.
  if(settings.Mode==SubmissionAutomationMode.Rehearsal && c.Checkpoint.Stage>=SubmissionWorkflowStage.SignReview)
   return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"rehearsal-ready-for-review");
  if(!Enum.IsDefined(role))throw new InvalidDataException("Unknown worker role.");
  if((c.Checkpoint.Stage>=SubmissionWorkflowStage.SignReview)!=(role==SubmissionAutomationWorkerRole.Protected))
   return new(SubmissionWorkflowStatus.Waiting,ReasonCode:"worker-role-handoff");
  switch(c.Checkpoint.Stage)
  {
   case SubmissionWorkflowStage.ValidateCandidate: return await Candidate(c,token);
   case SubmissionWorkflowStage.WindowsTests: return await NUnit(c,recover,token);
   case SubmissionWorkflowStage.ProcessorTests: return VerifyNUnit(c,"Processor");
   case SubmissionWorkflowStage.AppTests:
    return settings.InstalledAppTests==null ? VerifyApp(c) : await AutomationInstalledApp.Advance(c,settings,recover,runInstalledApp,credential,token);
   case SubmissionWorkflowStage.Endurance: return await Endurance(c,recover,token);
   case SubmissionWorkflowStage.PrepareReview: return await AutomationReview.Advance(c,settings,recover,token);
   case SubmissionWorkflowStage.SignReview: return await AutomationSigning.Advance(c,settings,recover,token);
   case SubmissionWorkflowStage.Deliver: return await AutomationDelivery.Advance(c,settings,recover,token);
   case SubmissionWorkflowStage.Retain: return AutomationDelivery.Retain(c);
   default: return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"review-delivery-binding-required");
  }
 }
 private async Task<SubmissionWorkflowStepResult> Candidate(SubmissionWorkflowStepContext c,CancellationToken token)
 {
  AutomationEndurance.ValidateReservation(settings.Endurance);
  // Intake's persisted receipt uses the API's numeric enum contract, unlike CLI settings.
  var inspection=JsonSerializer.Deserialize<SubmissionReleaseInspection>(File.ReadAllBytes(Path.Combine(c.RunDirectory,"release.json")))
   ?? throw new InvalidDataException("Missing release receipt.");
  if(inspection.SourceCommit!=settings.Release.SourceCommit || inspection.PackageSha256!=settings.Release.PackageSha256 ||
   inspection.ReleaseId!=settings.Release.ReleaseId || inspection.Repository!=settings.Release.Repository || inspection.Tag!=settings.Release.Tag || inspection.PackageName==null)
   throw new InvalidDataException("Release receipt differs from the workflow identity.");
  using var stream=File.OpenRead(Path.Combine(c.RunDirectory,"candidate.pkg"));
  var report=SubmissionPackage.Inspect(stream,inspection.PackageName,settings.PackageRequirements);
  AutomationFiles.Write(Path.Combine(c.RunDirectory,"candidate-report.json"),report);
  if(report.Sha256!=settings.Release.PackageSha256 || !report.PackageChecksPassed)
   return new(SubmissionWorkflowStatus.Failed,ReasonCode:"candidate-package-check-failed");
  await VerifySource(token);
  settings.NUnit.Validate();
  if(settings.InstalledAppTests!=null) AutomationInstalledApp.Validate(settings);
  if(settings.Review is {PriorEvidence:not null} review)
   _=AutomationPriorEvidence.Prepare(c.RunDirectory,new(settings.Release.PackageSha256,settings.Release.SourceCommit,
    review.Policy.Sha256,review.Template.Sha256),review,token);
  if(settings.Review is {Applicability:not null} applicability)
   _=AutomationApplicability.Prepare(c.RunDirectory,new(settings.Release.PackageSha256,settings.Release.SourceCommit,
    applicability.Policy.Sha256,applicability.Template.Sha256),applicability,token);
  if(settings.Review is {Qualifications:not null} qualifications)
   _=AutomationQualifications.Prepare(c.RunDirectory,new(settings.Release.PackageSha256,settings.Release.SourceCommit,
    qualifications.Policy.Sha256,qualifications.Template.Sha256),qualifications,token);
  if(!settings.NUnit.SourceRoots.Any(p=>Path.GetFullPath(p).Equals(Path.GetFullPath(settings.SourceRepository),StringComparison.OrdinalIgnoreCase)) ||
   !settings.NUnit.RemoveTestInstanceAfterRun || !settings.NUnit.RemoveTestPackageAfterSuccessfulRun)
   throw new InvalidDataException("Declare the candidate source root and owned test cleanup in the NUnit plan.");
  if(settings.NUnit.ActualDriver!=null && (settings.NUnit.ReleaseCandidate?.Sha256!=settings.Release.PackageSha256 ||
   settings.NUnit.ReleaseCandidate.SourceCommit!=settings.Release.SourceCommit ||
   AutomationFiles.Hash(settings.NUnit.ActualDriver.PackagePath)!=settings.Release.PackageSha256))
   throw new InvalidDataException("Actual-driver tests must use the frozen release candidate.");
  return AutomationFiles.Complete(c,"candidate-validation.json",report);
 }
 private async Task VerifySource(CancellationToken token, string? expectedSourceDigest = null)
 {
  async Task<string> Git(params string[] args) {
   var start=new ProcessStartInfo("git"){WorkingDirectory=settings.SourceRepository,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   foreach(var arg in args)start.ArgumentList.Add(arg);
   using var process=Process.Start(start)??throw new IOException("Could not inspect source.");
   var output=process.StandardOutput.ReadToEndAsync(token);var error=process.StandardError.ReadToEndAsync(token);
   await process.WaitForExitAsync(token);_ = await error;
   if(process.ExitCode!=0)throw new InvalidDataException("Source identity could not be verified.");return (await output).Trim();
  }
  if(await Git("rev-parse","HEAD")!=settings.Release.SourceCommit ||
   (expectedSourceDigest == null
    ? (await Git("status","--porcelain=v1","--untracked-files=normal","--ignore-submodules=none")).Length!=0
    : await WorkflowEvidence.SourceDigestAsync(settings.NUnit.SourceRoots,token)!=expectedSourceDigest))
   throw new InvalidDataException("Tests require the clean frozen source revision.");
 }
 private async Task<SubmissionWorkflowStepResult> NUnit(SubmissionWorkflowStepContext c,bool recover,CancellationToken token)
 {
  string folder=Path.Combine(c.RunDirectory,"nunit");string intent=Path.Combine(c.RunDirectory,"nunit-intent.json");
  if(recover && File.Exists(intent)) {
   using var recorded=JsonDocument.Parse(File.ReadAllBytes(intent));
   if(recorded.RootElement.GetProperty("OperationId").GetString()!=c.Checkpoint.OperationId ||
    recorded.RootElement.GetProperty("InputSha256").GetString()!=c.Checkpoint.InputSha256)
    throw new InvalidDataException("NUnit operation identity changed.");
   if(!File.Exists(Path.Combine(folder,"Workflow.json")) || new FileInfo(Path.Combine(folder,"Workflow.json")).Length==0)
    return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-nunit-operation-and-lease");
   await VerifySource(token,recorded.RootElement.GetProperty("SourceDigest").GetString()
    ?? throw new InvalidDataException("NUnit source identity is missing."));
   return VerifyNUnit(c,"Local");
  }
  await VerifySource(token);
  var sourceDigest=await WorkflowEvidence.SourceDigestAsync(settings.NUnit.SourceRoots,token);
  AutomationFiles.Write(intent,new { c.Checkpoint.OperationId,c.Checkpoint.InputSha256,SourceDigest=sourceDigest });
  // The public NUnit runner owns Windows + processor sequencing, the processor lease and cleanup.
  // Never invoke it a second time after an interrupted recorded attempt.
  var result=await runNUnit(settings.NUnit,credential(settings.NUnit.Host),folder,token);
  if(!result.Passed) return new(SubmissionWorkflowStatus.Failed,ReasonCode:"nunit-workflow-failed");
  // Public NUnit permits the generated Debug revision/date while checking all other source bytes.
  await VerifySource(token,sourceDigest);
  return VerifyNUnit(c,"Local");
 }
 private SubmissionWorkflowStepResult VerifyApp(SubmissionWorkflowStepContext c)
 {
  if(settings.NUnit.AndroidTests==null || settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null)
   return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"app-evidence-binding-required");
  // The public workflow includes exact Android discovery, restoration and owned-child cleanup in this case.
  using var reader=XmlReader.Create(Path.Combine(c.RunDirectory,"nunit","InstalledDriver.xml"),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
  var cases=XDocument.Load(reader).Descendants("test-case").Where(x=>(string?)x.Attribute("fullname")=="Android.Workflow").ToArray();
  if(cases.Length!=1 || (string?)cases[0].Attribute("result")!="Passed")
   return new(SubmissionWorkflowStatus.Failed,ReasonCode:"app-tests-or-restoration-incomplete");
  return VerifyNUnit(c,"Deployed driver live","app-tests.json");
 }
 private SubmissionWorkflowStepResult VerifyNUnit(SubmissionWorkflowStepContext c,string requiredStage,string? receiptName=null)
 {
  var folder=Path.Combine(c.RunDirectory,"nunit");
  // Public runner results include computed properties; read its schema with its normal serializer contract.
  var json=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
  var result=JsonSerializer.Deserialize<ProcessorWorkflowResult>(File.ReadAllBytes(Path.Combine(folder,"Workflow.json")),json)
   ??throw new InvalidDataException("Missing NUnit results.");
  using var lease=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"Lease.json")));
  if(!result.Passed || result.Stages.Count(s=>s.Stage==requiredStage && s.Tests?.MeetsGate==true)!=1 ||
   !result.Stages.Any(s=>s.Stage=="Remove test instance" && s.Outcome=="Passed") ||
   !result.Stages.Any(s=>s.Stage=="Remove temporary test package" && s.Outcome=="Passed") ||
   lease.RootElement.GetProperty("State").GetString()!="Released")
   return new(SubmissionWorkflowStatus.Failed,ReasonCode:"nunit-results-or-cleanup-incomplete");
  var files=Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
   .Select(p=>new SubmissionWorkflowReceipt(Path.GetRelativePath(c.RunDirectory,p),AutomationFiles.Hash(p))).ToArray();
  return AutomationFiles.Complete(c,receiptName??(requiredStage=="Local"?"windows-tests.json":"processor-tests.json"),
   new NUnitReceipt(c.Checkpoint.InputSha256,requiredStage,files));
 }
 private sealed record NUnitReceipt(string InputSha256,string Stage,SubmissionWorkflowReceipt[] Files);
 private static void VerifyRetainedNUnit(string root)
 {
  var receipt=AutomationFiles.Read<NUnitReceipt>(Path.Combine(root,"windows-tests.json"));
  foreach(var file in receipt.Files) {
   string path=Path.GetFullPath(file.RelativePath,root);
   if(Path.IsPathRooted(file.RelativePath) || Path.GetRelativePath(root,path).Split(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Contains("..") ||
    AutomationFiles.Hash(path)!=file.Sha256)throw new InvalidDataException("Completed NUnit evidence changed.");
  }
 }
 private async Task<SubmissionWorkflowStepResult> Endurance(SubmissionWorkflowStepContext c,bool recover,CancellationToken token)
 {
  var worker=settings.Endurance;
  if(worker==null)return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"endurance-plan-required");
  SubmissionEnduranceProcessProbe.Validate(worker.Probe,worker.Plan);
  if(worker.Plan.Identity.PackageSha256!=settings.Release.PackageSha256 || worker.Plan.Identity.SourceCommit!=settings.Release.SourceCommit ||
   worker.Processor.Host!=settings.NUnit.Host || worker.Processor.SshFingerprint!=settings.NUnit.SshFingerprint)
   throw new InvalidDataException("Endurance plan targets another package.");
  string directory=Path.Combine(c.RunDirectory,"endurance");
  return await AutomationEndurance.Advance(c,recover,new AutomationEndurance(directory,worker,credential(worker.Processor.Host)),token);
 }
}
