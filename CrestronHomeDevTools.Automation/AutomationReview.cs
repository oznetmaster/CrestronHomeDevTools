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
 SubmissionGapDeclaration[]? PlannedGaps=null);

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
  if(settings.Review is not {} plan)return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"review-plan-required");
  string output=Path.Combine(c.RunDirectory,"review"),intent=Path.Combine(c.RunDirectory,"review-intent.json");
  if(recover && File.Exists(intent)) {
   if(!File.Exists(Path.Combine(output,"COMPLETE")))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-review-preparation");
   using var saved=JsonDocument.Parse(File.ReadAllBytes(intent));
   if(saved.RootElement.GetProperty("OperationId").GetString()!=c.Checkpoint.OperationId ||
    saved.RootElement.GetProperty("InputSha256").GetString()!=c.Checkpoint.InputSha256)
    throw new InvalidDataException("Review operation identity changed.");
   string preparedPath=Path.Combine(c.RunDirectory,"review-inputs","prepared.json");
   if(AutomationFiles.Hash(preparedPath)!=saved.RootElement.GetProperty("PreparedSha256").GetString())throw new InvalidDataException("Prepared operation changed.");
   var prepared=AutomationFiles.Read<Prepared>(preparedPath);
   if(AutomationFiles.Hash(prepared.SettingsPath)!=saved.RootElement.GetProperty("SettingsSha256").GetString())throw new InvalidDataException("Review settings changed.");
   return Check(c,prepared,token);
  }
  if(Directory.Exists(output))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-existing-review");
  var inputs=PrepareInputs(c,settings,plan,token);
  AutomationFiles.Write(intent,new{c.Checkpoint.OperationId,c.Checkpoint.InputSha256,SettingsSha256=AutomationFiles.Hash(inputs.SettingsPath),
   PreparedSha256=AutomationFiles.Hash(Path.Combine(c.RunDirectory,"review-inputs","prepared.json"))});
  var arguments=new List<string>{"submission","prepare-review","--settings",inputs.SettingsPath,
   "--candidate-sha256",inputs.CandidateSha256,"--inventory-sha256",inputs.InventorySha256,"--mapping-sha256",inputs.MappingSha256,
   "--source-commit",settings.Release.SourceCommit,"--artifact-kind","driver","--prepare-for-signing"};
  if(inputs.DeclarationsPath!=null)arguments.AddRange(["--review-mode","declared-gaps","--declarations",inputs.DeclarationsPath,"--declarations-sha256",inputs.DeclarationsSha256!]);
  if(inputs.AndroidPinsPath!=null)arguments.AddRange(["--android-pins",inputs.AndroidPinsPath,"--android-pins-sha256",inputs.AndroidPinsSha256!]);
  int result=await (execute??AutomationConsole.Run)(plan.Console,arguments.ToArray(),Path.Combine(c.RunDirectory,"review-process"),token);
  if(result!=0)return new(SubmissionWorkflowStatus.Failed,ReasonCode:"review-preparation-failed");
  return Check(c,inputs,token);
 }

 internal static Prepared PrepareInputs(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,SubmissionAutomationReviewPlan plan,CancellationToken token)
 {
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

  // Sources must have been retained by a completed test producer, not dropped into the directory later.
  using var producer=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"windows-tests.json")));
  var retained=producer.RootElement.GetProperty("Files").EnumerateArray().ToDictionary(x=>x.GetProperty("RelativePath").GetString()!.Replace('\\','/'),x=>x.GetProperty("Sha256").GetString()!,StringComparer.Ordinal);
  // Installed-app tests run after the Windows/processor producer has closed its
  // inventory. Accept their independent receipt only after that stage completed.
  if(c.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.AppTests,out var app) && app.RelativePath=="installed-app-tests.json") {
   string receipt=Path.Combine(root,app.RelativePath);
   if(AutomationFiles.Hash(receipt)!=app.Sha256)throw new InvalidDataException("Completed app receipt changed.");
   AutomationInstalledApp.VerifyRetained(root);
   using var installed=JsonDocument.Parse(File.ReadAllBytes(receipt));
   if(installed.RootElement.GetProperty("InputSha256").GetString()!=c.Checkpoint.InputSha256)
    throw new InvalidDataException("App observations belong to another workflow.");
   foreach(var file in installed.RootElement.GetProperty("Files").EnumerateArray())
    if(!retained.TryAdd(file.GetProperty("RelativePath").GetString()!.Replace('\\','/'),file.GetProperty("Sha256").GetString()!))
     throw new InvalidDataException("Producer evidence paths overlap.");
  }
  var sources=new List<SubmissionEvidenceFile>();
  if(settings.PostEnduranceTests!=null)
   foreach(var file in AutomationPostEndurance.RetainedFiles(c))
    if(!retained.TryAdd(file.RelativePath,file.Sha256))throw new InvalidDataException("Post-endurance evidence paths overlap.");
  if(plan.SourceApplicability is not null)
   sources.Add(AutomationSourceApplicability.Prepare(root,settings,token));
  if(plan.Applicability is not null)
   sources.Add(AutomationApplicability.Prepare(root,identity,plan,token));
  if(plan.Qualifications is not null)
   sources.Add(AutomationQualifications.Prepare(root,identity,plan,token));
  if(plan.PriorEvidence is not null) {
   var prior=AutomationPriorEvidence.Prepare(root,identity,plan,token);
   string priorPath=Path.Combine(folder,"prior-observations.json");WriteDocument(priorPath,prior);
   sources.Add(new("review-inputs/prior-observations.json",AutomationFiles.Hash(priorPath)));
  }
  if(plan.ObservationSources.Distinct(StringComparer.Ordinal).Count()!=plan.ObservationSources.Length)throw new InvalidDataException("Duplicate observation source.");
  foreach(string relative in plan.ObservationSources) {
   if(!retained.TryGetValue(relative,out var hash) || !SubmissionEvidence.SafeEvidencePath(root,relative,out var path) || AutomationFiles.Hash(path)!=hash)
    throw new InvalidDataException("Observation source is not retained verified producer output.");
   if(relative.StartsWith(AutomationPostEndurance.DirectoryName+"/",StringComparison.Ordinal)) {
    var document=AutomationFiles.Read<SubmissionEvidenceDocument>(path);
    if(document.SchemaVersion!=1)throw new InvalidDataException("Unsupported post-endurance observation document.");
    string rebased="review-inputs/post-endurance-"+sources.Count.ToString("D3",System.Globalization.CultureInfo.InvariantCulture)+".json";
    WriteDocument(Path.Combine(root,rebased),new SubmissionEvidenceDocument(1,document.Observations.Select(o=>Rebase(o,AutomationPostEndurance.DirectoryName)).ToArray()));
    sources.Add(new(rebased,AutomationFiles.Hash(Path.Combine(root,rebased))));
   } else sources.Add(new(relative,hash));
  }
  if(File.Exists(Path.Combine(root,"endurance-evidence.json"))) {
   var envelope=AutomationFiles.Read<EnduranceEnvelope>(Path.Combine(root,"endurance-evidence.json"));
   if(envelope.EvidenceDirectory!="endurance/observations" || envelope.Observation.Identity!=identity)
    throw new InvalidDataException("Endurance must use the configured review policy and candidate.");
   var observation=Rebase(envelope.Observation,envelope.EvidenceDirectory);
   string path=Path.Combine(folder,"endurance.json");WriteDocument(path,new SubmissionEvidenceDocument(1,[observation]));
   sources.Add(new("review-inputs/endurance.json",AutomationFiles.Hash(path)));
  }
  if(sources.Count==0)throw new InvalidDataException("No retained observations were supplied.");
  string composition=Path.Combine(folder,"composition.json");WriteDocument(composition,new SubmissionEvidenceCompositionPlan(1,identity,sources));
  var report=SubmissionEvidenceComposition.CombineFiles(root,"review-inputs/composition.json",AutomationFiles.Hash(composition),"review-inputs/policy.json",DateTimeOffset.UtcNow,token);
  WriteDocument(Path.Combine(folder,"composition-report.json"),report);
  // Preserve every outcome. The public review command applies the full policy and any explicit gap declarations.
  string observations=Path.Combine(folder,"observations.json");WriteDocument(observations,report.Observations);
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
 private static SubmissionObservation Rebase(SubmissionObservation o,string root) {
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
  var files=Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal)
   .Select(p=>new SubmissionWorkflowReceipt(Path.GetRelativePath(c.RunDirectory,p),AutomationFiles.Hash(p))).ToArray();
  return AutomationFiles.Complete(c,"review-evidence.json",new Receipt(c.Checkpoint.InputSha256,hash,files));
 }
 internal static void VerifyRetained(string root) {
  foreach(var file in AutomationFiles.Read<Receipt>(Path.Combine(root,"review-evidence.json")).Files)
   if(!SubmissionEvidence.SafeEvidencePath(root,file.RelativePath.Replace('\\','/'),out var path) || AutomationFiles.Hash(path)!=file.Sha256)
    throw new InvalidDataException("Retained review packet changed.");
 }
 internal static void WriteDocument<T>(string path,T value)=>WriteBytes(path,JsonSerializer.SerializeToUtf8Bytes(value,DocumentJson));
 private static void WriteBytes(string path,byte[] bytes,string? expected=null) {
  string digest=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
  if(expected!=null && digest!=expected)throw new InvalidDataException("Review input changed while copying.");
  if(File.Exists(path)) {if(AutomationFiles.Hash(path)!=digest)throw new InvalidDataException("Retained review input changed.");return;}
  using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);file.Write(bytes);file.Flush(true);
 }
}
