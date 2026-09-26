// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using CrestronHomeNUnit.Android;

namespace CrestronHomeDevTools.Automation;

/// <summary>Opt-in final removal of this run's candidate after its post-endurance checks.</summary>
public sealed record SubmissionAutomationRemovalPlan(DriverRemovalAppPlan App, string RequirementId,
    string? PlacementRequirementId = null);

internal static class AutomationRemoval
{
    private sealed record Intent(string InputSha256, string OperationId, SubmissionWorkflowReceipt Endurance,
        string PostEnduranceSha256, DriverRemovalWorkflowPlan Plan, DateTimeOffset StartedUtc);
    private sealed record Receipt(string InputSha256, SubmissionWorkflowReceipt[] Files);
    internal const string ObservationPath = "removal/observations.json";

    internal static SubmissionRequirement Validate(SubmissionAutomationSettings settings)
    {
        var plan = settings.Removal ?? throw new InvalidDataException("Removal plan is missing.");
        if (settings.PostEnduranceTests == null || !settings.PostEnduranceFromDeployment || settings.Review == null ||
            settings.NUnit.ActualDriver == null || settings.NUnit.ReleaseCandidate == null || settings.Endurance == null)
            throw new InvalidDataException("Final removal requires candidate deployment, post-endurance checks and a review policy.");
        var review = settings.Review;
        if (AutomationFiles.Hash(review.Policy.Path) != review.Policy.Sha256) throw new InvalidDataException("Removal policy changed.");
        var policy = AutomationFiles.Read<SubmissionEvidencePolicy>(review.Policy.Path);
        if (policy.SchemaVersion != 1) throw new InvalidDataException("Unsupported removal evidence policy.");
        var requirements = policy.Requirements.Where(r => r.Id == plan.RequirementId).ToArray();
        if (requirements.Length != 1 || requirements[0] is not { MinimumDuration: var duration, Execution: { Method: "combined", RequiredOutcome: SubmissionEvidenceOutcome.Passed, Restore: false, ResponseLimitSeconds: null, MaximumSampleGapSeconds: null } } || duration != TimeSpan.Zero)
            throw new InvalidDataException("Removal must bind one combined API/app/log requirement without unrelated timing or restoration claims.");
        if (plan.PlacementRequirementId is {} placement)
        {
            var matches = policy.Requirements.Where(r => r.Id == placement).ToArray();
            if (placement == plan.RequirementId || matches.Length != 1 ||
                matches[0] is not { MinimumDuration: var minimum, Execution: { Method: "combined", RequiredOutcome: SubmissionEvidenceOutcome.Passed, Restore: false, ResponseLimitSeconds: null, MaximumSampleGapSeconds: null } } || minimum != TimeSpan.Zero)
                throw new InvalidDataException("Placement requires a distinct untimed combined membership requirement; it does not prove glyphs or controls.");
        }
        var configured = AndroidWorkflowSession.Read<AndroidSessionProfile>(settings.PostEnduranceTests.AndroidTests.ProfilePath);
        if (configured != plan.App.Profile) throw new InvalidDataException("Removal must observe the same Android session as the preceding checks.");
        return requirements[0];
    }

    private static DriverRemovalWorkflowPlan Resolve(SubmissionWorkflowStepContext c, SubmissionAutomationSettings settings)
    {
        var deployed = AutomationDeploymentEvidence.Read(c, settings);
        var resolved = AutomationPostEndurance.Resolve(c, settings).InstalledAppTests!;
        if (resolved.Target.DeviceId != deployed.Installed.DeviceId) throw new InvalidDataException("Removal deployment binding differs.");
        return new(settings.NUnit.Host, settings.NUnit.CertificateSha256, settings.NUnit.SshFingerprint,
            Path.Combine(c.RunDirectory, "candidate.pkg"), settings.Release.PackageSha256, resolved.Target,
            BindManagedApp(settings.Removal!.App, deployed.Installed.DeviceId,
                settings.ManagedDevices == null ? [] : AutomationManagedDevices.VerifyRetained(c)));
    }

    internal static DriverRemovalAppPlan BindManagedApp(DriverRemovalAppPlan app, int platformId, SubmissionManagedChildBinding[] bindings)
    {
        SubmissionManagedChildBinding Find(string alias) => bindings.SingleOrDefault(b => b.Alias == alias)
            ?? throw new InvalidDataException("Removal references an unknown managed-child alias.");
        var tiles = app.Tiles.Select(tile =>
        {
            if (tile.ManagedAlias == null) return tile;
            var bound = Find(tile.ManagedAlias);
            if (tile.Name != bound.Request.Name || tile.LocationId != bound.Request.LocationId)
                throw new InvalidDataException("Removal tile expectations differ from the commissioned child.");
            int id = tile.NativeLight ? bound.NativeLoadId ?? throw new InvalidDataException("Removal expects an unavailable native load.") : bound.DeviceId;
            return tile with { DeviceId = id, ManagedAlias = null };
        }).ToArray();
        var nonvisual = app.NonvisualDeviceIds.Concat(app.NonvisualManagedAliases.Select(a => Find(a).DeviceId));
        if (app.IncludeDeployedPlatformAsNonvisual) nonvisual = nonvisual.Append(platformId);
        return app with { Tiles = tiles, NonvisualDeviceIds = nonvisual.ToArray(), NonvisualManagedAliases = [], IncludeDeployedPlatformAsNonvisual = false };
    }

    internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext c, SubmissionAutomationSettings settings,
        Func<string, NetworkCredential> credentials, CancellationToken token,
        Func<DriverRemovalWorkflowPlan, NetworkCredential, string, CancellationToken, Task<DriverRemovalWorkflowResult>>? run = null)
    {
        var requirement = Validate(settings);
        AutomationPostEndurance.VerifyRetained(c);
        if (!c.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.Endurance, out var endurance) ||
            !SubmissionEvidence.SafeEvidencePath(c.RunDirectory, endurance.RelativePath, out var endurancePath) || AutomationFiles.Hash(endurancePath) != endurance.Sha256)
            throw new InvalidDataException("Removal requires completed unchanged endurance evidence.");
        string postHash = AutomationFiles.Hash(Path.Combine(c.RunDirectory, "post-endurance", "installed-app-tests.json"));
        string folder = Path.Combine(c.RunDirectory, "removal"), intentPath = Path.Combine(folder, "intent.json");
        if (File.Exists(Path.Combine(c.RunDirectory, "removal-evidence.json")))
        {
            VerifyRetained(c, false); VerifyIntent();
            var retainedResult = AutomationFiles.Read<DriverRemovalWorkflowResult>(Path.Combine(folder, "operation", "result.json"));
            return retainedResult.Passed ? new(SubmissionWorkflowStatus.Completed, new("removal-evidence.json", AutomationFiles.Hash(Path.Combine(c.RunDirectory, "removal-evidence.json"))))
                : new(SubmissionWorkflowStatus.Failed, ReasonCode: "actual-driver-removal-check-failed");
        }
        if (Directory.Exists(folder))
        {
            if (File.Exists(intentPath)) VerifyIntent();
            return new(SubmissionWorkflowStatus.OutcomeUnknown, ReasonCode: "inspect-removal-operation-and-leases");
        }
        Directory.CreateDirectory(folder);
        var intent = new Intent(c.Checkpoint.InputSha256, c.Checkpoint.OperationId ?? throw new InvalidDataException("Missing removal operation identity."), endurance, postHash, Resolve(c, settings), DateTimeOffset.UtcNow);
        AutomationFiles.Write(intentPath, intent);
        var result = await (run ?? DriverRemovalWorkflow.RemoveAsync)(intent.Plan, credentials(settings.NUnit.Host), Path.Combine(folder, "operation"), token);
        if (!result.RemovalRequested || !result.CandidateVerified || !result.ReservationsReleased || result.Removal == null)
            return new(SubmissionWorkflowStatus.OutcomeUnknown, ReasonCode: "inspect-removal-operation-and-leases");
        if (System.Text.Json.JsonSerializer.Serialize(AutomationFiles.Read<DriverRemovalWorkflowResult>(Path.Combine(folder, "operation", "result.json"))) != System.Text.Json.JsonSerializer.Serialize(result))
            throw new InvalidDataException("Removal producer result does not match its retained evidence.");
        // Record the exact removal outcome; do not derive a pass from process exit alone.
        var identity = new SubmissionEvidenceIdentity(settings.Release.PackageSha256, settings.Release.SourceCommit, settings.Review!.Policy.Sha256, settings.Review.Template.Sha256);
        var files = Inventory(c.RunDirectory).Select(f => new SubmissionEvidenceFile(f.RelativePath, f.Sha256)).ToArray();
        var observation = new SubmissionObservation(requirement.Id, identity, result.Passed ? SubmissionEvidenceOutcome.Passed : SubmissionEvidenceOutcome.Failed,
            intent.StartedUtc, DateTimeOffset.UtcNow, files, "Final actual-driver removal: exact API tree, reviewed Home/Room and native-light views, Home restoration and retained current-boot log interval.",
            new(requirement.Execution!.Target, "combined"));
        var observations = new List<SubmissionObservation> { observation };
        if (result.Passed && settings.Removal!.PlacementRequirementId is {} placementId)
        {
            // Reuse the real pre-removal baseline, never the empty post-removal
            // screen as evidence that the installed candidate was placed correctly.
            string before = Path.Combine(folder, "operation", "removal", "ui-before");
            var baseline = AutomationFiles.Read<DriverRemovalUiOutcome>(Path.Combine(before, "outcome.json"));
            var appPlan = AutomationFiles.Read<DriverRemovalAppPlan>(Path.Combine(before, "plan.json"));
            if (!baseline.Passed || !baseline.HomeRestored ||
                System.Text.Json.JsonSerializer.Serialize(appPlan) != System.Text.Json.JsonSerializer.Serialize(intent.Plan.App))
                throw new InvalidDataException("Placement baseline did not pass or belongs to another selection.");
            var placement = AutomationFiles.Read<SubmissionEvidencePolicy>(settings.Review.Policy.Path).Requirements.Single(r => r.Id == placementId);
            observations.Add(new(placementId, identity, SubmissionEvidenceOutcome.Passed, intent.StartedUtc, DateTimeOffset.UtcNow,
                files, "Before removal, the candidate's exact selected tree matched the reviewed Home, Room and native Lights membership plan, with complete bounded list traversal and return Home. Explicitly nonvisual entities were accounted for. This does not assert icon glyphs, default actions, control behavior or response timing.",
                new(placement.Execution!.Target, "combined")));
        }
        File.WriteAllText(Path.Combine(c.RunDirectory, ObservationPath), System.Text.Json.JsonSerializer.Serialize(new SubmissionEvidenceDocument(1, observations), AutomationReview.DocumentJson));
        var receipt = new Receipt(c.Checkpoint.InputSha256, Inventory(c.RunDirectory));
        AutomationFiles.Write(Path.Combine(c.RunDirectory, "removal-evidence.json"), receipt);
        VerifyRetained(c, false);
        return result.Passed
            ? new(SubmissionWorkflowStatus.Completed, new("removal-evidence.json", AutomationFiles.Hash(Path.Combine(c.RunDirectory, "removal-evidence.json"))))
            : new(SubmissionWorkflowStatus.Failed, ReasonCode: "actual-driver-removal-check-failed");

        void VerifyIntent()
        {
            var saved = AutomationFiles.Read<Intent>(intentPath);
            if (saved.InputSha256 != c.Checkpoint.InputSha256 || saved.OperationId != c.Checkpoint.OperationId || saved.Endurance != endurance ||
                saved.PostEnduranceSha256 != postHash || System.Text.Json.JsonSerializer.Serialize(saved.Plan) != System.Text.Json.JsonSerializer.Serialize(Resolve(c, settings)))
                throw new InvalidDataException("Removal intent no longer matches this workflow.");
        }
    }

    private static SubmissionWorkflowReceipt[] Inventory(string root)
    {
        var pending = new Stack<DirectoryInfo>(); pending.Push(new(Path.Combine(root, "removal")));
        var files = new List<SubmissionWorkflowReceipt>(); int count = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Unsafe removal evidence link.");
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                if (++count > 4096 || (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Removal evidence exceeded its bound or contains a link.");
                if (item is DirectoryInfo child) pending.Push(child);
                else files.Add(new(Path.GetRelativePath(root, item.FullName).Replace('\\', '/'), AutomationFiles.Hash(item.FullName)));
            }
        }
        return files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();
    }

    internal static SubmissionEvidenceFile VerifyRetained(SubmissionWorkflowStepContext c, bool requirePassed = true)
    {
        var receipt = AutomationFiles.Read<Receipt>(Path.Combine(c.RunDirectory, "removal-evidence.json"));
        if (receipt.InputSha256 != c.Checkpoint.InputSha256 || !receipt.Files.SequenceEqual(Inventory(c.RunDirectory)) ||
            !receipt.Files.Any(f => f.RelativePath == ObservationPath)) throw new InvalidDataException("Removal evidence changed or belongs to another run.");
        var result = AutomationFiles.Read<DriverRemovalWorkflowResult>(Path.Combine(c.RunDirectory, "removal", "operation", "result.json"));
        if (!result.RemovalRequested || !result.CandidateVerified || !result.ReservationsReleased || result.Removal == null || requirePassed && !result.Passed)
            throw new InvalidDataException("Successful actual removal was not confirmed.");
        return new(ObservationPath, receipt.Files.Single(f => f.RelativePath == ObservationPath).Sha256);
    }
}
