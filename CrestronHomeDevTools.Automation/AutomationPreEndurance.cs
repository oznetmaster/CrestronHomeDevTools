// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools.Automation;

/// <summary>Enforce complete initial coverage, independently of the later declared-gap review mode.</summary>
internal static class AutomationPreEndurance
{
 internal static SubmissionEvidenceReport Check(SubmissionWorkflowStepContext context,
  SubmissionAutomationSettings settings,CancellationToken token)
 {
  SubmissionEvidenceReport Missing(string code,string message)=>new([new("",code,message)]);
  var review=settings.Review;
  if(review==null)return Missing("review-policy-required","Freeze the complete checklist policy before endurance.");
  if(settings.Endurance==null)return Missing("endurance-plan-required","An endurance plan is required.");
  if(AutomationFiles.Hash(review.Policy.Path)!=review.Policy.Sha256 ||
   AutomationFiles.Hash(review.Template.Path)!=review.Template.Sha256)
   throw new InvalidDataException("Pre-endurance policy or template changed.");
  var policy=AutomationFiles.Read<SubmissionEvidencePolicy>(review.Policy.Path);
  if(policy.SchemaVersion!=1)throw new InvalidDataException("Unsupported pre-endurance policy.");
  string root=context.RunDirectory;
  var identity=new SubmissionEvidenceIdentity(settings.Release.PackageSha256,settings.Release.SourceCommit,
   review.Policy.Sha256,review.Template.Sha256);
  // Only obligations that intrinsically follow the interval may be deferred. In particular,
  // PlacementRequirementId is NOT deferred with removal. PlannedGaps cannot waive this gate.
  var endurance=settings.Endurance.Plan;
  var policyEndurance=policy.Requirements.SingleOrDefault(r=>r.Id==endurance.Requirement.Id);
  bool durationMatches=policyEndurance!=null && (endurance.Requirement==policyEndurance ||
   (settings.Mode==SubmissionAutomationMode.Rehearsal && endurance.Requirement.MinimumDuration>TimeSpan.Zero &&
    endurance.Requirement.MinimumDuration<policyEndurance.MinimumDuration &&
    endurance.Requirement with{MinimumDuration=policyEndurance.MinimumDuration}==policyEndurance));
  if(endurance.Identity!=identity || !durationMatches ||
   endurance.Requirement.Execution?.Method!="endurance")
   throw new InvalidDataException("Endurance must bind the full reviewed policy's endurance requirement.");
  var deferred=new HashSet<string>(StringComparer.Ordinal){endurance.Requirement.Id};
  if(settings.Removal!=null)deferred.Add(AutomationRemoval.Validate(settings).Id);
  if(settings.ResponseComparison!=null)deferred.Add(AutomationResponseComparison.Validate(settings).Id);
  if(policy.Requirements.All(r=>deferred.Contains(r.Id)))
   throw new InvalidDataException("Pre-endurance policy must include initial functional requirements.");

  var retained=new Dictionary<string,string>(StringComparer.Ordinal);
  foreach(var stage in new[]{SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests}) {
   if(!context.Checkpoint.CompletedStages.TryGetValue(stage,out var receipt))
    return Missing("initial-stage-incomplete",$"{stage} must complete before endurance.");
   if(!SubmissionEvidence.SafeEvidencePath(root,receipt.RelativePath,out var path) || AutomationFiles.Hash(path)!=receipt.Sha256)
    throw new InvalidDataException("Initial test stage receipt changed.");
   var producer=AutomationFiles.Read<ProducerReceiptWithStage>(path);
   if(producer.InputSha256!=context.Checkpoint.InputSha256)
    throw new InvalidDataException("Initial tests belong to another workflow.");
   foreach(var file in producer.Files) {
    token.ThrowIfCancellationRequested();
    if(!SubmissionEvidence.SafeEvidencePath(root,file.RelativePath,out var evidence) || AutomationFiles.Hash(evidence)!=file.Sha256)
     throw new InvalidDataException("Initial producer evidence changed.");
    string relative=file.RelativePath.Replace('\\','/');
    if(retained.TryGetValue(relative,out var old) && old!=file.Sha256)
     throw new InvalidDataException("Initial producer inventories disagree.");
    retained[relative]=file.Sha256;
   }
  }
  if(settings.PreEnduranceTests!=null)
   foreach(var file in AutomationInitialAdditionalTests.RetainedFiles(context))
    if(!retained.TryAdd(file.RelativePath,file.Sha256) && retained[file.RelativePath]!=file.Sha256)
     throw new InvalidDataException("Additional initial evidence paths overlap.");
  if(settings.ResponseComparison is {} comparison && comparison.Pairs.Any(p=>!retained.ContainsKey(AutomationReview.ResolveObservationSource(p.Before,retained,root))))
   return Missing("response-baseline-missing","Retain the configured initial response measurements before endurance.");
  var observations=new List<SubmissionObservation>();
  string[] sources=review.PreEnduranceObservationSources ?? review.ObservationSources
   .Where(p=>!p.StartsWith(AutomationPostEndurance.DirectoryName+"/",StringComparison.Ordinal)).ToArray();
  if(sources.Length>1024 || sources.Distinct(StringComparer.Ordinal).Count()!=sources.Length)
   throw new InvalidDataException("Initial observation sources must be distinct and bounded.");
  foreach(string requested in sources) {
   // Use the same accepted replacement lineage and producer-relative paths as
   // final review. A completed step's evidence is not rooted at the phase root.
   string relative=AutomationReview.ResolveObservationSource(requested,retained,root);
   if(!retained.TryGetValue(relative,out var hash) || !SubmissionEvidence.SafeEvidencePath(root,relative,out var path) || AutomationFiles.Hash(path)!=hash)
    throw new InvalidDataException("Initial observation is not retained completed producer output.");
   var document=AutomationFiles.Read<SubmissionEvidenceDocument>(path);
   if(document.SchemaVersion!=1 || document.Observations.Count is <1 or >1024)
    throw new InvalidDataException("Invalid initial observation document.");
   string? producerRoot=AutomationReview.ObservationBase(relative);
   foreach(var raw in document.Observations) {
    var observation=producerRoot==null?raw:AutomationReview.Rebase(raw,producerRoot);
    if(observation.Files.Any(f=>!retained.TryGetValue(f.RelativePath.Replace('\\','/'),out var pin) || pin!=f.Sha256))
     throw new InvalidDataException("Initial observation references evidence outside its completed producer inventory.");
    observations.Add(observation);
   }
  }
  void AddPrepared(SubmissionEvidenceFile file) {
   if(!SubmissionEvidence.SafeEvidencePath(root,file.RelativePath,out var path) || AutomationFiles.Hash(path)!=file.Sha256)
    throw new InvalidDataException("Prepared applicability evidence changed.");
   observations.AddRange(AutomationFiles.Read<SubmissionEvidenceDocument>(path).Observations);
  }
  if(review.SourceApplicability!=null)AddPrepared(AutomationSourceApplicability.Prepare(root,settings,token));
  if(review.Applicability!=null)AddPrepared(AutomationApplicability.Prepare(root,identity,review,token));
  if(review.PriorEvidence!=null)observations.AddRange(AutomationPriorEvidence.Prepare(root,identity,review,token).Observations);
  if(File.Exists(Path.Combine(root,AutomationPlacement.ReceiptName)))AddPrepared(AutomationPlacement.VerifyRetained(context));
  // Evaluate the full policy to retain unknown/duplicate/invalid observations. Only missing
  // evidence for the specifically bound later operations is expected at this stage.
  var report=SubmissionEvidence.Evaluate(identity,policy.Requirements,observations,root,DateTimeOffset.UtcNow,token);
  return new(report.Issues.Where(i=>!(i.Code=="missing-observation" && deferred.Contains(i.RequirementId))).ToArray());
 }
 // NUnit receipts contain Stage; installed-app receipts do not. Neither may contain unknown fields.
 private sealed record ProducerReceiptWithStage(string InputSha256,SubmissionWorkflowReceipt[] Files,string? Stage=null);
}
