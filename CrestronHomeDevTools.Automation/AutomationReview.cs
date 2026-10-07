// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

public sealed record SubmissionAutomationInput(string Path,string Sha256);
public sealed record SubmissionAutomationConsole(string Directory,SubmissionEvidenceFile[] Files);
/// <summary>Saved review configuration. Observation paths identify outputs of the verified NUnit producer,
/// relative to the run root. The controller never turns test counts into checklist passes.</summary>
public sealed record SubmissionAutomationReviewPlan(SubmissionAutomationInput Policy,SubmissionAutomationInput Template,
 SubmissionAutomationInput Inventory,SubmissionAutomationInput Mapping,SubmissionAutomationConsole Console,
 string Title,string Author,string[] ObservationSources,SubmissionAutomationInput? Declarations=null,
 SubmissionAutomationInput? AndroidPins=null,JsonElement? AndroidEvidence=null,
 SubmissionAutomationPriorEvidence? PriorEvidence=null,SubmissionAutomationApplicability? Applicability=null,
 SubmissionAutomationQualifications? Qualifications=null,
 SubmissionAutomationInput? SourceApplicability=null,
 SubmissionGapDeclaration[]? PlannedGaps=null)
{
 /// <summary>Optional initial producer observations when final review selects post-endurance observations.
 /// These must cover every prerequisite in the full policy; this is not a list of requirements to waive.</summary>
 public string[]? PreEnduranceObservationSources { get; init; }
}

internal static class AutomationReview
{
 internal static readonly JsonSerializerOptions DocumentJson=new(AutomationFiles.Json){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
 internal sealed record Prepared(string SettingsPath,string CandidateSha256,string InventorySha256,string MappingSha256,
  string? DeclarationsPath,string? DeclarationsSha256,string? AndroidPinsPath,string? AndroidPinsSha256);
 internal sealed record Receipt(string InputSha256,string ReviewSha256,SubmissionWorkflowReceipt[] Files);
 private sealed record EnduranceEnvelope(string EvidenceDirectory,SubmissionObservation Observation);
 internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,
  bool recover,CancellationToken token,Func<SubmissionAutomationConsole,string[],string,CancellationToken,Task<int>>? execute=null)
 {
  settings=AutomationReviewTooling.Resolve(c,settings);
  if(settings.Review is not {} plan)return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"review-plan-required");
  var published=await AutomationReviewInputs.Seal(c,settings,token,execute);
  if(published.Status!=SubmissionWorkflowStatus.Completed)return published;
  var frozen=AutomationReviewInputs.Verify(c);
  var inputs=frozen.Prepared;
  string output=Path.Combine(c.RunDirectory,"review"),intent=Path.Combine(c.RunDirectory,"review-intent.json");
  if(recover && File.Exists(intent)) {
   if(!File.Exists(Path.Combine(output,"COMPLETE")))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-review-preparation");
   using var saved=JsonDocument.Parse(File.ReadAllBytes(intent));
   if(saved.RootElement.GetProperty("OperationId").GetString()!=c.Checkpoint.OperationId ||
    saved.RootElement.GetProperty("InputSha256").GetString()!=c.Checkpoint.InputSha256)
    throw new InvalidDataException("Review operation identity changed.");
   if(saved.RootElement.GetProperty("InputsSha256").GetString()!=frozen.InputsSha256 ||
    saved.RootElement.GetProperty("ReviewInputsReceiptSha256").GetString()!=AutomationFiles.Hash(Path.Combine(c.RunDirectory,AutomationReviewInputs.ReceiptName)))
    throw new InvalidDataException("Review snapshot changed.");
   return Check(c,inputs,token);
  }
  if(Directory.Exists(output))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-existing-review");
  AutomationFiles.Write(intent,new{c.Checkpoint.OperationId,c.Checkpoint.InputSha256,frozen.InputsSha256,
   ReviewInputsReceiptSha256=AutomationFiles.Hash(Path.Combine(c.RunDirectory,AutomationReviewInputs.ReceiptName))});
  var arguments=new List<string>{"submission","prepare-frozen-review","--inputs",Path.Combine(c.RunDirectory,AutomationReviewInputs.Folder),
   "--inputs-sha256",frozen.InputsSha256,"--output",output,"--prepare-for-signing"};
  int result=await (execute??AutomationConsole.Run)(plan.Console,arguments.ToArray(),Path.Combine(c.RunDirectory,"review-process"),token);
  if(result!=0)return new(SubmissionWorkflowStatus.Failed,ReasonCode:"review-preparation-failed");
  return Check(c,inputs,token);
 }

 internal static Prepared PrepareInputs(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,SubmissionAutomationReviewPlan plan,CancellationToken token)
 {
  var accepted=AutomationTestAssessment.ReadForReview(c,settings with{Review=plan});
  ValidatePlannedGaps(plan);
  string root=c.RunDirectory,folder=Path.Combine(root,"review-inputs");Directory.CreateDirectory(folder);
  string Copy(SubmissionAutomationInput input,string name) {
   if(!Path.IsPathFullyQualified(input.Path) || AutomationFiles.Hash(input.Path)!=input.Sha256)throw new InvalidDataException("Reviewed document input changed.");
   string destination=Path.Combine(folder,name);WriteBytes(destination,File.ReadAllBytes(input.Path),input.Sha256);return destination;
  }
  string policy=Copy(plan.Policy,"policy.json"),template=Copy(plan.Template,"template.pdf"),inventory=Copy(plan.Inventory,"inventory.json"),mapping=Copy(plan.Mapping,"mapping.json");
  var identity=new SubmissionEvidenceIdentity(settings.Release.PackageSha256,settings.Release.SourceCommit,plan.Policy.Sha256,plan.Template.Sha256);
  WriteDocument(Path.Combine(folder,"candidate.json"),new SubmissionCandidate(1,identity,settings.PackageRequirements));
  using var release=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"release.json")));
  string name=release.RootElement.GetProperty("PackageName").GetString()??throw new InvalidDataException("Missing published package name.");
  if(Path.GetFileName(name)!=name || name.Contains('/') || name.Contains('\\'))throw new InvalidDataException("Unsafe package name.");
  WriteBytes(Path.Combine(folder,name),File.ReadAllBytes(Path.Combine(root,"candidate.pkg")),settings.Release.PackageSha256);

  // Only package the assessment already retained by phase two. No test collection or new decisions here.
  WriteDocument(Path.Combine(folder,"observations.json"),accepted);
  string observations=Path.Combine(folder,"observations.json");
  string? declarations=plan.Declarations==null?null:Copy(plan.Declarations,"declarations.json");
  if(plan.PlannedGaps is {} gaps) {
   declarations=Path.Combine(folder,"declarations.json");
   WriteDocument(declarations,new SubmissionGapDeclarations(1,identity,SubmissionReviewMode.DeclaredGaps,gaps));
  }
  string? android=plan.AndroidPins==null?null:Copy(plan.AndroidPins,"android-pins.json");
  JsonElement? androidEvidence=plan.AndroidEvidence;
  if((android==null)!=(androidEvidence==null))throw new InvalidDataException("Explicit Android review bindings require both pins and evidence locations.");
  if(android==null && (settings.NUnit.AndroidTests!=null || settings.InstalledAppTests!=null)) {
   var binding=AutomationAndroidReview.Bind(c,settings.Release,settings.InstalledAppTests!=null,Path.Combine(folder,"candidate.json"),token);
   android=binding.PinsPath;androidEvidence=binding.Evidence;
  }
  var values=new Dictionary<string,object>{["schemaVersion"]=1,["candidate"]=Path.Combine(folder,"candidate.json"),["policy"]=policy,
   ["template"]=template,["inventory"]=inventory,["mapping"]=mapping,["observations"]=observations,["package"]=Path.Combine(folder,name),
   ["evidence"]=root,["output"]=Path.Combine(root,"review"),["title"]=plan.Title,["author"]=plan.Author};
  if(androidEvidence is {} locations)values.Add("androidEvidence",locations);
  string settingsPath=Path.Combine(folder,"settings.json");WriteDocument(settingsPath,values);
  var prepared=new Prepared(settingsPath,AutomationFiles.Hash(Path.Combine(folder,"candidate.json")),plan.Inventory.Sha256,plan.Mapping.Sha256,
   declarations,declarations==null?null:AutomationFiles.Hash(declarations),android,android==null?null:AutomationFiles.Hash(android));
  AutomationFiles.Write(Path.Combine(folder,"prepared.json"),prepared);return prepared;
 }
 internal static void ValidatePlannedGaps(SubmissionAutomationReviewPlan plan) {
  if(plan.PlannedGaps is not {} gaps)return;
  if(plan.Declarations!=null || gaps.Length is <1 or >512 || gaps.Any(g=>g==null ||
   string.IsNullOrWhiteSpace(g.RequirementId) || string.IsNullOrWhiteSpace(g.Reason) || g.InterpretationReview!=null) ||
   gaps.Select(g=>g.RequirementId).Distinct(StringComparer.Ordinal).Count()!=gaps.Length)
   throw new InvalidDataException("Planned gaps require distinct scoped reasons, no interpretation review and no second declaration source.");
  if(AutomationFiles.Hash(plan.Policy.Path)!=plan.Policy.Sha256)throw new InvalidDataException("Planned-gap policy changed.");
  var policy=AutomationFiles.Read<SubmissionEvidencePolicy>(plan.Policy.Path);
  if(policy.SchemaVersion!=1 || gaps.Any(g=>!policy.Requirements.Any(r=>r.Id==g.RequirementId)))
   throw new InvalidDataException("Every planned gap must belong to the reviewed policy.");
 }
 internal static string ResolveObservationSource(string requested,IReadOnlyDictionary<string,string> retained,string? root=null) {
  const string suffix="/installed-app/AndroidUI/";
  int stepIndex=requested.LastIndexOf(suffix,StringComparison.Ordinal);
  if(root!=null && stepIndex>=0) {
   string stepRoot=requested[..stepIndex];
   string pointer=stepRoot+"/installed-app/replacement.json";
   if(retained.TryGetValue(pointer,out var pin)) {
    if(!SubmissionEvidence.SafeEvidencePath(root,pointer,out var path) || AutomationFiles.Hash(path)!=pin)throw new InvalidDataException("Replacement lineage changed.");
    var accepted=AutomationFiles.Read<AutomationAppStepRecovery.Completion>(path);
    AutomationAppStepRecovery.RequireId(accepted.AttemptId);
    string prefix="installed-app/recovery-attempts/"+accepted.AttemptId+"/";
    _=AutomationAppScopeRevision.Accepted(accepted);
    if(!retained.ContainsKey(stepRoot+"/"+prefix+"attempt.json") || !retained.ContainsKey(stepRoot+"/"+prefix+"original-evidence.json"))throw new InvalidDataException("Missing replacement provenance.");
    if(accepted.CaseRecovery is {} caseProof && !retained.ContainsKey(stepRoot+"/"+caseProof.ReceiptPath))throw new InvalidDataException("Missing case recovery receipt.");
    string requestedSuffix=requested[(stepIndex+suffix.Length)..];
    string resolved;
    if(accepted.ObservationSources is {} mappings) {
     if(!mappings.TryGetValue(requestedSuffix,out var revised))throw new InvalidDataException("Revised observation mapping is missing.");
     resolved=stepRoot+"/"+revised;
    } else resolved=stepRoot+"/"+accepted.ProducerPrefix+requestedSuffix;
    if(!retained.ContainsKey(resolved))throw new InvalidDataException("Replacement observation is missing.");
    return resolved;
   }
  }
  if(retained.ContainsKey(requested))return requested;
  const string marker="/installed-app/AndroidUI/";
  int index=requested.LastIndexOf(marker,StringComparison.Ordinal);
  if(index<0)return requested;
  string step=requested[..index];
  string repair=step+"/installed-app/preparation-recovery/";
  string replacement=repair+"installed-app/AndroidUI/"+requested[(index+marker.Length)..];
  // Only a completed coordinator inventory can authorise this explicit repair lineage.
  return retained.ContainsKey(repair+"repair.json") && retained.ContainsKey(repair+"original-evidence.json") &&
   retained.ContainsKey(replacement)?replacement:requested;
 }
 internal static string? ObservationBase(string relative) {
  const string marker="/installed-app/AndroidUI/";
  int index=relative.LastIndexOf(marker,StringComparison.Ordinal);
  if(index>=0)return relative[..index];
  return new[]{AutomationInitialAdditionalTests.DirectoryName,AutomationPostEndurance.DirectoryName}
   .SingleOrDefault(name=>relative.StartsWith(name+"/",StringComparison.Ordinal));
 }
 internal static SubmissionObservation Rebase(SubmissionObservation o,string root) {
  string P(string path)=>root+"/"+path;
  var e=o.Execution;
  return o with { Files=o.Files.Select(f=>f with{RelativePath=P(f.RelativePath)}).ToArray(),Execution=e==null?null:e with {
   Response=e.Response==null?null:e.Response with{TriggerEvidence=P(e.Response.TriggerEvidence),ResponseEvidence=P(e.Response.ResponseEvidence)},
   Restoration=e.Restoration==null?null:e.Restoration with{OriginalEvidence=P(e.Restoration.OriginalEvidence),VerificationEvidence=P(e.Restoration.VerificationEvidence)},
   Samples=e.Samples?.Select(s=>s with{Evidence=P(s.Evidence)}).ToArray() } };
 }
 internal static SubmissionWorkflowStepResult Check(SubmissionWorkflowStepContext c,Prepared inputs,CancellationToken token) {
  string folder=Path.Combine(c.RunDirectory,"review"),path=Path.Combine(folder,"review-receipt.json");
  string hash=AutomationFiles.Hash(path);
  if(File.ReadAllText(Path.Combine(folder,"COMPLETE")).Trim()!=hash)throw new InvalidDataException("Incomplete review output.");
  using var receipt=JsonDocument.Parse(File.ReadAllBytes(path));var r=receipt.RootElement;
  void Match(string field,string expected) {if(r.GetProperty(field).GetString()!=expected)throw new InvalidDataException("Review receipt differs from the prepared operation.");}
  Match("candidateSha256",inputs.CandidateSha256);Match("sourceCommit",c.Checkpoint.Release.SourceCommit);
  Match("inventorySha256",inputs.InventorySha256);Match("mappingSha256",inputs.MappingSha256);
  Match("formSha256",AutomationFiles.Hash(Path.Combine(folder,"self-test.review.pdf")));
  Match("formReportSha256",AutomationFiles.Hash(Path.Combine(folder,"form-report.json")));
  if(!r.GetProperty("signingCopy").GetBoolean() || r.GetProperty("submissionReady").GetBoolean() || r.GetProperty("deliveryAttempted").GetBoolean())
   throw new InvalidDataException("Review is not an unsigned signing copy.");
  string bundle=Path.Combine(folder,"evidence.zip"),digest=r.GetProperty("bundleSha256").GetString()!;
  SubmissionValidationReport validation;
  if(inputs.DeclarationsSha256 is {} declaration) {
   Match("state","UnsignedReviewWithDeclaredGapsPrepared");Match("declarationsSha256",declaration);
   var checkedBundle=SubmissionBundle.CheckReview(bundle,digest,inputs.CandidateSha256,c.RunDirectory,declaration,SubmissionReviewMode.DeclaredGaps,DateTimeOffset.UtcNow,token);
   if(!checkedBundle.ReadyForReview)throw new InvalidDataException("Declared-gap review bundle failed revalidation.");validation=checkedBundle.Review.Validation;
  } else {
   Match("state","UnsignedReviewPrepared");var checkedBundle=SubmissionBundle.Check(bundle,digest,inputs.CandidateSha256,c.RunDirectory,DateTimeOffset.UtcNow,token);
   if(!checkedBundle.ValidationChecksPassed)throw new InvalidDataException("Review bundle failed revalidation.");validation=checkedBundle.Validation;
  }
  if(validation.Package?.Sha256!=c.Checkpoint.Release.PackageSha256)throw new InvalidDataException("Review contains another package.");
  var files=Directory.GetFiles(folder,"*",SearchOption.AllDirectories)
   .Concat(File.Exists(Path.Combine(c.RunDirectory,AutomationReviewTooling.FileName))?[Path.Combine(c.RunDirectory,AutomationReviewTooling.FileName)]:Array.Empty<string>()).Order(StringComparer.Ordinal)
   .Select(p=>new SubmissionWorkflowReceipt(Path.GetRelativePath(c.RunDirectory,p),AutomationFiles.Hash(p))).ToArray();
  return AutomationFiles.Complete(c,"review-evidence.json",new Receipt(c.Checkpoint.InputSha256,hash,files));
 }
 internal static void VerifyRetained(string root) {
  foreach(var file in AutomationFiles.Read<Receipt>(Path.Combine(root,"review-evidence.json")).Files)
   if(!SubmissionEvidence.SafeEvidencePath(root,file.RelativePath.Replace('\\','/'),out var path) || AutomationFiles.Hash(path)!=file.Sha256)
    throw new InvalidDataException("Retained review packet changed.");
 }
 internal static void WriteDocument<T>(string path,T value)=>WriteBytes(path,JsonSerializer.SerializeToUtf8Bytes(value,DocumentJson));
 internal static void WriteBytes(string path,byte[] bytes,string? expected=null) {
  string digest=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
  if(expected!=null && digest!=expected)throw new InvalidDataException("Review input changed while copying.");
  if(File.Exists(path)) {if(AutomationFiles.Hash(path)!=digest)throw new InvalidDataException("Retained review input changed.");return;}
  using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);file.Write(bytes);file.Flush(true);
 }
}
