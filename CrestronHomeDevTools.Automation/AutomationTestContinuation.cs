// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
namespace CrestronHomeDevTools.Automation;

// Planning only. This class cannot execute tests, import a pass, change a checkpoint or prepare documents.
internal static class AutomationTestContinuation
{
 internal enum Disposition { NewExecutionRequired, PriorPassReviewRequired, OriginalSourceReviewRequired, InstallationReviewRequired, QualifiedEvidenceReviewRequired }
 internal sealed record Requirement(string Id, SubmissionEvidenceOutcome? OriginalOutcome, Disposition Disposition, string[] Reasons);
 internal sealed record Inspection(SubmissionEvidenceIdentity Identity, Requirement[] Requirements, string[] ChangedRequirements) {
  public bool PlanningOnly => true;
  public bool ExecutionAuthorized => false;
  public bool PhaseThreeAuthorized => false;
  public bool ProducerAuthenticationRequired => true;
 }
 internal static Inspection Analyze(SubmissionAutomationSettings settings, SubmissionEvidencePolicy policy,
  SubmissionEvidenceCompositionReport report, string[] changedRequirements)
 {
  var review=settings.Review??throw new InvalidDataException("A pinned complete test policy is required.");
  var identity=new SubmissionEvidenceIdentity(settings.Release.PackageSha256,settings.Release.SourceCommit,review.Policy.Sha256,review.Template.Sha256);
  if(policy.SchemaVersion!=1 || policy.Requirements.Count==0 || policy.Requirements.Any(r=>r==null || string.IsNullOrWhiteSpace(r.Id)) ||
   policy.Requirements.Select(r=>r.Id).Distinct(StringComparer.Ordinal).Count()!=policy.Requirements.Count ||
   report.Observations.SchemaVersion!=1 || report.Observations.Observations.Any(o=>o==null || o.Identity!=identity || !Enum.IsDefined(o.Outcome)) ||
   report.Observations.Observations.Select(o=>o.RequirementId).Distinct(StringComparer.Ordinal).Count()!=report.Observations.Observations.Count)
   throw new InvalidDataException("Continuation requires an unambiguous original policy and candidate evidence.");
  var ids=policy.Requirements.Select(r=>r.Id).ToHashSet(StringComparer.Ordinal);
  if(changedRequirements==null || changedRequirements.Any(id=>!ids.Contains(id)) || changedRequirements.Distinct(StringComparer.Ordinal).Count()!=changedRequirements.Length ||
   report.Observations.Observations.Any(o=>!ids.Contains(o.RequirementId)) || report.Evidence.Issues.Any(i=>!ids.Contains(i.RequirementId)))
   throw new InvalidDataException("Continuation scopes must identify distinct requirements in the unchanged policy.");
  string[] structural=["duplicate-observation","unknown-requirement","identity-mismatch","invalid-time","invalid-evidence-file","unavailable-evidence","evidence-digest"];
  if(report.Evidence.Issues.Any(i=>structural.Contains(i.Code,StringComparer.Ordinal)))
   throw new InvalidDataException("Inspect invalid original evidence before planning result reuse.");
  var qualifications=AutomationTestAssessment.ReadQualifications(settings,report);
  var decisions=policy.Requirements.ToDictionary(r=>r.Id,r=> {
   var original=report.Observations.Observations.SingleOrDefault(o=>o.RequirementId==r.Id);
   var issues=report.Evidence.Issues.Where(i=>i.RequirementId==r.Id).ToArray();
   bool qualified=issues.Length>0 && issues.All(i=>AutomationTestAssessment.IsQualifiedIssue(settings,report,i,qualifications));
   var reasons=issues.Select(i=>i.Code).Distinct(StringComparer.Ordinal).ToList();
   bool changed=changedRequirements.Contains(r.Id,StringComparer.Ordinal);
   if(changed)reasons.Add("test-implementation-changed");
   bool fresh=changed || original==null || (issues.Length>0 && !qualified) || original.Outcome is SubmissionEvidenceOutcome.Failed or SubmissionEvidenceOutcome.NotTested or SubmissionEvidenceOutcome.Inconclusive || (original.Outcome==SubmissionEvidenceOutcome.Partial && !qualified);
   if(original==null)reasons.Add("no-original-observation");
   else if(fresh)reasons.Add("original-result-does-not-establish-continuation-pass");
   var disposition=fresh?Disposition.NewExecutionRequired:qualified?Disposition.QualifiedEvidenceReviewRequired:original!.Outcome==SubmissionEvidenceOutcome.Passed
    ?Disposition.PriorPassReviewRequired:Disposition.OriginalSourceReviewRequired;
   if(!fresh)reasons.Add(qualified?"explicit-rehearsal-limitation-remains-qualified":disposition==Disposition.PriorPassReviewRequired?"scoped-change-impact-review-required":"original-native-evidence-or-applicability-review-required");
   return new Requirement(r.Id,original?.Outcome,disposition,reasons.ToArray());
  },StringComparer.Ordinal);
  if(settings.ResponseComparison is {} comparison) {
   string interval=settings.Endurance?.Plan.Requirement.Id??throw new InvalidDataException("Response comparison must bind its endurance interval.");
   if(!ids.Contains(comparison.RequirementId) || !ids.Contains(interval) || interval==comparison.RequirementId)
    throw new InvalidDataException("Response comparison and interval must be distinct original requirements.");
   // Measurements must bracket the SAME interval with the SAME method. Never splice a new endpoint into an old pair.
   string[] coupled=[comparison.RequirementId,interval];
   if(coupled.Any(id=>decisions[id].Disposition==Disposition.NewExecutionRequired))
    foreach(string id in coupled)decisions[id]=decisions[id] with{Disposition=Disposition.NewExecutionRequired,
     Reasons=decisions[id].Reasons.Append("matching-before-interval-after-execution-required").ToArray()};
  }
  if(decisions.Values.Any(d=>d.Disposition==Disposition.NewExecutionRequired) && settings.Removal is {} removal) {
   foreach(string id in new[]{removal.RequirementId,removal.PlacementRequirementId}.OfType<string>()) {
    if(!ids.Contains(id))throw new InvalidDataException("Installation scope is absent from the original policy.");
    if(decisions[id].Disposition!=Disposition.NewExecutionRequired)decisions[id]=decisions[id] with{Disposition=Disposition.InstallationReviewRequired,
     Reasons=decisions[id].Reasons.Append("prior-placement-or-cleanup-does-not-cover-a-later-installation").ToArray()};
   }
  }
  return new(identity,decisions.Values.OrderBy(d=>d.Id,StringComparer.Ordinal).ToArray(),changedRequirements.Order(StringComparer.Ordinal).ToArray());
 }
 internal static object Inspect(AutomationRequest request,string statePin,string composition,string compositionPin,string[] changed,CancellationToken token)
  =>SubmissionWorkflow.WithVerifiedCheckpoint(request.Settings.PrivateRoot,request.Settings.Release,c=> {
   if(AutomationFiles.Hash(Path.Combine(c.RunDirectory,"state.json"))!=statePin)throw new InvalidDataException("Inspected workflow state changed.");
   var review=request.Settings.Review??throw new InvalidDataException("Test policy missing.");
   if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,composition,out var path) || AutomationFiles.Hash(path)!=compositionPin ||
    AutomationFiles.Hash(review.Policy.Path)!=review.Policy.Sha256)throw new InvalidDataException("Original composition or policy changed.");
   string policyRelative=(Path.GetDirectoryName(composition)?.Replace('\\','/') is {Length:>0} folder?folder+"/":"")+"policy.json";
   if(!SubmissionEvidence.SafeEvidencePath(c.RunDirectory,policyRelative,out var retainedPolicy) || AutomationFiles.Hash(retainedPolicy)!=review.Policy.Sha256)
    throw new InvalidDataException("Composition must retain the exact original policy beside its plan.");
   var policy=AutomationFiles.Read<SubmissionEvidencePolicy>(retainedPolicy);
   var report=SubmissionEvidenceComposition.CombineFiles(c.RunDirectory,composition,compositionPin,policyRelative,DateTimeOffset.UtcNow,token);
   return new { SchemaVersion=1, c.Checkpoint.InputSha256, SettingsSha256=request.Sha256, StateSha256=statePin,
    OriginalComposition=new SubmissionEvidenceFile(composition,compositionPin),Inspection=Analyze(request.Settings,policy,report,changed),
    Instruction="Preserve the original run. This inspection grants no test execution, prior-pass acceptance, checkpoint transition, document preparation or delivery authority." };
  });
 internal static int Command(AutomationRequest request,string statePin,string composition,string compositionPin,string changedFile,string changedPin) {
  using var input=new FileStream(changedFile,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(input.Length>1024*1024)throw new InvalidDataException("Changed-test selection exceeds its size limit.");
  byte[] bytes=new byte[checked((int)input.Length)];input.ReadExactly(bytes);
  if(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))!=changedPin)throw new InvalidDataException("Changed-test selection differs from its pin.");
  var changed=JsonSerializer.Deserialize<string[]>(bytes,AutomationFiles.Json)??throw new InvalidDataException("Missing changed-test selection.");
  Console.WriteLine(JsonSerializer.Serialize(Inspect(request,statePin,composition,compositionPin,changed,default),AutomationFiles.Json));return 0;
 }
}
