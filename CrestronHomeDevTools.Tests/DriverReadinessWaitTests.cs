// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
public sealed class DriverReadinessWaitTests
{
    private static DriverReadinessReport Report(params string[] reasons) =>
        new(DateTimeOffset.UtcNow, 42, [42], reasons.Select(r => new DriverReadinessIssue(42, "Example", r)).ToArray());
    [Test]
    public async Task TransitionalConfigurationIsRetainedBeforeReady()
    {
        var reports = new Queue<DriverReadinessReport>([Report("configuration-required-or-unconfirmed"), Report("device-offline-or-unconfirmed"), Report()]);
        var retained = new List<DriverReadinessReport>();
        var result = await DriverReadinessWait.WaitAsync(_ => Task.FromResult(reports.Dequeue()), retained.Add,
            TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1));
        Assert.That(result.Ready, Is.True);
        Assert.That(retained.Count, Is.EqualTo(3));
        Assert.That(retained[0].Ready, Is.False);
    }
    [Test]
    public async Task WrongIdentityStopsWithoutRetry()
    {
        int calls = 0;
        var result = await DriverReadinessWait.WaitAsync(_ => { calls++; return Task.FromResult(Report("root-model-changed")); }, _ => {},
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
        Assert.That(calls, Is.EqualTo(1)); Assert.That(result.Ready, Is.False); Assert.That(result.TimedOut, Is.False);
    }
    [Test]
    public async Task TimeoutPreservesLastUnreadyObservation()
    {
        var result = await DriverReadinessWait.WaitAsync(_ => Task.FromResult(Report("device-not-ready")), _ => {},
            TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(1));
        Assert.That(result.TimedOut, Is.True); Assert.That(result.Ready, Is.False); Assert.That(result.LastObservation, Is.Not.Null);
    }
    [Test]
    public async Task ExternalCancellationAndInspectionFailuresAreNotConvertedToReadiness()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await DriverReadinessWait.WaitAsync(_ => Task.FromResult(Report()), _ => {},
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), cancelled.Token));
        int calls=0;
        await Assert.ThrowsAsync<IOException>(async () => await DriverReadinessWait.WaitAsync(_ => { calls++; throw new IOException("fixture"); }, _ => {},
            TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1)));
        Assert.That(calls, Is.EqualTo(1)); await Task.CompletedTask;
    }
}
