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
  string SourceSha256,string ProfileSha256);
 internal sealed record Observation(string OriginalPath,int Invocation,string RevisedPath);
 internal sealed record Plan(string Reason,Invocation[] Invocations,Observation[] Observations);
 private sealed record ChildReceipt(SubmissionWorkflowReceipt[] Files);
 private static bool Equal<T>(T a,T b)=>JsonSerializer.Serialize(a,AutomationFiles.Json)==JsonSerializer.Serialize(b,AutomationFiles.Json);
 private static string Child(int index)=>"invocations/"+index.ToString("D3")+"/";
 private static bool Relative(string path)=>!string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
  !path.Contains('\\') && !path.Contains(':') && path.Split('/').All(s=>s is not ("" or "." or ".."));

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
  var logins=revision.Invocations.Select(credentials).ToArray();
  var results=new List<InstalledDriverTestResult>();
  for(int i=0;i<revision.Invocations.Length;i++) {
   token.ThrowIfCancellationRequested();await Validate(settings,request,token);
   if(AutomationAppStepRecovery.EvidenceHash(context.RunDirectory,request.AttemptId)!=request.OriginalEvidenceSha256)
    throw new InvalidDataException("Original evidence changed during the revised operation.");
   var item=revision.Invocations[i];string child=Path.Combine(attempt,Child(i));
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
     await run(plan,logins[i],output,token);
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
     if(!File.Exists(failed))AutomationFiles.Write(failed,observed with{Detail="Revised scope "+i+" failed; remaining scopes were not started."});
     return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"app-revision-failed-inspect-retained-evidence");
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
  string prefix="installed-app/recovery-attempts/"+request.AttemptId+"/";
  string[] producers=Enumerable.Range(0,revision.Invocations.Length).Select(i=>prefix+Child(i)+"installed-app/AndroidUI/").ToArray();
  var observations=revision.Observations.ToDictionary(o=>o.OriginalPath,o=>producers[o.Invocation]+o.RevisedPath,StringComparer.Ordinal);
  foreach(var mapping in observations)
   if(!SubmissionEvidence.SafeEvidencePath(context.RunDirectory,mapping.Value,out _))throw new InvalidDataException("Revised observation output is missing.");
  AutomationFiles.Write(Path.Combine(context.RunDirectory,"installed-app","replacement.json"),
   new AutomationAppStepRecovery.Completion(request.AttemptId,producers[0],request.OriginalEvidenceSha256,producers,observations));
  return AutomationFiles.Complete(context,"installed-app-tests.json",new{context.Checkpoint.InputSha256,Files=AutomationInstalledApp.Inventory(context.RunDirectory)});
 }

 internal static string[] Accepted(AutomationAppStepRecovery.Completion accepted)
 {
  AutomationAppStepRecovery.RequireId(accepted.AttemptId);
  string prefix="installed-app/recovery-attempts/"+accepted.AttemptId+"/";
  if(accepted.ProducerPrefixes==null) {
   if(accepted.ObservationSources!=null || accepted.ProducerPrefix!=prefix+"installed-app/AndroidUI/")throw new InvalidDataException("Invalid replacement producer.");
   return [accepted.ProducerPrefix];
  }
  var producers=accepted.ProducerPrefixes;
  if(producers.Length is <2 or >8 || accepted.ProducerPrefix!=producers[0] ||
   !producers.SequenceEqual(Enumerable.Range(0,producers.Length).Select(i=>prefix+Child(i)+"installed-app/AndroidUI/")) ||
   accepted.ObservationSources is not {Count:>0} || accepted.ObservationSources.Any(p=>!Relative(p.Key) || !Relative(p.Value) || !producers.Any(v=>p.Value.StartsWith(v,StringComparison.Ordinal))))
   throw new InvalidDataException("Invalid revised producer provenance.");
  return producers;
 }
}
