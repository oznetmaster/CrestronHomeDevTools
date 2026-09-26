// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

public sealed record SubmissionAutomationResponsePair(string Name, string Before, string After);
public sealed record SubmissionAutomationResponseComparisonPlan(string RequirementId,
    SubmissionAutomationResponsePair[] Pairs, SubmissionResponseLimits? Limits = null);

internal static class AutomationResponseComparison
{
    private const string Folder = "response-comparison";
    private sealed record Envelope(string EvidenceDirectory, SubmissionObservation Observation);
    private sealed record Receipt(string InputSha256, SubmissionWorkflowReceipt[] Files);

    internal static SubmissionRequirement Validate(SubmissionAutomationSettings settings)
    {
        var plan = settings.ResponseComparison ?? throw new InvalidDataException("Response comparison is not configured.");
        var review = settings.Review ?? throw new InvalidDataException("Response comparison requires review configuration.");
        if (settings.PostEnduranceTests == null || settings.Endurance == null ||
            plan.Pairs is not { Length: > 0 and <= 32 } ||
            plan.Pairs.Any(p => p == null || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Before) || string.IsNullOrWhiteSpace(p.After) ||
                !p.After.StartsWith("post-endurance/installed-app/", StringComparison.Ordinal) ||
                !(p.Before.StartsWith("nunit/", StringComparison.Ordinal) || p.Before.StartsWith("installed-app/", StringComparison.Ordinal))) ||
            plan.Pairs.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != plan.Pairs.Length ||
            plan.Pairs.SelectMany(p => new[] { p.Before, p.After }).Distinct(StringComparer.Ordinal).Count() != plan.Pairs.Length * 2 ||
            AutomationFiles.Hash(review.Policy.Path) != review.Policy.Sha256)
            throw new InvalidDataException("Response comparison needs distinct initial/post-endurance producer files and an unchanged review policy.");
        SubmissionResponseComparison.ValidateLimits(plan.Limits);
        var policy = AutomationFiles.Read<SubmissionEvidencePolicy>(review.Policy.Path);
        var requirement = policy.Requirements.SingleOrDefault(r => r.Id == plan.RequirementId);
        if (policy.SchemaVersion != 1 || requirement is not { MinimumDuration: var duration,
            Execution: { Method: "combined", RequiredOutcome: SubmissionEvidenceOutcome.Passed, Restore: false, ResponseLimitSeconds: null, MaximumSampleGapSeconds: null } } || duration != TimeSpan.Zero)
            throw new InvalidDataException("Use a separate untimed comparison requirement, preserving the endurance duration requirement.");
        if (plan.Limits == null && !((review.PlannedGaps?.Any(g => g.RequirementId == plan.RequirementId) ?? false) || review.Declarations != null))
            throw new InvalidDataException("Without reviewed acceptance limits, declare this comparison partial rather than claiming unchanged performance.");
        return requirement;
    }

    internal static SubmissionEvidenceFile Prepare(SubmissionWorkflowStepContext context, SubmissionAutomationSettings settings, CancellationToken token)
    {
        var requirement = Validate(settings);
        var plan = settings.ResponseComparison!;
        string root = context.RunDirectory;
        var afterFiles = AutomationPostEndurance.RetainedFiles(context).ToArray();
        bool installed = settings.InstalledAppTests != null;
        string receiptName = installed ? "installed-app-tests.json" : "windows-tests.json";
        var stage = installed ? SubmissionWorkflowStage.AppTests : SubmissionWorkflowStage.WindowsTests;
        if (!context.Checkpoint.CompletedStages.TryGetValue(stage, out var initialReceipt) || initialReceipt.RelativePath != receiptName ||
            AutomationFiles.Hash(Path.Combine(root, receiptName)) != initialReceipt.Sha256)
            throw new InvalidDataException("The initial measurement producer must have an intact completed receipt.");
        if (installed) AutomationInstalledApp.VerifyRetained(root); else SubmissionAutomationStages.VerifyRetainedNUnit(root);
        using var initial = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, receiptName)));
        var retained = initial.RootElement.GetProperty("Files").EnumerateArray().ToDictionary(
            f => f.GetProperty("RelativePath").GetString()!.Replace('\\', '/'), f => f.GetProperty("Sha256").GetString()!, StringComparer.Ordinal);
        foreach (var file in afterFiles) if (!retained.TryAdd(file.RelativePath, file.Sha256)) throw new InvalidDataException("Measurement producer paths overlap.");
        string intervalPath = Path.Combine(root, "endurance-evidence.json");
        var intervalReceipt = context.Checkpoint.CompletedStages[SubmissionWorkflowStage.Endurance];
        if (intervalReceipt.RelativePath != "endurance-evidence.json" || AutomationFiles.Hash(intervalPath) != intervalReceipt.Sha256)
            throw new InvalidDataException("The completed interval differs from its retained stage receipt.");
        var envelope = AutomationFiles.Read<Envelope>(intervalPath);
        var identity = new SubmissionEvidenceIdentity(settings.Release.PackageSha256, settings.Release.SourceCommit, settings.Review!.Policy.Sha256, settings.Review.Template.Sha256);
        if (envelope.EvidenceDirectory != "endurance/observations" || envelope.Observation.Identity != identity ||
            envelope.Observation.Outcome != SubmissionEvidenceOutcome.Passed)
            throw new InvalidDataException("Completed interval belongs to another candidate or policy.");
        var inputs = new List<SubmissionWorkflowReceipt>();
        SubmissionResponseSeries Read(string relative)
        {
            token.ThrowIfCancellationRequested();
            if (!retained.TryGetValue(relative, out var hash) || !SubmissionEvidence.SafeEvidencePath(root, relative, out var path) ||
                new FileInfo(path).Length > 1024 * 1024 || AutomationFiles.Hash(path) != hash)
                throw new InvalidDataException("Measurement file is not intact retained producer evidence.");
            inputs.Add(new(relative, hash));
            return AutomationFiles.Read<SubmissionResponseSeries>(path);
        }
        var comparisons = plan.Pairs.Select(p => new { p.Name, p.Before, p.After, Report = SubmissionResponseComparison.Compare(
            Read(p.Before), Read(p.After), identity, envelope.Observation.StartedUtc, envelope.Observation.FinishedUtc, plan.Limits) }).ToArray();
        string folder = Path.Combine(root, Folder), receiptPath = Path.Combine(folder, "receipt.json");
        var binding = new { context.Checkpoint.InputSha256, Plan = plan, Endurance = intervalReceipt, Inputs = inputs };
        if (File.Exists(receiptPath))
        {
            var result = VerifyRetained(context);
            AutomationFiles.Write(Path.Combine(folder, "plan.json"), binding);
            return result;
        }
        if (Directory.Exists(folder)) throw new InvalidDataException("Inspect incomplete response comparison output before recovery.");
        Directory.CreateDirectory(folder);
        AutomationFiles.Write(Path.Combine(folder, "plan.json"), binding);
        AutomationFiles.Write(Path.Combine(folder, "comparison.json"), comparisons);
        var outcome = plan.Limits == null ? SubmissionEvidenceOutcome.Partial : comparisons.All(p => p.Report.WithinReviewedLimits == true)
            ? SubmissionEvidenceOutcome.Passed : SubmissionEvidenceOutcome.Failed;
        string rationale = "Compared matching device/control/request measurements before and after the retained endurance interval. " +
            (plan.Limits == null ? "No reviewed acceptance limits were supplied; this is a partial comparison for review, not a no-degradation pass. " :
             "Assessed all explicit developer-reviewed limits; these are not thresholds prescribed by Crestron. " + plan.Limits.Rationale + " ") +
            "Observer overhead remains part of the measurements. This does not establish physical relay latency, first visible app response, statistical equivalence or the required endurance duration.";
        var files = inputs.Select(p => new SubmissionEvidenceFile(p.RelativePath, p.Sha256)).Concat(new[] { "plan.json", "comparison.json" }
            .Select(name => new SubmissionEvidenceFile(Folder + "/" + name, AutomationFiles.Hash(Path.Combine(folder, name))))).ToArray();
        var observation = new SubmissionObservation(requirement.Id, identity, outcome, envelope.Observation.StartedUtc, DateTimeOffset.UtcNow,
            files, rationale, new(requirement.Execution!.Target, "combined"));
        AutomationReview.WriteDocument(Path.Combine(folder, "observations.json"), new SubmissionEvidenceDocument(1, [observation]));
        var all = files.Select(f => new SubmissionWorkflowReceipt(f.RelativePath, f.Sha256)).Append(
            new(Folder + "/observations.json", AutomationFiles.Hash(Path.Combine(folder, "observations.json")))).ToArray();
        AutomationFiles.Write(receiptPath, new Receipt(context.Checkpoint.InputSha256, all));
        return VerifyRetained(context);
    }

    internal static SubmissionEvidenceFile VerifyRetained(SubmissionWorkflowStepContext context)
    {
        var receipt = AutomationFiles.Read<Receipt>(Path.Combine(context.RunDirectory, Folder, "receipt.json"));
        if (receipt.InputSha256 != context.Checkpoint.InputSha256) throw new InvalidDataException("Response report belongs to another workflow.");
        foreach (var file in receipt.Files)
            if (!SubmissionEvidence.SafeEvidencePath(context.RunDirectory, file.RelativePath, out var path) || AutomationFiles.Hash(path) != file.Sha256)
                throw new InvalidDataException("Response comparison or its original measurements changed.");
        var observation = receipt.Files.Single(f => f.RelativePath == Folder + "/observations.json");
        return new(observation.RelativePath, observation.Sha256);
    }
}
