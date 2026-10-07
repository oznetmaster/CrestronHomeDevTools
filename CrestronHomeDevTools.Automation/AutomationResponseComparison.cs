// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

public sealed record SubmissionAutomationResponsePair(string Name, string Before, string After);
public sealed record SubmissionAutomationResponseComparisonPlan(string RequirementId,
    SubmissionAutomationResponsePair[] Pairs, SubmissionResponseLimits? Limits = null, string? Assessment = null);

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
                !(p.Before.StartsWith("nunit/", StringComparison.Ordinal) || p.Before.StartsWith("installed-app/", StringComparison.Ordinal) ||
                    (settings.PreEnduranceTests != null && p.Before.StartsWith("pre-endurance/installed-app/", StringComparison.Ordinal)))) ||
            plan.Pairs.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != plan.Pairs.Length ||
            plan.Pairs.SelectMany(p => new[] { p.Before, p.After }).Distinct(StringComparer.Ordinal).Count() != plan.Pairs.Length * 2 ||
            AutomationFiles.Hash(review.Policy.Path) != review.Policy.Sha256)
            throw new InvalidDataException("Response comparison needs distinct initial/post-endurance producer files and an unchanged review policy.");
        SubmissionResponseComparison.ValidateLimits(plan.Limits);
        if (plan.Assessment != null && (plan.Limits != null || plan.Assessment != "performance-assessment/assessment.json" ||
            plan.Pairs.Any(p => p.Before == plan.Assessment || p.After == plan.Assessment)))
            throw new InvalidDataException("Use performance-assessment/assessment.json for the phase-two assessment, without numerical acceptance limits.");
        var policy = AutomationFiles.Read<SubmissionEvidencePolicy>(review.Policy.Path);
        var requirement = policy.Requirements.SingleOrDefault(r => r.Id == plan.RequirementId);
        if (policy.SchemaVersion != 1 || requirement is not { MinimumDuration: var duration,
            Execution: { Method: "combined", RequiredOutcome: SubmissionEvidenceOutcome.Passed, Restore: false, ResponseLimitSeconds: null, MaximumSampleGapSeconds: null } } || duration != TimeSpan.Zero)
            throw new InvalidDataException("Use a separate untimed comparison requirement, preserving the endurance duration requirement.");
        if (plan.Limits == null && plan.Assessment == null && !((review.PlannedGaps?.Any(g => g.RequirementId == plan.RequirementId) ?? false) || review.Declarations != null))
            throw new InvalidDataException("Without reviewed acceptance limits, declare this comparison partial rather than claiming unchanged performance.");
        return requirement;
    }

    internal static bool AwaitingAssessment(SubmissionWorkflowStepContext context, SubmissionAutomationSettings settings)
    {
        _ = Validate(settings);
        if (settings.ResponseComparison!.Assessment is not {} relative) return false;
        if (File.Exists(Path.Combine(context.RunDirectory, relative)))
        {
            if (!SubmissionEvidence.SafeEvidencePath(context.RunDirectory, relative, out _))
                throw new InvalidDataException("Unsafe performance assessment path.");
            return false;
        }
        if (File.Exists(Path.Combine(context.RunDirectory, Folder, "receipt.json")))
            throw new InvalidDataException("Retained performance assessment is missing.");
        return true;
    }

    internal static SubmissionEvidenceFile Prepare(SubmissionWorkflowStepContext context, SubmissionAutomationSettings settings, CancellationToken token)
    {
        var requirement = Validate(settings);
        AutomationPerformanceCapture.VerifyOtherCaptures(context);
        var plan = settings.ResponseComparison!;
        string root = context.RunDirectory;
        var afterFiles = AutomationPostEndurance.RetainedFiles(context).ToArray();
        bool installed = settings.InstalledAppTests != null;
        string receiptName = installed ? "installed-app-tests.json" : "windows-tests.json";
        if (installed && settings.PreEnduranceTests != null) receiptName = AutomationInitialAdditionalTests.ReceiptName;
        var stage = installed ? SubmissionWorkflowStage.AppTests : SubmissionWorkflowStage.WindowsTests;
        if (!context.Checkpoint.CompletedStages.TryGetValue(stage, out var initialReceipt) || initialReceipt.RelativePath != receiptName ||
            AutomationFiles.Hash(Path.Combine(root, receiptName)) != initialReceipt.Sha256)
            throw new InvalidDataException("The initial measurement producer must have an intact completed receipt.");
        if (installed) AutomationInstalledApp.VerifyRetained(root); else SubmissionAutomationStages.VerifyRetainedNUnit(root);
        if (settings.PreEnduranceTests != null) AutomationInitialAdditionalTests.VerifyRetained(context);
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
        if (envelope.EvidenceDirectory != AutomationEnduranceSelection.EvidenceDirectory(context) || envelope.Observation.Identity != identity ||
            envelope.Observation.Outcome != SubmissionEvidenceOutcome.Passed)
            throw new InvalidDataException("Completed interval belongs to another candidate or policy.");
        var inputs = new List<SubmissionWorkflowReceipt>(AutomationPerformanceCapture.FailedRetention(context));
        DateTimeOffset latestObservation = default;
        SubmissionResponseSeries Read(string relative)
        {
            token.ThrowIfCancellationRequested();
            relative = AutomationReview.ResolveObservationSource(relative, retained, root);
            if (!retained.TryGetValue(relative, out var hash) || !SubmissionEvidence.SafeEvidencePath(root, relative, out var path) ||
                new FileInfo(path).Length > 1024 * 1024 || AutomationFiles.Hash(path) != hash)
                throw new InvalidDataException("Measurement file is not intact retained producer evidence.");
            inputs.Add(new(relative, hash));
            var series = AutomationFiles.Read<SubmissionResponseSeries>(path);
            if (series.Measurements is { Length: > 0 })
                latestObservation = new[] { latestObservation, series.Measurements.Max(m => m.ObservedUtc) }.Max();
            return series;
        }
        var comparisons = plan.Pairs.Select(p => new { p.Name, p.Before, p.After, Report = SubmissionResponseComparison.Compare(
            Read(p.Before), Read(p.After), identity, envelope.Observation.StartedUtc, envelope.Observation.FinishedUtc, plan.Limits) }).ToArray();
        SubmissionEvidenceOutcome? assessedOutcome = null;
        if (plan.Assessment != null)
        {
            string assessmentPath = plan.Assessment;
            if (!SubmissionEvidence.SafeEvidencePath(root, assessmentPath, out var fullAssessment) ||
                new FileInfo(fullAssessment).Length > 1024 * 1024)
                throw new InvalidDataException("Performance assessment must be a bounded phase-two record.");
            string assessmentHash = AutomationFiles.Hash(fullAssessment);
            var assessment = AutomationFiles.Read<SubmissionPerformanceAssessment>(fullAssessment);
            assessedOutcome = SubmissionPerformanceReview.Assess(assessment, identity, requirement.Id,
                envelope.Observation.StartedUtc, envelope.Observation.FinishedUtc,
                comparisons.ToDictionary(p => p.Name, p => p.Report, StringComparer.Ordinal));
            if (assessment.ReviewedUtc < latestObservation)
                throw new InvalidDataException("Performance assessment predates the observations it assesses.");
            inputs.Add(new(assessmentPath, assessmentHash));
            foreach (var finding in assessment.Findings)
            {
                var pair = plan.Pairs.Single(p => p.Name == finding.Pair);
                foreach (var side in new[] { (Measurement: pair.Before, Files: finding.BeforeEvidence), (Measurement: pair.After, Files: finding.AfterEvidence) })
                {
                    string measurement = AutomationReview.ResolveObservationSource(side.Measurement, retained, root);
                    string prefix = measurement[..(measurement.LastIndexOf('/') + 1)];
                    foreach (var file in side.Files)
                    {
                        token.ThrowIfCancellationRequested();
                        if (side.Measurement == pair.After && file.RelativePath.StartsWith(AutomationPerformanceCapture.Folder + "/", StringComparison.Ordinal))
                        {
                            var support = AutomationPerformanceCapture.SupportingFile(context, settings, pair.Name, file);
                            var additional = AutomationFiles.Read<SubmissionResponseSeries>(Path.Combine(root, support.Series));
                            _ = SubmissionResponseComparison.Compare(Read(pair.Before), additional, identity,
                                envelope.Observation.StartedUtc, envelope.Observation.FinishedUtc);
                            if (additional.Measurements.Any(m => m.ObservedUtc > assessment.ReviewedUtc))
                                throw new InvalidDataException("Assessment predates its additional observations.");
                            inputs.AddRange(support.Files);
                            continue;
                        }
                        if (file.RelativePath == measurement || !file.RelativePath.StartsWith(prefix, StringComparison.Ordinal) ||
                            !retained.TryGetValue(file.RelativePath, out var hash) || hash != file.Sha256 ||
                            !SubmissionEvidence.SafeEvidencePath(root, file.RelativePath, out var path) || AutomationFiles.Hash(path) != hash)
                            throw new InvalidDataException("Performance findings need intact supporting evidence from the matching before/after producer, beyond timing series alone.");
                        inputs.Add(new(file.RelativePath, hash));
                    }
                }
            }
        }
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
        var outcome = assessedOutcome ?? (plan.Limits == null ? SubmissionEvidenceOutcome.Partial : comparisons.All(p => p.Report.WithinReviewedLimits == true)
            ? SubmissionEvidenceOutcome.Passed : SubmissionEvidenceOutcome.Failed);
        string rationale = "Compared matching device/control/request measurements before and after the retained endurance interval. " +
            (plan.Assessment != null ? "Phase-two reviewer assessed continued functionality, performance degradation, immediate functions and prompt accurate feedback against retained before/after evidence. No numerical acceptance threshold was imposed. " : plan.Limits == null ? "No reviewed acceptance limits were supplied; this is a partial comparison for review, not a no-degradation pass. " :
             "Assessed all explicit developer-reviewed limits; these are not thresholds prescribed by Crestron. " + plan.Limits.Rationale + " ") +
            "Observer overhead remains part of numerical measurements; those measurements alone do not establish physical relay latency, first visible app response, statistical equivalence or the required endurance duration.";
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

    internal static bool AcceptableForFinalization(SubmissionWorkflowStepContext context, SubmissionAutomationSettings settings)
    {
        var requirement=Validate(settings);
        var source=VerifyRetained(context);
        var document=AutomationFiles.Read<SubmissionEvidenceDocument>(Path.Combine(context.RunDirectory,source.RelativePath));
        var identity=new SubmissionEvidenceIdentity(settings.Release.PackageSha256,settings.Release.SourceCommit,settings.Review!.Policy.Sha256,settings.Review.Template.Sha256);
        if(document.SchemaVersion!=1 || document.Observations.Count!=1 || document.Observations[0].RequirementId!=requirement.Id ||
            document.Observations[0].Identity!=identity || document.Observations[0].Execution?.Target!=requirement.Execution!.Target)
            throw new InvalidDataException("Response comparison does not identify the configured final test.");
        // Missing reviewed limits already require an explicit partial-comparison declaration in Validate.
        // A measured failure never becomes acceptable through a gap declaration.
        return document.Observations[0].Outcome==(settings.ResponseComparison is { Limits:null, Assessment:null }
            ? SubmissionEvidenceOutcome.Partial : SubmissionEvidenceOutcome.Passed);
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
