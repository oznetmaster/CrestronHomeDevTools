// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ProcessorCloudBackupsTests
{
    private static readonly TimeSpan Timeout=TimeSpan.FromSeconds(1);
    private static CloudConfigurationBackup Entry(string id="one",string date="2026-10-26T12:00:00",string type="Normal")
        =>new(id,$"configuration-{id}.zip",date,"4.012.0194","Passed",type);
    private static JsonElement ListEvent(string id,params CloudConfigurationBackup[] rows)
        =>JsonSerializer.SerializeToElement(new{EventType="cp.processorBackupRestore:configurationListChanged",OperationId=id,ConfigurationList=rows});
    private static JsonElement Status(string id,object status,string type="cp.processorBackupRestore:backupStatusChanged")
        =>JsonSerializer.SerializeToElement(new{EventType=type,OperationId=id,LatestStatus=status});
    [Test]
    public async Task TypedListRequestsOwnBackupContextAndKeepsDatesVerbatim()
    {
        var connection=new Fake();var result=await ProcessorCloudBackups.ListAsync(connection,10,Timeout);
        Assert.That(result.Configurations.Single().Date,Is.EqualTo("2026-10-26T12:00:00"));
        Assert.That(connection.Sent.Single().Command,Is.EqualTo(ProcessorCloudBackups.ListCommand));
        Assert.That(connection.Sent.Single().Parameters.GetProperty("deployCode").GetString(),Is.Empty);
        Assert.That(connection.Saves,Is.Zero);
    }
    [TestCase(CloudConfigurationBackupKind.Normal,"backupConfiguration","Normal")]
    [TestCase(CloudConfigurationBackupKind.Golden,"backupGoldenConfiguration","Golden")]
    public async Task ExplicitSaveKindUsesCorrectWireValueAndVerifiesNewMatchingEntry(CloudConfigurationBackupKind kind,string wire,string type)
    {
        var connection=new Fake{After=[Entry("new",type:type)]};
        var result=await ProcessorCloudBackups.SaveAsync(connection,10,kind,Timeout);
        Assert.That(result.BackendSucceeded,Is.True);Assert.That(result.NewEntryVerified,Is.True);
        Assert.That(connection.Sent.Single(c=>c.Command==ProcessorCloudBackups.SaveCommand).Parameters.GetProperty("operation").GetString(),Is.EqualTo(wire));
        Assert.That(connection.Saves,Is.EqualTo(1));Assert.That(connection.Lists,Is.EqualTo(2));
        Assert.That(connection.Sent.Select(c=>c.Command),Is.EqualTo(new[]{ProcessorCloudBackups.ListCommand,ProcessorCloudBackups.SaveCommand,ProcessorCloudBackups.ListCommand}));
    }
    [TestCase("absent")] [TestCase("ambiguous")] [TestCase("wrong-kind")]
    public async Task BackendSuccessAloneDoesNotVerifyAListedBackup(string mode)
    {
        var connection=new Fake{After=mode switch{
            "absent"=>[Entry()],"ambiguous"=>[Entry("a"),Entry("b")],_=>[Entry("a",type:"Golden")]}};
        var result=await ProcessorCloudBackups.SaveAsync(connection,10,CloudConfigurationBackupKind.Normal,Timeout);
        Assert.That(result.NewEntryVerified,Is.False);Assert.That(connection.Saves,Is.EqualTo(1));
    }
    [Test]
    public async Task GoldenReplacementCanKeepItsIdButHaveUpdatedMetadata()
    {
        var connection=new Fake{Before=[Entry(type:"Golden")],After=[Entry(date:"2026-10-26T12:05:00",type:"Golden")]};
        Assert.That((await ProcessorCloudBackups.SaveAsync(connection,10,CloudConfigurationBackupKind.Golden,Timeout)).NewEntryVerified,Is.True);
    }
    [Test]
    public async Task RenumberedIdsDoNotMakeExistingBackupsAppearNew()
    {
        var existing=Entry("old");var added=Entry("new") with{Filename="new-configuration.zip"};
        var connection=new Fake{Before=[existing],After=[existing with{ConfigurationId="renumbered"},added]};
        var result=await ProcessorCloudBackups.SaveAsync(connection,10,CloudConfigurationBackupKind.Normal,Timeout);
        Assert.That(result.NewEntryVerified,Is.True);Assert.That(result.NewOrUpdatedEntries.Single(),Is.EqualTo(added));
    }
    [Test]
    public async Task IdChangesAloneDoNotVerifyASave()
    {
        var connection=new Fake{After=[Entry() with{ConfigurationId="renumbered"}]};
        Assert.That((await ProcessorCloudBackups.SaveAsync(connection,10,CloudConfigurationBackupKind.Normal,Timeout)).NewEntryVerified,Is.False);
    }
    [TestCase("missing-save")] [TestCase("missing-list")] [TestCase("wrong-identity")]
    public async Task MissingCapabilitiesOrChangedIdentityPreventSave(string mode)
    {
        var fake=new Fake{Mode=mode};await Assert.ThrowsAsync<NotSupportedException>(async()=>await ProcessorCloudBackups.SaveAsync(fake,10,CloudConfigurationBackupKind.Normal,Timeout));
        Assert.That(fake.Saves,Is.Zero);
    }
    [TestCase("submit-timeout")] [TestCase("wait-failure")]
    public async Task UncertainSaveIsNeverAutomaticallyRetried(string mode)
    {
        var fake=new Fake{Mode=mode};await Assert.ThrowsAsync<IOException>(async()=>await ProcessorCloudBackups.SaveAsync(fake,10,CloudConfigurationBackupKind.Normal,Timeout));
        Assert.That(fake.Saves,Is.EqualTo(1));Assert.That(fake.Lists,Is.EqualTo(1));
    }
    [Test]
    public async Task UndefinedKindCannotSubmitAnything()
    {
        var fake=new Fake();await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async()=>await ProcessorCloudBackups.SaveAsync(fake,10,(CloudConfigurationBackupKind)99,Timeout));
        Assert.That(fake.Sent,Is.Empty);
    }
    [Test]
    public async Task ListEventsCanArriveBeforeWaitAndEndedCanPrecedeList()
    {
        var early=new CloudBackupTracker();early.Accept(ListEvent("a",Entry()));early.Accept(Status("a","Ended"));
        Assert.That(await early.WaitForList("a",Timeout,default),Has.Count.EqualTo(1));
        var reordered=new CloudBackupTracker();reordered.Accept(Status("b","Ended"));var wait=reordered.WaitForList("b",Timeout,default);
        Assert.That(wait.IsCompleted,Is.False);reordered.Accept(ListEvent("b"));Assert.That(await wait,Is.Empty);
    }
    [TestCase("Succeeded")] [TestCase(2)]
    public async Task DedicatedBackupSuccessUsesItsOwnEnumMapping(object status)
    {
        var tracker=new CloudBackupTracker();tracker.Accept(Status("a",status));tracker.Accept(Status("a","Ended"));await tracker.WaitForSave("a",Timeout,default);
    }
    [Test]
    public async Task EndedDoesNotMeanSaveSucceeded()
    {
        var tracker=new CloudBackupTracker();tracker.Accept(Status("a","Ended"));
        await Assert.ThrowsAsync<IOException>(async()=>await tracker.WaitForSave("a",Timeout,default));
    }
    [TestCase("Failed")] [TestCase("UnzipFailed")] [TestCase(0)] [TestCase(1)]
    public async Task FailureCannotBeOverwrittenByLaterSuccessOrEnded(object status)
    {
        var tracker=new CloudBackupTracker();tracker.Accept(Status("a",status));tracker.Accept(Status("a","Succeeded"));tracker.Accept(Status("a","Ended"));
        await Assert.ThrowsAsync<CloudBackupOperationException>(async()=>await tracker.WaitForSave("a",Timeout,default));
    }
    [Test]
    public async Task GenericStatusTwoMeansFailureInsteadOfBackupStatusTwoSuccess()
    {
        var tracker=new CloudBackupTracker();tracker.Accept(Status("a",2,"cp.types:operationStatusChanged"));tracker.Accept(Status("a",2));
        await Assert.ThrowsAsync<CloudBackupOperationException>(async()=>await tracker.WaitForSave("a",Timeout,default));
    }
    [Test]
    public async Task FailureRetainsTheExactCorrelatedDiagnosticEventAfterItsDocumentIsDisposed()
    {
        var tracker=new CloudBackupTracker();
        using(var document=JsonDocument.Parse("""{"EventType":"cp.types:operationStatusChanged","OperationId":"save-a","LatestStatus":"Failed","Message":"example backend diagnostic","SubsystemCode":"example-code"}"""))
            tracker.Accept(document.RootElement);
        var error=await Assert.ThrowsAsync<CloudBackupOperationException>(async()=>await tracker.WaitForSave("save-a",Timeout,default));
        Assert.That(error!.OperationId,Is.EqualTo("save-a"));
        Assert.That(error.DiagnosticEvent.GetProperty("Message").GetString(),Is.EqualTo("example backend diagnostic"));
        Assert.That(error.DiagnosticEvent.GetProperty("SubsystemCode").GetString(),Is.EqualTo("example-code"));
        Assert.That(error.Message,Does.Not.Contain("example backend diagnostic"));
    }
    [Test]
    public async Task CapturedSuccessfulSaveSequencePreservesSuccessThroughItsAutomaticListRefresh()
    {
        var tracker=new CloudBackupTracker();
        foreach(string state in new[]{"Started","Succeeded","Started"})
        {
            tracker.Accept(Status("save-a",state,"cp.types:operationStatusChanged"));
            tracker.Accept(Status("save-a",state));
        }
        tracker.Accept(ListEvent("save-a",Entry()));
        tracker.Accept(Status("save-a","Ended","cp.types:operationStatusChanged"));
        tracker.Accept(Status("save-a","Ended"));
        await tracker.WaitForSave("save-a",Timeout,default);
        Assert.That(await tracker.WaitForList("save-a",Timeout,default),Has.Count.EqualTo(1));
    }
    [Test]
    public async Task SaveWaitsForAutomaticRefreshToEndAfterUploadSuccess()
    {
        var tracker=new CloudBackupTracker();var wait=tracker.WaitForSave("save-a",Timeout,default);
        tracker.Accept(Status("save-a","Succeeded"));
        tracker.Accept(Status("save-a","Started"));
        tracker.Accept(ListEvent("save-a",Entry()));
        Assert.That(wait.IsCompleted,Is.False);
        tracker.Accept(Status("save-a","Ended","cp.types:operationStatusChanged"));
        Assert.That(wait.IsCompleted,Is.False);
        tracker.Accept(Status("save-a","Ended"));
        await wait;
    }
    [Test]
    public async Task OtherOperationsCannotCompleteTheSelectedWait()
    {
        var tracker=new CloudBackupTracker();tracker.Accept(ListEvent("other",Entry()));tracker.Accept(Status("other","Ended"));
        await Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(),async()=>await tracker.WaitForList("selected",TimeSpan.FromMilliseconds(20),default));
    }
    [Test]
    public async Task MalformedListFailsInsteadOfReturningAnEmptySuccess()
    {
        var tracker=new CloudBackupTracker();tracker.Accept(JsonSerializer.SerializeToElement(new{EventType="cp.processorBackupRestore:configurationListChanged",OperationId="a",ConfigurationList=new[]{new{Filename="missing-id"}}}));
        tracker.Accept(Status("a","Ended"));await Assert.ThrowsAsync<InvalidDataException>(async()=>await tracker.WaitForList("a",Timeout,default));
    }
    [Test]
    public async Task ConnectionFailureUnblocksWaitWithoutClaimingSuccess()
    {
        var tracker=new CloudBackupTracker();var wait=tracker.WaitForSave("a",Timeout,default);tracker.Fail(new IOException("closed"));
        await Assert.ThrowsAsync<IOException>(async()=>await wait);
    }
    [Test]
    public async Task BoundOldCompletedEventsWithoutDiscardingCurrentOperation()
    {
        var tracker=new CloudBackupTracker();
        for(int i=0;i<300;i++){tracker.Accept(ListEvent(i.ToString(),Entry()));tracker.Accept(Status(i.ToString(),"Ended"));}
        Assert.That(await tracker.WaitForList("299",Timeout,default),Has.Count.EqualTo(1));
    }
    private sealed class Fake:IConfigurationConnection,ICloudBackupEventSource
    {
        internal string? Mode;internal int Saves,Lists;
        internal CloudConfigurationBackup[] Before=[Entry()],After=[Entry("new")];
        internal readonly List<(string Command,JsonElement Parameters)> Sent=[];
        public Task<T?> GetAsync<T>(string path,CancellationToken cancellationToken=default)
        {
            var commands=new[]{ProcessorCloudBackups.ListCommand,ProcessorCloudBackups.SaveCommand}.Where(c=>!(Mode=="missing-save"&&c==ProcessorCloudBackups.SaveCommand||Mode=="missing-list"&&c==ProcessorCloudBackups.ListCommand)).ToArray();
            object device=new DeviceInfo{Id=Mode=="wrong-identity"?11:10,Commands=commands};return Task.FromResult((T?)device);
        }
        public Task<T?> ExecuteAsync<T>(int deviceId,string commandName,object? parameters=null,CancellationToken cancellationToken=default)
        {
            Sent.Add((commandName,JsonSerializer.SerializeToElement(parameters)));
            if(commandName==ProcessorCloudBackups.SaveCommand){Saves++;if(Mode=="submit-timeout")throw new IOException("Acknowledgement lost");}
            else Lists++;
            return Task.FromResult((T?)(object)(commandName==ProcessorCloudBackups.SaveCommand?"save":"list-"+Lists));
        }
        public Task<IReadOnlyList<CloudConfigurationBackup>> WaitForCloudConfigurationListAsync(string operationId,TimeSpan timeout,CancellationToken token=default)
            =>Task.FromResult<IReadOnlyList<CloudConfigurationBackup>>(Lists==1?Before:After);
        public Task WaitForCloudConfigurationSaveAsync(string operationId,TimeSpan timeout,CancellationToken token=default)
        {if(Mode=="wait-failure")throw new IOException("Outcome unknown");return Task.CompletedTask;}
        public Task<OperationResult> WaitForOperationAsync(string operationId,TimeSpan timeout,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
