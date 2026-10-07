// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

// Explicit correction of an invalid test arrangement. Original frozen inputs remain
// untouched; every newly bound invocation must pass before the original step can close.
internal static class AutomationAppScopeRevision
{
 internal sealed record Invocation(InstalledDriverTestPlan Tests,JsonElement Fixture,string CredentialBindings,
  string SourceSha256,string ProfileSha256) {
  [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public RetainedScope? Retained {get;init;}
 }
 internal sealed record RetainedScope(string AttemptId,int Invocation,string VerifiedSha256,string Reason);
 internal sealed record Observation(string OriginalPath,int Invocation,string RevisedPath);
 internal sealed record Plan(string Reason,Invocation[] Invocations,Observation[] Observations);
 private sealed record ChildReceipt(SubmissionWorkflowReceipt[] Files);
 private static bool Equal<T>(T a,T b)=>JsonSerializer.Serialize(a,AutomationFiles.Json)==JsonSerializer.Serialize(b,AutomationFiles.Json);
 private static string Child(int index)=>"invocations/"+index.ToString("D3")+"/";
 private static bool Relative(string path)=>!string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
  !path.Contains('\\') && !path.Contains(':') && path.Split('/').All(s=>s is not ("" or "." or ".."));

 private static string RetainedPrefix(RetainedScope retained) {
  AutomationAppStepRecovery.RequireId(retained.AttemptId);
  if(retained.Invocation is <0 or >7 || retained.VerifiedSha256.Length!=64 || !retained.VerifiedSha256.All(char.IsAsciiHexDigit) ||
   string.IsNullOrWhiteSpace(retained.Reason) || retained.Reason.Length>3000)
   throw new InvalidDataException("Retained success requires an exact receipt and explicit applicability review.");
  return "installed-app/recovery-attempts/"+retained.AttemptId+"/"+Child(retained.Invocation);
 }
 private static string VerifyRetainedScope(string root,Invocation item,string currentAttempt) {
  var retained=item.Retained!;
  if(retained.AttemptId==currentAttempt)throw new InvalidDataException("Retained success must come from an earlier attempt.");
  string prefix=RetainedPrefix(retained);
  if(!SubmissionEvidence.SafeEvidencePath(root,prefix+"verified.json",out var receipt) || AutomationFiles.Hash(receipt)!=retained.VerifiedSha256 ||
   !SubmissionEvidence.SafeEvidencePath(root,prefix+"intent.json",out var intent))
   throw new InvalidDataException("Retained success receipt or binding is missing or changed.");
  var original=AutomationFiles.Read<Invocation>(intent);
  if(original.Retained!=null || !Equal(original,item with{Retained=null}))
   throw new InvalidDataException("Retained success cannot change its original candidate, processor, fixture, policy, probe or profile.");
  string child=Path.GetDirectoryName(receipt)!;
  if(!AutomationFiles.Read<InstalledDriverTestResult>(Path.Combine(child,"installed-app","InstalledDriverTests.json")).Passed ||
   !AutomationFiles.Read<ChildReceipt>(receipt).Files.SequenceEqual(AutomationInstalledApp.Inventory(child)))
   throw new InvalidDataException("Retained scope did not pass with intact evidence and confirmed cleanup.");
  return prefix+"installed-app/AndroidUI/";
 }
 internal static async Task Validate(SubmissionAutomationSettings settings,AutomationAppStepRecovery.Request request,CancellationToken token)
 {
  var revision=request.ScopeRevision??throw new InvalidDataException("Missing scope revision.");
  var original=settings.InstalledAppTests??throw new InvalidDataException("Missing original scope.");
  if(string.IsNullOrWhiteSpace(revision.Reason) || revision.Reason.Length>3000 ||
   revision.Invocations is not {Length:>=2 and <=8} || revision.Observations is not {Length:>0 and <=64} ||
   revision.Invocations.Any(i=>i==null) || revision.Observations.Any(o=>o==null) ||
   !Equal(request.Replacement,revision.Invocations[0].Tests) || request.SourceSha256!=revision.Invocations[0].SourceSha256 ||
   request.ProfileSha256!=revision.Invocations[0].ProfileSha256)
   throw new InvalidDataException("A scope revision requires an explicit reason, bounded invocations and matching primary pins.");
  foreach(var item in revision.Invocations) {
   var next=item.Tests;next.Validate();
   if(next.PackagePath!=original.PackagePath || next.PackageSha256!=original.PackageSha256 || next.PackageSourceCommit!=original.PackageSourceCommit ||
    next.Target.Model!=original.Target.Model || Version.Parse(next.Target.Version)!=Version.Parse(original.Target.Version) || next.Target.Developer!=original.Target.Developer || next.Target.ControlType!=original.Target.ControlType ||
    next.TimeoutSeconds!=original.TimeoutSeconds || next.LeaseWaitSeconds!=original.LeaseWaitSeconds ||
    !Equal(next.AndroidTests.RequiredTests,original.AndroidTests.RequiredTests) || next.AndroidTests.ManagedChildren.Count!=0 ||
    next.OperatorReadiness==null || original.OperatorReadiness==null ||
    next.OperatorReadiness.Directory!=original.OperatorReadiness.Directory || next.OperatorReadiness.RunKey!=original.OperatorReadiness.RunKey ||
    !Path.IsPathFullyQualified(item.CredentialBindings) || item.Fixture.ValueKind!=JsonValueKind.Object ||
    AutomationFiles.Hash(next.PackagePath)!=next.PackageSha256 || AutomationFiles.Hash(next.AndroidTests.ProfilePath)!=item.ProfileSha256 ||
    await WorkflowEvidence.SourceDigestAsync(next.SourceRoots,token)!=item.SourceSha256)
    throw new InvalidDataException("Revised scopes must retain the candidate, selected tests, budgets and operator inbox, with pinned source/profile and explicit credentials.");
   // A new physical arrangement cannot change the candidate/template/policy identity.
   if(settings.InstalledAppFixtureSettings is not {} fixture ||
    !fixture.TryGetProperty("EvidenceIdentity",out var identity) ||
    !item.Fixture.TryGetProperty("EvidenceIdentity",out var nextIdentity) || !JsonElement.DeepEquals(identity,nextIdentity))
    throw new InvalidDataException("Revised fixture must retain its complete evidence identity and policy.");
  }
  string prefix=(request.Phase=="main"?"":request.Phase+"/")+"installed-app/steps/"+request.Step.ToString("D3")+"/installed-app/AndroidUI/";
  var review=settings.Review??throw new InvalidDataException("Scope revisions require the retained review observation contract.");
  var expected=review.ObservationSources.Concat(review.PreEnduranceObservationSources??[]).Where(p=>p.StartsWith(prefix,StringComparison.Ordinal))
   .Select(p=>p[prefix.Length..]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
  var supplied=revision.Observations.Select(o=>o.OriginalPath).Order(StringComparer.Ordinal).ToArray();
  if(expected.Length==0 || !expected.SequenceEqual(supplied,StringComparer.Ordinal) ||
   revision.Observations.Any(o=>o.Invocation<0 || o.Invocation>=revision.Invocations.Length || !Relative(o.OriginalPath) || !Relative(o.RevisedPath)) ||
   Enumerable.Range(0,revision.Invocations.Length).Any(i=>!revision.Observations.Any(o=>o.Invocation==i)))
   throw new InvalidDataException("Revision must map every retained observation exactly once and cover every new invocation.");
 }

 internal static async Task<SubmissionWorkflowStepResult> Execute(SubmissionWorkflowStepContext context,SubmissionAutomationSettings settings,
  AutomationAppStepRecovery.Request request,string attempt,
  Func<InstalledDriverTestPlan,NetworkCredential,string,CancellationToken,Task<InstalledDriverTestResult>> run,
  Func<Invocation,NetworkCredential> credentials,CancellationToken token)
 {
  var revision=request.ScopeRevision!;
  // Resolve every host before publishing readiness for the first scope.
  var logins=revision.Invocations.Select(i=>i.Retained==null?credentials(i):null).ToArray();
  var producers=new string[revision.Invocations.Length];
  var retainedReceipts=new Dictionary<string,string>(StringComparer.Ordinal);
  // Validate every retained success before any new physical action can be requested.
  foreach(var item in revision.Invocations.Where(i=>i.Retained!=null))VerifyRetainedScope(context.RunDirectory,item,request.AttemptId);
  var results=new List<InstalledDriverTestResult>();
  for(int i=0;i<revision.Invocations.Length;i++) {
   token.ThrowIfCancellationRequested();await Validate(settings,request,token);
   if(AutomationAppStepRecovery.EvidenceHash(context.RunDirectory,request.AttemptId)!=request.OriginalEvidenceSha256)
    throw new InvalidDataException("Original evidence changed during the revised operation.");
   var item=revision.Invocations[i];
   if(item.Retained!=null) {
    producers[i]=VerifyRetainedScope(context.RunDirectory,item,request.AttemptId);
    retainedReceipts.Add(producers[i],item.Retained.VerifiedSha256);
    continue;
   }
   producers[i]="installed-app/recovery-attempts/"+request.AttemptId+"/"+Child(i)+"installed-app/AndroidUI/";
   string child=Path.Combine(attempt,Child(i));
   string done=Path.Combine(child,"verified.json"),intent=Path.Combine(child,"intent.json"),output=Path.Combine(child,"installed-app");
   var bound=settings with{InstalledAppFixtureSettings=item.Fixture,InstalledAppTests=item.Tests};
   if(File.Exists(done)) {
    if(!Equal(AutomationFiles.Read<Invocation>(intent),item))throw new InvalidDataException("Completed revised invocation binding changed.");
    AutomationAppFixture.Check(child,bound,false);
    if(!AutomationFiles.Read<ChildReceipt>(done).Files.SequenceEqual(AutomationInstalledApp.Inventory(child)))
     throw new InvalidDataException("A completed revised invocation changed.");
   } else {
    bool invoked=File.Exists(intent);
    if(invoked) {
     if(!Equal(AutomationFiles.Read<Invocation>(intent),item))throw new InvalidDataException("Revised invocation changed; no replay.");
     AutomationAppFixture.Check(child,bound,false);
    } else {
     if(Directory.Exists(child))throw new InvalidDataException("Unresolved partial preparation; inspect without replay.");
     Directory.CreateDirectory(child);AutomationAppFixture.Check(child,bound,true);
     AutomationFiles.Write(intent,item);
     var ready=item.Tests.OperatorReadiness!;
     var plan=item.Tests with{OperatorReadiness=ready with{Step="app-revision-"+request.AttemptId+"-"+i.ToString("D3")+".ready"}};
     await run(plan,logins[i]!,output,token);
    }
    string resultPath=Path.Combine(output,"InstalledDriverTests.json");
    if(!File.Exists(resultPath))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-app-revision-no-replay");
    using(var doc=JsonDocument.Parse(File.ReadAllBytes(resultPath)))
     if(doc.RootElement.TryGetProperty("State",out _))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-app-revision-no-replay");
    var observed=AutomationFiles.Read<InstalledDriverTestResult>(resultPath);results.Add(observed);
    if(!observed.Passed) {
     // Keep the aggregate at the ordinary failed-attempt location for subsequent
     // explicit reconciliation. Unstarted scopes are not reported as passed.
     Directory.CreateDirectory(Path.Combine(attempt,"installed-app"));
     string failed=Path.Combine(attempt,"installed-app","InstalledDriverTests.json");
     var explanation=FailureExplanation(output,i,observed);
     if(!File.Exists(failed))AutomationFiles.Write(failed,observed with{Detail=explanation.Detail});
     return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:explanation.Code);
    }
    await Validate(settings,request,token);AutomationAppFixture.Check(child,bound,false);
    AutomationFiles.Write(done,new ChildReceipt(AutomationInstalledApp.Inventory(child)));
   }
  }
  await Validate(settings,request,token);
  using(var originalIntent=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(context.RunDirectory,"installed-app-intent.json"))))
   if(originalIntent.RootElement.GetProperty("SourceDigest").GetString()!=await WorkflowEvidence.SourceDigestAsync(settings.InstalledAppTests!.SourceRoots,token))
    throw new InvalidDataException("Original frozen source changed during revision.");
  AutomationAppFixture.Check(context.RunDirectory,settings,false);
  if(AutomationAppStepRecovery.EvidenceHash(context.RunDirectory,request.AttemptId)!=request.OriginalEvidenceSha256)
   throw new InvalidDataException("Original evidence changed during revision.");
  foreach(var item in revision.Invocations.Where(i=>i.Retained!=null))VerifyRetainedScope(context.RunDirectory,item,request.AttemptId);
  var observations=revision.Observations.ToDictionary(o=>o.OriginalPath,o=>producers[o.Invocation]+o.RevisedPath,StringComparer.Ordinal);
  foreach(var mapping in observations)
   if(!SubmissionEvidence.SafeEvidencePath(context.RunDirectory,mapping.Value,out _))throw new InvalidDataException("Revised observation output is missing.");
  AutomationFiles.Write(Path.Combine(context.RunDirectory,"installed-app","replacement.json"),
   new AutomationAppStepRecovery.Completion(request.AttemptId,producers[0],request.OriginalEvidenceSha256,producers,observations){RetainedProducerReceipts=retainedReceipts.Count==0?null:retainedReceipts});
  return AutomationFiles.Complete(context,"installed-app-tests.json",new{context.Checkpoint.InputSha256,Files=AutomationInstalledApp.Inventory(context.RunDirectory)});
 }

 private static (string Code,string Detail) FailureExplanation(string output,int scope,InstalledDriverTestResult result) {
  string path=Path.Combine(output,"AndroidUI","system-outage","recording-result.json");
  if(!File.Exists(path) || new FileInfo(path).Length>65536)
   return ("app-revision-failed-inspect-retained-evidence","Revised scope "+scope+" failed; inspect retained test and restoration results. The workflow has not continued.");
  using var json=JsonDocument.Parse(File.ReadAllBytes(path));
  if(!json.RootElement.TryGetProperty("disposition",out var disposition) ||
   !Enum.TryParse<SubmissionRecoveryDisposition>(disposition.GetString(),out var value))
   return ("app-revision-failed-inspect-retained-evidence","Revised scope "+scope+" failed; inspect retained test and restoration results. The workflow has not continued.");
  string detail=value switch {
   SubmissionRecoveryDisposition.MeasurementInconclusive=>"Recovery timing was not established; this is not proof of a driver failure. Repair or review measurement before any physical retry.",
   SubmissionRecoveryDisposition.HarnessFailed=>"The recovery test tool failed. Repair the tool before any physical retry.",
   SubmissionRecoveryDisposition.BehaviourFailed=>"A recovery requirement failed. Inspect the retained behavioural evidence before retrying.",
   SubmissionRecoveryDisposition.Cancelled=>"Recovery observation was cancelled. Inspect its retained outcome before continuing.",
   _=>"The enclosing app test failed after recovery observation; inspect its retained test output."
  };
  bool restored=result.RestorationConfirmed&&result.CleanupConfirmed&&result.ReservationsReleased;
  return ("app-revision-recovery-"+value.ToString().ToLowerInvariant(),"Scope "+scope+": "+detail+
   (restored?" Restoration and cleanup confirmed. No physical action is currently requested.":" Restoration or cleanup is unconfirmed and needs attention.")+" The workflow has not continued.");
 }
 private static bool ValidRetainedProducer(AutomationAppStepRecovery.Completion accepted,string producer) {
  if(accepted.RetainedProducerReceipts==null || !accepted.RetainedProducerReceipts.TryGetValue(producer,out var hash) ||
   hash.Length!=64 || !hash.All(char.IsAsciiHexDigit))return false;
  var parts=producer.Split('/');
  return parts.Length==8 && parts[0]=="installed-app" && parts[1]=="recovery-attempts" &&
   Guid.TryParseExact(parts[2],"N",out _) && parts[2]!=accepted.AttemptId &&
   parts[3]=="invocations" && int.TryParse(parts[4],out var index) && index is >=0 and <=7 && parts[4]==index.ToString("D3") &&
   parts[5]=="installed-app" && parts[6]=="AndroidUI" && parts[7]=="";
 }
 internal static string[] Accepted(AutomationAppStepRecovery.Completion accepted)
 {
  AutomationAppStepRecovery.RequireId(accepted.AttemptId);
  if(accepted.CaseRecovery!=null)return AutomationAppCaseRecovery.Accepted(accepted);
  string prefix="installed-app/recovery-attempts/"+accepted.AttemptId+"/";
  if(accepted.ProducerPrefixes==null) {
   if(accepted.ObservationSources!=null || accepted.ProducerPrefix!=prefix+"installed-app/AndroidUI/")throw new InvalidDataException("Invalid replacement producer.");
   return [accepted.ProducerPrefix];
  }
  var producers=accepted.ProducerPrefixes;
  if(producers.Length is <2 or >8 || accepted.ProducerPrefix!=producers[0] ||
   producers.Distinct(StringComparer.Ordinal).Count()!=producers.Length ||
   producers.Where((p,i)=>p!=prefix+Child(i)+"installed-app/AndroidUI/").Any(p=>!ValidRetainedProducer(accepted,p)) ||
   (accepted.RetainedProducerReceipts!=null && accepted.RetainedProducerReceipts.Keys.Any(p=>!producers.Contains(p,StringComparer.Ordinal) || !ValidRetainedProducer(accepted,p))) ||
   accepted.ObservationSources is not {Count:>0} || accepted.ObservationSources.Any(p=>!Relative(p.Key) || !Relative(p.Value) || !producers.Any(v=>p.Value.StartsWith(v,StringComparison.Ordinal))))
   throw new InvalidDataException("Invalid revised producer provenance.");
  return producers;
 }
}
