// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ProcessorErrorLogTests
{
    private const string Started = "Notice: crestErrorLogServer # 2026-09-26 08:00:00 # Log started";
    private static ProcessorErrorLogSnapshot Snapshot(string body, int second = 0) => ProcessorErrorLog.Parse("processor",
        "\r\nPersistent log contents during current boot:\r\n" + body + "\r\nCP4-R>", "CP4-R>",
        DateTimeOffset.UnixEpoch.AddSeconds(second), DateTimeOffset.UnixEpoch.AddSeconds(second + 1));

    [Test]
    public void ProcessorEndMarkerIsNotPartOfTheLastPersistentEntry()
    {
        const string footer = "\nPersistent log contents during current boot end";
        var before = Snapshot(Started + footer);
        var after = Snapshot(Started + "\nNotice: ctpd # time # SHELL Connection" + footer, 2);
        Assert.That(ProcessorErrorLog.Compare(before, after).NoNewErrorsOrExceptions, Is.True);
        Assert.That(before.Entries, Is.EqualTo(new[] { Started }));
        var malformed = Snapshot(Started + footer + "\nError: App # time # Unexpected trailing output", 2);
        Assert.That(ProcessorErrorLog.Compare(before, malformed).Comparable, Is.False);
    }

    [Test]
    public void HistoricalErrorsDoNotBecomeNewRemovalErrors()
    {
        string original = Started + "\nError: App # 2026-09-26 08:01:00 # Historical exception\n  at Example.Method()";
        var before = Snapshot(original);
        var after = Snapshot(original + "\nOk: App # 2026-09-26 08:02:00 # Driver unloaded", 2);
        var result = ProcessorErrorLog.Compare(before, after);
        Assert.That(result.Comparable && result.NoNewErrorsOrExceptions, Is.True);
        Assert.That(result.NewEntries, Has.Length.EqualTo(1));
        Assert.That(before.Entries[1], Does.Contain("at Example.Method()"));
    }

    [TestCase("Error: App # time # Unload failed")]
    [TestCase("Fatal: App # time # Failure")]
    [TestCase("Notice: App # time # System.InvalidOperationException exception\n  at Example.Dispose()")]
    public void NewFailuresRemainAttributedOnlyToTheInterval(string message)
    {
        var result = ProcessorErrorLog.Compare(Snapshot(Started), Snapshot(Started + "\n" + message, 2));
        Assert.That(result.Comparable, Is.True); Assert.That(result.NoNewErrorsOrExceptions, Is.False);
        Assert.That(result.ConcernLines, Is.EqualTo(new[] { message }));
    }

    [TestCase("reset")][TestCase("truncated")][TestCase("changed")]
    public void LostOrChangedBaselineCannotPass(string change)
    {
        var before = Snapshot(Started + "\nOk: App # time # Existing");
        var after = Snapshot(change switch { "reset" => Started.Replace("08:00:00", "09:00:00"),
            "truncated" => "Ok: App # time # Existing", _ => Started + "\nOk: App # time # Modified" }, 2);
        Assert.That(ProcessorErrorLog.Compare(before, after).Comparable, Is.False);
    }

    [Test]
    public void LiveConsoleOutputDoesNotAlterPersistentPrefixButIsRetained()
    {
        var before = Snapshot(Started + "\n2026-09-26 08:01:00.100 [ 1] [INFO] live old message");
        var after = Snapshot(Started + "\n2026-09-26 08:02:00.100 [ 1] [ERROR] live new error", 2);
        var result = ProcessorErrorLog.Compare(before, after);
        Assert.That(before.Entries, Has.Length.EqualTo(1)); Assert.That(before.ConsoleLines, Has.Length.EqualTo(1));
        Assert.That(result.Comparable, Is.True); Assert.That(result.NoNewErrorsOrExceptions, Is.False);
    }

    [TestCase(false)][TestCase(true)]
    public void SuspensionAtStartOrDuringIntervalDoesNotPass(bool earlier)
    {
        string stop = "\nWarning: PersistentLog # time # Consecutive error states detected; logging is suspended";
        var before = Snapshot(Started + (earlier ? stop : ""));
        var after = Snapshot(Started + stop + "\nNotice: PersistentLog # time # logging is resumed", 2);
        Assert.That(ProcessorErrorLog.Compare(before, after).Comparable, Is.False);
    }

    [Test]
    public void OldSuspensionFollowedByResumeDoesNotInvalidateLaterIntervals()
    {
        string original = Started + "\nWarning: PersistentLog # time # logging is suspended\nNotice: PersistentLog # time # logging is resumed";
        Assert.That(ProcessorErrorLog.Compare(Snapshot(original), Snapshot(original, 2)).NoNewErrorsOrExceptions, Is.True);
    }

    [Test]
    public void MissingHeaderPartialReplyEmptyBaselineAndUnknownPreambleDoNotPass()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<InvalidDataException>(() => ProcessorErrorLog.Parse("processor", "Unknown command\nCP4-R>", "CP4-R>", now, now));
        Assert.Throws<InvalidDataException>(() => ProcessorErrorLog.Parse("processor", "Persistent log contents during current boot:\n" + Started, "CP4-R>", now, now));
        Assert.That(ProcessorErrorLog.Compare(Snapshot(""), Snapshot(Started, 2)).Comparable, Is.False);
        var unknown = ProcessorErrorLog.Parse("processor", "Unexpected output\nPersistent log contents during current boot:\n" + Started + "\nCP4-R>", "CP4-R>", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        Assert.That(ProcessorErrorLog.Compare(unknown, Snapshot(Started, 2)).Comparable, Is.False);
    }

    [Test]
    public void DifferentProcessorOrOverlappingObservationsAreRejected()
    {
        var before = Snapshot(Started); var after = Snapshot(Started, 2);
        Assert.Throws<ArgumentException>(() => ProcessorErrorLog.Compare(before, after with { Host = "another" }));
        Assert.Throws<ArgumentException>(() => ProcessorErrorLog.Compare(before, after with { RequestSentUtc = before.RequestSentUtc }));
    }

    private sealed class Session(params string[] chunks) : IUptimeSession
    {
        private readonly Queue<string> output = new(chunks);
        public List<string> Commands { get; } = [];
        public bool IsConnected => true;
        public string ReadAvailable() => Commands.Count == 0 ? "Old banner\nCP4-R>" : output.TryDequeue(out var chunk) ? chunk : "";
        public void WriteLine(string command) => Commands.Add(command);
    }

    [Test]
    public async Task FragmentedReadSendsOnlyTheDocumentedReadCommand()
    {
        var session = new Session("\r\nPersistent log contents during current ", "boot:\r\n" + Started, "\r\nCP4-R>");
        var result = await ProcessorErrorLog.ReadCoreAsync(session, "processor", TimeSpan.FromSeconds(2), default);
        Assert.That(result.Entries, Is.EqualTo(new[] { Started }));
        Assert.That(session.Commands, Is.EqualTo(new[] { "err plogcurrent" }));
    }

    [Test]
    public void OversizedReadStopsWithoutRetryOrLogClearing()
    {
        var session = new Session(new string('x', 2 * 1024 * 1024 + 1));
        Assert.ThrowsAsync<InvalidDataException>(async () => await ProcessorErrorLog.ReadCoreAsync(session, "processor", TimeSpan.FromSeconds(2), default));
        Assert.That(session.Commands, Is.EqualTo(new[] { "err plogcurrent" }));
    }
}
