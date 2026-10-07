// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DriverRemovalValidationTests
{
    private string folder = null!;
    private readonly DriverRemovalTarget target = new("processor", 100, -6, "Demo", "Platform", "2.1.1.0", 10);
    private static readonly DriverRemovalDevice Root = new(100, -6, "Demo", "Platform", 10, "2.1.001.0000", "Loaded");
    private static readonly DriverRemovalDevice Child = new(101, 100, "Demo light", "Wrapper", null, null, null);
    private static readonly DriverRemovalDevice Grandchild = new(102, 101, "Demo light", "Native", 10, null, null);
    private static readonly DriverRemovalDevice Other = new(200, -6, "House", "Other", 20, "1.0", "Loaded");
    private int removes, reads, logs;
    private DriverRemovalDevice[] current = null!;
    private Func<int, DriverRemovalDevice[]>? inventory;
    private bool beforePass, afterPass, home, evidence, throwOnRemove, newError;
    private Action? afterUiAction;

    [SetUp]
    public void SetUp()
    {
        folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "removal-" + Guid.NewGuid().ToString("N"));
        removes = reads = logs = 0; current = [Root, Child, Grandchild, Other]; inventory = null;
        beforePass = afterPass = home = evidence = true; throwOnRemove = newError = false; afterUiAction = null;
    }

    [TearDown]
    public void TearDown() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    private Task<DriverRemovalValidationResult> Run(TimeSpan? timeout = null) => DriverRemovalValidation.RunCoreAsync(
        target, folder, timeout ?? TimeSpan.FromSeconds(5), _ => Task.CompletedTask,
        (selected, after, directory, _) =>
        {
            Assert.That(selected.Select(d => d.Id), Is.EqualTo(new[] { 100, 101, 102 }));
            if (evidence) File.WriteAllText(Path.Combine(directory, "observed-ui.xml"), after ? "Home and Room absent" : "Home and Room present");
            if (after) afterUiAction?.Invoke();
            return Task.FromResult(new DriverRemovalUiOutcome(after ? afterPass : beforePass, home));
        },
        _ =>
        {
            int index = logs++;
            string body = "Notice: Log # time # Log started\nError: Old # time # Historical exception";
            if (index > 0 && newError) body += "\nError: Driver # time # Dispose exception";
            return Task.FromResult(ProcessorErrorLog.Parse("processor", "Persistent log contents during current boot:\n" + body + "\nCP4-R>", "CP4-R>",
                DateTimeOffset.UnixEpoch.AddSeconds(index * 2), DateTimeOffset.UnixEpoch.AddSeconds(index * 2 + 1)));
        },
        _ => { reads++; return Task.FromResult(inventory?.Invoke(reads) ?? current); },
        _ =>
        {
            removes++;
            if (throwOnRemove) throw new IOException("Connection lost after command; outcome unknown");
            current = [Other]; return Task.CompletedTask;
        }, CancellationToken.None);


    [Test]
    public async Task SuccessfulEarlierScanDoesNotConsumeLaterVerificationBudget()
    {
        int snapshots=0;var before=new[]{Root,Child,Grandchild,Other};
        var result=await DriverRemovalValidation.RunCoreAsync(target,folder,TimeSpan.FromMilliseconds(500),
            _=>Task.CompletedTask,async(selected,after,directory,token)=>{
                await Task.Delay(300,token);File.WriteAllText(Path.Combine(directory,"ui.xml"),after?"absent":"present");
                return new DriverRemovalUiOutcome(true,true);
            },token=>Task.FromResult(ProcessorErrorLog.Parse("processor","Persistent log contents during current boot:\nNotice: Log # time # Log started\nCP4-R>","CP4-R>",DateTimeOffset.UnixEpoch.AddSeconds(snapshots++*2),DateTimeOffset.UnixEpoch.AddSeconds(snapshots*2))),
            _=>Task.FromResult(removes==0?before:new[]{Other}),_=>{removes++;return Task.CompletedTask;},CancellationToken.None);
        Assert.That(result.Passed,Is.True);Assert.That(removes,Is.EqualTo(1));
    }

    [Test]
    public async Task CallerCancellationIsNotResetAtPhaseBoundary()
    {
        using var stop=new CancellationTokenSource();
        await Assert.CatchAsync<OperationCanceledException>(async()=>await DriverRemovalValidation.RunCoreAsync(target,folder,TimeSpan.FromSeconds(5),
            _=>Task.CompletedTask,(selected,after,directory,token)=>{
                File.WriteAllText(Path.Combine(directory,"ui.xml"),"present");stop.Cancel();return Task.FromResult(new DriverRemovalUiOutcome(true,true));
            },_=>throw new AssertionException("Cancelled phase must not read log"),_=>Task.FromResult(new[]{Root,Child,Grandchild,Other}),
            _=>{removes++;return Task.CompletedTask;},stop.Token));
        Assert.That(removes,Is.Zero);
    }

    [Test]
    public async Task IndividualSlowScanStillExpiresWithoutSendingRemoval()
    {
        await Assert.CatchAsync<OperationCanceledException>(async()=>await DriverRemovalValidation.RunCoreAsync(target,folder,TimeSpan.FromMilliseconds(60),
            _=>Task.CompletedTask,async(selected,after,directory,token)=>{await Task.Delay(2000,token);return new DriverRemovalUiOutcome(true,true);},
            _=>throw new AssertionException("Expired phase must not read log"),_=>Task.FromResult(new[]{Root,Child,Grandchild,Other}),
            _=>{removes++;return Task.CompletedTask;},CancellationToken.None));
        Assert.That(removes,Is.Zero);Assert.That(File.Exists(Path.Combine(folder,"result.json")),Is.False);
    }

    [Test]
    public async Task RemovesExactTreeOnceAndRetainsIndependentEvidence()
    {
        var result = await Run();
        Assert.That(result.Passed, Is.True); Assert.That(removes, Is.EqualTo(1));
        Assert.That(reads, Is.EqualTo(4));
        Assert.That(File.Exists(Path.Combine(folder, "final-inventory.json")), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(folder, "before-log.json")), Does.Contain("Historical exception"));
        Assert.That(File.Exists(Path.Combine(folder, "removal-intent.json")), Is.True);
    }

    [TestCase("ui")][TestCase("home")][TestCase("evidence")][TestCase("identity")][TestCase("changed")]
    public async System.Threading.Tasks.Task IncompletePreflightSendsNoRemoval (string reason)
    {
        if (reason == "ui") beforePass = false;
        if (reason == "home") home = false;
        if (reason == "evidence") evidence = false;
        if (reason == "identity") current = [Root with { Name = "Different" }, Child, Grandchild, Other];
        if (reason == "changed") inventory = i => i == 1 ? current : [Root, Child, Grandchild, Other with { LocationId = 30 }];
		await Assert.ThrowsAsync<InvalidDataException>(async () => await Run());
        Assert.That(removes, Is.Zero); Assert.That(File.Exists(Path.Combine(folder, "removal-intent.json")), Is.False);
    }

    [Test]
    public async System.Threading.Tasks.Task UncertainRemovalCannotBeReplayed ()
    {
        throwOnRemove = true;
		await Assert.ThrowsAsync<IOException>(async () => await Run());
        Assert.That(File.ReadAllText(Path.Combine(folder, "stopped.json")), Does.Contain("\"RemovalAttempted\":true"));
		await Assert.ThrowsAsync<InvalidOperationException>(async () => await Run());
        Assert.That(removes, Is.EqualTo(1));
    }

    [Test]
    public async System.Threading.Tasks.Task UnrelatedDeviceLossFailsEvenWhenTargetDisappears ()
    {
        inventory = _ => removes == 0 ? current : [];
		await Assert.ThrowsAsync<InvalidDataException>(async () => await Run());
        Assert.That(removes, Is.EqualTo(1));
    }

    [Test]
    public async System.Threading.Tasks.Task NewOrphanOfRemovedTreePreventsCompletion ()
    {
        inventory = _ => removes == 0 ? current : [Other, new(103, 101, "Orphan", "Native", 10, null, null)];
		await Assert.CatchAsync<OperationCanceledException>(async () => await Run(TimeSpan.FromMilliseconds(700)));
        Assert.That(removes, Is.EqualTo(1)); Assert.That(File.Exists(Path.Combine(folder, "result.json")), Is.False);
    }

    [Test]
    public async System.Threading.Tasks.Task InventoryIsCheckedAgainAfterUiObservation ()
    {
        afterUiAction = () => current = [Other with { LocationId = 40 }];
		await Assert.ThrowsAsync<InvalidDataException>(async () => await Run());
        Assert.That(File.Exists(Path.Combine(folder, "final-inventory.json")), Is.True);
    }

    [TestCase("ui")][TestCase("home")][TestCase("log")]
    public async Task RemovalAloneDoesNotPassTheChecklist(string failure)
    {
        if (failure == "ui") afterPass = false;
        if (failure == "home") afterUiAction = () => home = false;
        if (failure == "log") newError = true;
        var result = await Run();
        Assert.That(result.RemovalConfirmed, Is.True); Assert.That(result.Passed, Is.False);
        Assert.That(result.SafeToRelease, Is.EqualTo(failure != "home"));
    }
}
