// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

public sealed partial class AutomationInstalledAppTests
{
    private async Task<AutomationAppStepRecovery.Request> CaseRequest()
    {
        var plan = settings.InstalledAppTests!;
        settings = settings with { InstalledAppTests = plan with { AndroidTests = plan.AndroidTests with { RequiredTests = ["Fixture.A", "Fixture.B"] } },
            Review = new(null!, null!, null!, null!, null!, "Synthetic", "Synthetic", [
                "post-endurance/installed-app/AndroidUI/a.json", "post-endurance/installed-app/AndroidUI/b.json"]),
            ResponseComparison = new("synthetic", [new("a", "installed-app/AndroidUI/a-response.json", "post-endurance/installed-app/AndroidUI/a-response.json")]) };
        await AutomationInstalledApp.Advance(context, settings, false, (p,c,f,t) => {
            Directory.CreateDirectory(Path.Combine(f, "AndroidUI"));
            var failed = new InstalledDriverTestResult(new WorkflowTestOutcome(1,1,0,false), true,true,true,true,"Synthetic mixed result");
            File.WriteAllText(Path.Combine(f,"InstalledDriverTests.json"),JsonSerializer.Serialize(failed));
            AutomationAppCaseResultsTests.Trx(("A","Passed"),("B","Failed")).Save(Path.Combine(f,"AndroidUI","original.trx"));
            new XDocument(new XElement("test-run", new XElement("test-case",new XAttribute("fullname","Fixture.B"),new XAttribute("runstate","Runnable"))))
                .Save(Path.Combine(f,"AndroidUI","discovery.dump"));
            File.WriteAllText(Path.Combine(f,"AndroidUI","a.json"),"original observation");
            File.WriteAllText(Path.Combine(f,"AndroidUI","a-response.json"),"original measurement");
            return Task.FromResult(failed);
        }, _=>new(), default);
        var original = settings.InstalledAppTests!;
        string trx = "installed-app/AndroidUI/original.trx";
        return new(3,"post-endurance",0,Guid.NewGuid().ToString("N"),new('f',64),AutomationAppStepRecovery.EvidenceHash(context.RunDirectory),
            "installed-app/InstalledDriverTests.json",original with { AndroidTests=original.AndroidTests with { RequiredTests=["Fixture.B"] } },
            await WorkflowEvidence.SourceDigestAsync(original.SourceRoots,default),AutomationFiles.Hash(original.AndroidTests.ProfilePath)) {
            CaseRecovery=new("Unchanged assertions; repair report lifetime only",new(trx,AutomationFiles.Hash(Path.Combine(context.RunDirectory,trx))),
                [new("a.json","Fixture.A"),new("b.json","Fixture.B"),new("a-response.json","Fixture.A")]) };
    }
    private Task<InstalledDriverTestResult> CaseRun(InstalledDriverTestPlan plan,NetworkCredential credential,string folder,CancellationToken token)
    {
        calls++; Assert.That(plan.AndroidTests.RequiredTests,Is.EqualTo(new[]{"Fixture.B"}));
        Directory.CreateDirectory(Path.Combine(folder,"AndroidUI"));
        var result=new InstalledDriverTestResult(new WorkflowTestOutcome(1,0,0,true),true,true,true,true,"Synthetic repaired case");
        File.WriteAllText(Path.Combine(folder,"InstalledDriverTests.json"),JsonSerializer.Serialize(result));
        AutomationAppCaseResultsTests.Trx(("B","Passed")).Save(Path.Combine(folder,"AndroidUI","replacement.trx"));
        File.WriteAllText(Path.Combine(folder,"AndroidUI","b.json"),"new observation"); return Task.FromResult(result);
    }
    [Test] public async Task CaseRecoveryRetainsPassesAndMeasurementsWithoutRerunOrRewritingFailures()
    {
        var request=await CaseRequest();var originals=AutomationAppStepRecovery.Inventory(context.RunDirectory);
        var result=await Replace(request,CaseRun);
        Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
        Assert.That((await Replace(request,CaseRun)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));Assert.That(calls,Is.EqualTo(1));
        foreach(var file in originals)Assert.That(AutomationFiles.Hash(Path.Combine(context.RunDirectory,file.RelativePath)),Is.EqualTo(file.Sha256));
        Assert.That(AutomationAppStepRecovery.Failure(context.RunDirectory,request.FailedOutcome).Tests!.Failed,Is.EqualTo(1));
        var accepted=AutomationFiles.Read<AutomationAppStepRecovery.Completion>(Path.Combine(context.RunDirectory,"installed-app/replacement.json"));
        Assert.That(AutomationAppScopeRevision.Accepted(accepted),Has.Length.EqualTo(2));
        Assert.That(accepted.ObservationSources!["a-response.json"],Is.EqualTo("installed-app/AndroidUI/a-response.json"));
        Assert.That(accepted.ObservationSources["b.json"],Does.Contain(request.AttemptId));
        var receipt=AutomationFiles.Read<AutomationAppCaseRecovery.Receipt>(Path.Combine(context.RunDirectory,accepted.CaseRecovery!.ReceiptPath));
        Assert.That(receipt.Cases,Has.Length.EqualTo(2));Assert.That(receipt.Cases.Select(c=>c.TrxSha256).Distinct().Count(),Is.EqualTo(2));
        AutomationInstalledApp.VerifyRetained(context.RunDirectory);
    }
    [TestCase("passed-selection")][TestCase("candidate")][TestCase("deadline")][TestCase("mapping")]
    [TestCase("observation")][TestCase("trx-pin")][TestCase("phase")][TestCase("explicit")]
    public async Task CaseRecoveryRejectsChangedBindingsBeforeInvocation(string change)
    {
        var request=await CaseRequest();var p=request.Replacement;
        request=change switch {
            "passed-selection"=>request with{Replacement=p with{AndroidTests=p.AndroidTests with{RequiredTests=["Fixture.A"]}}},
            "candidate"=>request with{Replacement=p with{Target=p.Target with{DeviceId=999}}},
            "deadline"=>request with{Replacement=p with{TimeoutSeconds=p.TimeoutSeconds+1}},
            "mapping"=>request with{CaseRecovery=request.CaseRecovery! with{Observations=[new("a.json","Fixture.A"),new("b.json","Fixture.B")]}},
            "trx-pin"=>request with{CaseRecovery=request.CaseRecovery! with{OriginalTrx=request.CaseRecovery.OriginalTrx with{Sha256=new('0',64)}}},
            "phase"=>request with{Phase="pre-endurance"},_=>request};
        if(change=="observation")File.Delete(Path.Combine(context.RunDirectory,"installed-app/AndroidUI/a.json"));
        if(change=="explicit"){
            string file=Path.Combine(context.RunDirectory,"installed-app/AndroidUI/discovery.dump");File.WriteAllText(file,File.ReadAllText(file).Replace("Runnable","Explicit"));
            request=request with{OriginalEvidenceSha256=AutomationAppStepRecovery.EvidenceHash(context.RunDirectory)};
        }
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await Replace(request,CaseRun));Assert.That(calls,Is.Zero);
    }
    [Test] public async Task CaseRecoveryCannotHideIncorrectReplacementCoverage()
    {
        var request=await CaseRequest();
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await Replace(request,async(p,c,f,t)=>{
            var result=await CaseRun(p,c,f,t);
            AutomationAppCaseResultsTests.Trx(("A","Passed")).Save(Path.Combine(f,"AndroidUI","replacement.trx"));return result;
        }));
        Assert.That(File.Exists(Path.Combine(context.RunDirectory,"installed-app-tests.json")),Is.False);
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await Replace(request,CaseRun));Assert.That(calls,Is.EqualTo(1));
    }
    [Test] public async Task InterruptedCaseRecoveryNeverRepeatsTheInvocation()
    {
        var request=await CaseRequest();
        await Assert.ThrowsAsync<IOException>(async()=>await Replace(request,(p,c,f,t)=>{calls++;throw new IOException("interrupted");}));
        Assert.That((await Replace(request,CaseRun)).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));Assert.That(calls,Is.EqualTo(1));
    }
    [Test] public async Task FailedCaseRecoveryRemainsFailedAndNeverRepeats()
    {
        var request=await CaseRequest();
        Task<InstalledDriverTestResult> Failed(InstalledDriverTestPlan p,NetworkCredential c,string f,CancellationToken t) {
            calls++;Directory.CreateDirectory(f);var result=new InstalledDriverTestResult(new WorkflowTestOutcome(0,1,0,false),true,true,true,true,"failure retained");
            File.WriteAllText(Path.Combine(f,"InstalledDriverTests.json"),JsonSerializer.Serialize(result));return Task.FromResult(result);
        }
        Assert.That((await Replace(request,Failed)).Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));
        Assert.That((await Replace(request,CaseRun)).Status,Is.EqualTo(SubmissionWorkflowStatus.NeedsInput));Assert.That(calls,Is.EqualTo(1));
    }
    [TestCase(1,SubmissionWorkflowStage.PrepareReview)][TestCase(2,SubmissionWorkflowStage.FinalizeTests)]
    public async Task PostcheckRepairSelectsTheExplicitLegacyOrCurrentBoundary(int schema,SubmissionWorkflowStage stage)
    {
        await CaseRequest();settings=settings with{PostEnduranceTests=settings.InstalledAppTests};
        var opened=SubmissionWorkflow.Open(settings.PrivateRoot,settings.Release);
        var state=context.Checkpoint with{SchemaVersion=schema,Stage=stage,Status=SubmissionWorkflowStatus.Failed,InputSha256=opened.InputSha256};
        string run=Path.Combine(settings.PrivateRoot,SubmissionWorkflow.RunKey(settings.Release));Directory.CreateDirectory(Path.Combine(run,"post-endurance"));
        foreach(var previous in new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests,SubmissionWorkflowStage.AppTests,SubmissionWorkflowStage.Endurance}){
            string name=previous+".json";File.WriteAllText(Path.Combine(run,name),"synthetic completed stage");state.CompletedStages.Add(previous,new(name,AutomationFiles.Hash(Path.Combine(run,name))));
        }
        File.WriteAllText(Path.Combine(run,"state.json"),JsonSerializer.Serialize(state,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}}));
        AutomationFiles.Write(Path.Combine(run,"post-endurance/target-plan.json"),settings.InstalledAppTests);
        var selected=AutomationAppStepRecovery.Select(new(settings,new('a',64)),"post-endurance",0);
        Assert.That(selected.State.Stage,Is.EqualTo(stage));Assert.That(selected.State.SchemaVersion,Is.EqualTo(schema));
    }
    [Test] public async Task ExplicitObservationMappingAlsoRetainsAdditionalCaseEvidence()
    {
        var request=await CaseRequest();
        File.WriteAllText(Path.Combine(context.RunDirectory,"installed-app/AndroidUI/a-extra.json"),"additional original evidence");
        request=request with{OriginalEvidenceSha256=AutomationAppStepRecovery.EvidenceHash(context.RunDirectory),CaseRecovery=request.CaseRecovery! with {
            Observations=[..request.CaseRecovery!.Observations,new("a-extra.json","Fixture.A")]}};
        Assert.That((await Replace(request,CaseRun)).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
        var accepted=AutomationFiles.Read<AutomationAppStepRecovery.Completion>(Path.Combine(context.RunDirectory,"installed-app/replacement.json"));
        Assert.That(accepted.ObservationSources!["a-extra.json"],Is.EqualTo("installed-app/AndroidUI/a-extra.json"));
    }

}
