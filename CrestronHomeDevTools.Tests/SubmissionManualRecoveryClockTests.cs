// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

// Synthetic timelines reproduce human-response delays without another hardware outage.
// These tests deliberately distinguish missing timing proof from a device failure.
[TestFixture]
public sealed class SubmissionManualRecoveryClockTests
{
    [TestCase(0, false)]
    [TestCase(24, true)]
    public void FortyTwoSecondConnectedDiagnosticDoesNotProveManualDeadline(double actionDelay, bool unproven)
    {
        WithEvidence((at, root, identity) => {
            var plan = Plan(identity);
            double restored = 80 + actionDelay;
            var record = new SubmissionOutageMeasurementRecord(1, identity,
                [new("processor", at(10, 11), at(80, restored))], null,
                [new("control", SubmissionEvidenceOutcome.Passed, at(restored + 8, restored + 42))],
                at(0, 1), at(500, 501), true);
            var report = SubmissionOutageMeasurements.Assess(plan, record, root, Epoch.AddSeconds(600));
            Assert.That(report.MaximumRecoverySeconds, Is.EqualTo(actionDelay + 42));
            Assert.That(report.Outcome, Is.EqualTo(unproven ? SubmissionEvidenceOutcome.Partial : SubmissionEvidenceOutcome.Passed));
            Assert.That(report.Issues.Contains("recovery-deadline-unproven"), Is.EqualTo(unproven));
            Assert.That(report.Issues, Does.Not.Contain("recovery-deadline-exceeded"));
        });
    }

    [Test]
    public void LateDoneCannotBeUsedAsAnExactRestorationTimeForEarlyMeasurements()
    {
        WithEvidence((at, root, identity) => {
            // Reconnect requested at80, connectivity observed at90, checks98..132,
            // operator clicksDone at140. Using[80,140] conflicts with the checks.
            var record = new SubmissionOutageMeasurementRecord(1, identity,
                [new("processor", at(10, 11), at(80, 140))], null,
                [new("control", SubmissionEvidenceOutcome.Passed, at(98, 132))],
                at(0, 1), at(500, 501), true);
            var report = SubmissionOutageMeasurements.Assess(Plan(identity), record, root, Epoch.AddSeconds(600));
            Assert.That(report.Outcome, Is.EqualTo(SubmissionEvidenceOutcome.Partial));
            Assert.That(report.Issues, Does.Contain("functional-evidence-not-after-restoration"));
        });
    }

    private static readonly DateTimeOffset Epoch = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static SubmissionOutageMeasurementPlan Plan(SubmissionEvidenceIdentity identity) =>
        new(identity, "system.network", ["processor"], ["control"], TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60), SubmissionOutageRecoveryClock.NetworkRestored);
    private static void WithEvidence(Action<Func<double, double, SubmissionOutageCapture>, string, SubmissionEvidenceIdentity> check)
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "manual-clock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            byte[] bytes = "Synthetic timing only; not physical-test evidence."u8.ToArray();
            File.WriteAllBytes(Path.Combine(root, "synthetic.txt"), bytes);
            var proof = new SubmissionEvidenceFile("synthetic.txt", Convert.ToHexStringLower(SHA256.HashData(bytes)));
            check((first,last) => new(Epoch.AddSeconds(first), Epoch.AddSeconds(last), proof), root,
                new(new('a',64), new('b',40), new('c',64), new('d',64)));
        } finally { Directory.Delete(root, true); }
    }
}
