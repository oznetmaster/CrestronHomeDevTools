// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

// Explicit continuation of ordinary postchecks, preserving successful case results.
// This is not a new all-pass TRX and does not authorize physical outage replay.
internal static class AutomationAppCaseRecovery
{
    internal sealed record Observation(string Path, string TestName);
    internal sealed record Plan(string Reason, SubmissionWorkflowReceipt OriginalTrx, Observation[] Observations);
    internal sealed record Provenance(string PreviousProducerPrefix, string ReceiptPath);
    internal sealed record Receipt(string Reason, string[] RequiredTests, SubmissionWorkflowReceipt OriginalTrx,
        SubmissionWorkflowReceipt ReplacementTrx, AutomationAppCaseResults.AcceptedCase[] Cases, Observation[] Observations);
    internal sealed record Prepared(AutomationAppCaseResults.Snapshot Original, string PreviousProducerPrefix);

    private static bool Relative(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        !path.Contains('\\') && !path.Contains(':') && path.Split('/').All(s => s is not ("" or "." or ".."));
    private static string[] RequiredPaths(string root, SubmissionAutomationSettings settings, AutomationAppStepRecovery.Request request)
    {
        string phase = "post-endurance/";
        string step = root.Replace('\\', '/').EndsWith("/installed-app/steps/" + request.Step.ToString("D3"), StringComparison.Ordinal)
            ? "installed-app/steps/" + request.Step.ToString("D3") + "/" : "";
        string prefix = phase + step + "installed-app/AndroidUI/";
        var review = settings.Review ?? throw new InvalidDataException("Case recovery requires the original observation contract.");
        return review.ObservationSources.Concat(review.PreEnduranceObservationSources ?? [])
            .Concat(settings.ResponseComparison?.Pairs.Select(p => p.After) ?? [])
            .Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).Select(p => p[prefix.Length..])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
    internal static Prepared Validate(string root, SubmissionAutomationSettings settings, AutomationAppStepRecovery.Request request)
    {
        var plan = request.CaseRecovery ?? throw new InvalidDataException("Missing case recovery plan.");
        var original = settings.InstalledAppTests!;
        if (request.Phase != "post-endurance" || request.ScopeRevision != null ||
            original.OperatorReadiness != null || request.Replacement.OperatorReadiness != null ||
            original.AndroidTests.ManagedChildren.Count != 0 || request.Replacement.AndroidTests.ManagedChildren.Count != 0 ||
            original.AndroidTests.RequiredTests is not { Count: > 1 } required ||
            string.IsNullOrWhiteSpace(plan.Reason) || plan.Reason.Length > 3000 || plan.Observations is not { Length: > 0 and <= 128 })
            throw new InvalidDataException("Case recovery requires an explicit applicability review of ordinary postchecks without physical prompts or provisioning.");
        // Permit only the outstanding case selection and the normal fixture-location repair.
        AutomationAppStepRecovery.ValidateReplacement(original, request with { Replacement = request.Replacement with {
            AndroidTests = request.Replacement.AndroidTests with { RequiredTests = required } } });
        var failed = AutomationAppStepRecovery.Failure(root, request.FailedOutcome);
        if (!failed.RestorationConfirmed || !failed.CleanupConfirmed || !failed.CandidateVerified || !failed.ReservationsReleased)
            throw new InvalidDataException("Individual passes require the original invocation's verified candidate and complete restoration.");
        string producer = request.FailedOutcome[..^"InstalledDriverTests.json".Length] + "AndroidUI/";
        if (!Relative(plan.OriginalTrx.RelativePath) || !plan.OriginalTrx.RelativePath.StartsWith(producer, StringComparison.Ordinal) ||
            plan.OriginalTrx.RelativePath[producer.Length..].Contains('/') || !plan.OriginalTrx.RelativePath.EndsWith(".trx", StringComparison.Ordinal) ||
            !SubmissionEvidence.SafeEvidencePath(root, plan.OriginalTrx.RelativePath, out var trx))
            throw new InvalidDataException("Select the original invocation's retained NUnit TRX.");
        if (Directory.GetFiles(Path.GetDirectoryName(trx)!, "*.trx").Length != 1)
            throw new InvalidDataException("Ambiguous original NUnit invocation.");
        var snapshot = AutomationAppCaseResults.Read(trx, plan.OriginalTrx.Sha256);
        var result = AutomationAppCaseResults.Evaluate(required.ToArray(), snapshot);
        if (result.Accepted.Length == 0 || result.Outstanding.Length == 0 || failed.Tests!.Passed != result.Accepted.Length ||
            failed.Tests.Failed != result.Outstanding.Length || failed.Tests.Skipped != 0 ||
            request.Replacement.AndroidTests.RequiredTests == null ||
            !result.Outstanding.SequenceEqual(request.Replacement.AndroidTests.RequiredTests.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("The replacement must select exactly the failed cases, preserving every original pass.");
        if (!SubmissionEvidence.SafeEvidencePath(root, producer + "discovery.dump", out var discovery))
            throw new InvalidDataException("Original NUnit discovery is missing.");
        using (var reader = XmlReader.Create(discovery, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20 * 1024 * 1024 }))
        {
            var cases = XDocument.Load(reader).Descendants("test-case").ToArray();
            foreach (string name in result.Outstanding)
            {
                var found = cases.Where(c => (string?)c.Attribute("fullname") == name).ToArray();
                if (found.Length != 1 || (string?)found[0].Attribute("runstate") != "Runnable")
                    throw new InvalidDataException("Explicit physical tests cannot use ordinary case recovery.");
            }
        }
        var expected = RequiredPaths(root, settings, request);
        if (plan.Observations.Select(o => o.Path).Distinct(StringComparer.Ordinal).Count() != plan.Observations.Length ||
            expected.Any(p => !plan.Observations.Any(o => o.Path == p)) ||
            required.Any(name => !plan.Observations.Any(o => o.TestName == name)) ||
            plan.Observations.Any(o => !Relative(o.Path) || !required.Contains(o.TestName, StringComparer.Ordinal)))
            throw new InvalidDataException("Map every retained observation and response measurement exactly once to its reviewed test case.");
        foreach (var observation in plan.Observations.Where(o => result.Accepted.Any(c => c.Name == o.TestName)))
            if (!SubmissionEvidence.SafeEvidencePath(root, producer + observation.Path, out _))
                throw new InvalidDataException("A retained passing case is missing its observation.");
        return new(snapshot, producer);
    }

    internal static AutomationAppStepRecovery.Completion Complete(string root, SubmissionAutomationSettings settings,
        AutomationAppStepRecovery.Request request, string attempt)
    {
        var prepared = Validate(root, settings, request);
        string output = Path.Combine(attempt, "installed-app"), android = Path.Combine(output, "AndroidUI");
        var result = AutomationFiles.Read<InstalledDriverTestResult>(Path.Combine(output, "InstalledDriverTests.json"));
        if (!result.Passed || !result.CandidateVerified || !result.ReservationsReleased)
            throw new InvalidDataException("Replacement cases did not pass with a verified candidate and restoration.");
        string[] trxs = Directory.GetFiles(android, "*.trx");
        if (trxs.Length != 1 || !SubmissionEvidence.SafeEvidencePath(root, Path.GetRelativePath(root, trxs[0]).Replace('\\', '/'), out var trx))
            throw new InvalidDataException("Replacement NUnit result is ambiguous or unsafe.");
        var next = AutomationAppCaseResults.Read(trx, AutomationFiles.Hash(trx));
        var required = settings.InstalledAppTests!.AndroidTests.RequiredTests!.ToArray();
        var combined = AutomationAppCaseResults.Evaluate(required, prepared.Original, next);
        if (combined.Outstanding.Length != 0 || result.Tests!.Passed != next.Cases.Length || result.Tests.Failed != 0 || result.Tests.Skipped != 0)
            throw new InvalidDataException("Not every original case has a verified passing execution.");
        string prefix = "installed-app/recovery-attempts/" + request.AttemptId + "/";
        string newProducer = prefix + "installed-app/AndroidUI/";
        var observations = request.CaseRecovery!.Observations.ToDictionary(o => o.Path, o =>
            (combined.Accepted.Single(c => c.Name == o.TestName).TrxSha256 == prepared.Original.TrxSha256 ? prepared.PreviousProducerPrefix : newProducer) + o.Path,
            StringComparer.Ordinal);
        foreach (var file in observations.Values)
            if (!SubmissionEvidence.SafeEvidencePath(root, file, out _)) throw new InvalidDataException("Selected case observation is missing.");
        string receipt = prefix + "case-recovery.json";
        AutomationFiles.Write(Path.Combine(root, receipt), new Receipt(request.CaseRecovery.Reason, required, request.CaseRecovery.OriginalTrx,
            new(Path.GetRelativePath(root, trx).Replace('\\', '/'), next.TrxSha256), combined.Accepted, request.CaseRecovery.Observations));
        return new(request.AttemptId, newProducer, request.OriginalEvidenceSha256,
            [newProducer, prepared.PreviousProducerPrefix], observations) { CaseRecovery = new(prepared.PreviousProducerPrefix, receipt) };
    }

    internal static string[] Accepted(AutomationAppStepRecovery.Completion accepted)
    {
        string prefix = "installed-app/recovery-attempts/" + accepted.AttemptId + "/";
        var proof = accepted.CaseRecovery!;
        string[] producers = [prefix + "installed-app/AndroidUI/", proof.PreviousProducerPrefix];
        if (accepted.RetainedProducerReceipts != null || accepted.ProducerPrefix != producers[0] ||
            accepted.ProducerPrefixes == null || !accepted.ProducerPrefixes.SequenceEqual(producers, StringComparer.Ordinal) ||
            !System.Text.RegularExpressions.Regex.IsMatch(proof.PreviousProducerPrefix, @"\Ainstalled-app/(?:recovery-attempts/[0-9a-f]{32}/installed-app/)?AndroidUI/\z") ||
            proof.PreviousProducerPrefix == producers[0] || proof.ReceiptPath != prefix + "case-recovery.json" ||
            accepted.ObservationSources is not { Count: > 0 } || accepted.ObservationSources.Any(o =>
                !Relative(o.Key) || !Relative(o.Value) || !producers.Any(p => o.Value == p + o.Key)))
            throw new InvalidDataException("Invalid retained-case producer provenance.");
        return producers;
    }
}
