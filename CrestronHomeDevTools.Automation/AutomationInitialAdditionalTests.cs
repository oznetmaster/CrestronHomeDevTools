// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;

using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

/// <summary>Separate initial fixture, such as instrumented outages, with the ordinary installed-app
/// runner's identity, lease, selected-test, restoration and no-replay checks.</summary>
internal static class AutomationInitialAdditionalTests
	{
	internal const string DirectoryName = "pre-endurance";
	internal const string ReceiptName = "initial-tests.json";
	private sealed record Binding (string InputSha256, SubmissionWorkflowReceipt AppTests);
	private sealed record Receipt (string InputSha256, SubmissionWorkflowReceipt[] Files);
	internal static SubmissionAutomationSettings Settings (SubmissionAutomationSettings settings) => settings with
		{
		InstalledAppTests = settings.PreEnduranceTests ?? throw new InvalidDataException ("Additional initial tests are not configured."),
		CredentialBindings = settings.PreEnduranceSeparateProcessor ? settings.PreEnduranceCredentialBindings
			?? throw new InvalidDataException("Separate initial processor credentials are required.") : settings.CredentialBindings,
		NUnit = settings.NUnit with
			{
			AndroidTests = null,
			Host = settings.PreEnduranceSeparateProcessor ? settings.PreEnduranceTests!.Host : settings.NUnit.Host,
			CertificateSha256 = settings.PreEnduranceSeparateProcessor ? settings.PreEnduranceTests!.CertificateSha256 : settings.NUnit.CertificateSha256,
			SshFingerprint = settings.PreEnduranceSeparateProcessor ? settings.PreEnduranceTests!.SshFingerprint : settings.NUnit.SshFingerprint
			},
		InstalledAppFixtureSettings = settings.PreEnduranceSeparateProcessor ? settings.PreEnduranceFixtureSettings : settings.PreEnduranceFixtureSettings ?? settings.InstalledAppFixtureSettings
		};
	internal static void Validate (SubmissionAutomationSettings settings)
		{
		if (!settings.PreEnduranceSeparateProcessor && settings.PreEnduranceCredentialBindings != null)
			throw new InvalidDataException("Separate initial credential bindings require a separate processor phase.");
		if (settings.PreEnduranceTests == null)
			{
			if (settings.PreEnduranceFromDeployment || settings.PreEnduranceSeparateProcessor || settings.PreEnduranceFixtureSettings != null)
				throw new InvalidDataException ("Additional initial bindings require PreEnduranceTests.");
			return;
			}
		if (settings.InstalledAppTests == null || settings.Endurance == null || settings.Review == null)
			throw new InvalidDataException ("Additional initial tests require installed-app tests, endurance and review bindings.");
		if (settings.PreEnduranceFromDeployment && (settings.NUnit.ActualDriver == null || settings.NUnit.ReleaseCandidate == null))
			throw new InvalidDataException ("Deployment-bound initial tests require the actual candidate deployment.");
		if (settings.PreEnduranceSeparateProcessor)
			{
			if (string.IsNullOrWhiteSpace(settings.PreEnduranceCredentialBindings) || !Path.IsPathFullyQualified(settings.PreEnduranceCredentialBindings))
				throw new InvalidDataException("Separate initial processor requires explicit absolute credential bindings.");
			if (settings.PreEnduranceFromDeployment || string.Equals(settings.PreEnduranceTests.Host,settings.NUnit.Host,StringComparison.OrdinalIgnoreCase) ||
			 settings.PreEnduranceFixtureSettings == null || settings.PreEnduranceFixtureSettings.Value.GetRawText().Contains("${managed:",StringComparison.Ordinal))
				throw new InvalidDataException("Separate-processor initial tests require a distinct host, explicit fixture inputs and concrete target IDs; main-processor deployment or managed-child bindings cannot be reused.");
			}
		if (!settings.PreEnduranceSeparateProcessor && !settings.PreEnduranceFromDeployment && settings.PreEnduranceTests.Target != settings.InstalledAppTests.Target)
			throw new InvalidDataException ("Additional initial tests must select the same installed driver or bind to its deployment receipt.");
		AutomationInstalledApp.ValidateTemplate (Settings (settings), settings.PreEnduranceFromDeployment);
		}
	private static Binding Expected (SubmissionWorkflowStepContext context)
		{
		AutomationInstalledApp.VerifyRetained (context.RunDirectory);
		if (!SubmissionEvidence.SafeEvidencePath (context.RunDirectory, "installed-app-tests.json", out var path))
			throw new InvalidDataException ("Completed initial app receipt is missing or unsafe.");
		var receipt = AutomationFiles.Read<Receipt> (path);
		if (receipt.InputSha256 != context.Checkpoint.InputSha256)
			throw new InvalidDataException ("Initial app tests belong to another workflow.");
		var reference = new SubmissionWorkflowReceipt ("installed-app-tests.json", AutomationFiles.Hash (path));
		if (context.Checkpoint.CompletedStages.TryGetValue (SubmissionWorkflowStage.AppTests, out var completed))
			{
			if (completed.RelativePath != ReceiptName || !SubmissionEvidence.SafeEvidencePath (context.RunDirectory, ReceiptName, out var aggregate) ||
			 AutomationFiles.Hash (aggregate) != completed.Sha256)
				throw new InvalidDataException ("Additional initial tests require their pinned combined completion receipt.");
			var combined = AutomationFiles.Read<Receipt> (aggregate);
			if (combined.InputSha256 != context.Checkpoint.InputSha256 || !combined.Files.Contains (reference))
				throw new InvalidDataException ("Initial app receipt differs from the combined completion record.");
			foreach (var file in combined.Files)
				if (!SubmissionEvidence.SafeEvidencePath (context.RunDirectory, file.RelativePath, out var retained) || AutomationFiles.Hash (retained) != file.Sha256)
					throw new InvalidDataException ("Combined initial evidence changed after completion.");
			}
		return new (context.Checkpoint.InputSha256, reference);
		}
	internal static async Task<SubmissionWorkflowStepResult> Advance (SubmissionWorkflowStepContext context,
	 SubmissionAutomationSettings settings, Func<InstalledDriverTestPlan, NetworkCredential, string, CancellationToken, Task<InstalledDriverTestResult>> run,
	 Func<string, NetworkCredential> credentials, CancellationToken token)
		{
		Validate (settings);
		var binding = Expected (context);
		string folder = Path.Combine (context.RunDirectory, DirectoryName);
		bool prepared = Directory.Exists (folder);
		if (prepared && !SubmissionEvidence.SafeEvidencePath (folder, "initial-app-binding.json", out _))
			throw new InvalidDataException ("Existing additional initial operation has no safe binding; inspect without replaying.");
		Directory.CreateDirectory (folder);
		AutomationFiles.Write (Path.Combine (folder, "initial-app-binding.json"), binding);
		var resolved = settings.PreEnduranceSeparateProcessor ? Settings(settings) :
		 AutomationPostEndurance.ResolveTarget (context, settings, Settings (settings), settings.PreEnduranceFromDeployment, beforeAppTests: true);
		bool attempted = File.Exists (Path.Combine (folder, "installed-app-intent.json"));
		if (prepared && !attempted)
			return new (SubmissionWorkflowStatus.OutcomeUnknown, ReasonCode: "inspect-additional-initial-operation-and-leases");
		if (attempted && !SubmissionEvidence.SafeEvidencePath (folder, "target-plan.json", out _))
			throw new InvalidDataException ("Retained additional initial target plan is missing or unsafe.");
		AutomationFiles.Write (Path.Combine (folder, "target-plan.json"), resolved.InstalledAppTests);
		var result = await AutomationInstalledApp.Advance (context with
			{
			RunDirectory = folder
			}, resolved, attempted, run, credentials, token);
		return result.Status == SubmissionWorkflowStatus.Completed ? Complete (context) : result;
		}
	internal static SubmissionWorkflowStepResult Complete (SubmissionWorkflowStepContext context)
		{
		var files = RetainedFiles (context).ToList ();
		var initial = AutomationFiles.Read<Receipt> (Path.Combine (context.RunDirectory, "installed-app-tests.json"));
		files.AddRange (initial.Files);
		files.Add (new ("installed-app-tests.json", AutomationFiles.Hash (Path.Combine (context.RunDirectory, "installed-app-tests.json"))));
		if (files.Select (f => f.RelativePath.Replace ('\\', '/')).Distinct (StringComparer.Ordinal).Count () != files.Count)
			throw new InvalidDataException ("Initial producer evidence paths overlap.");
		return AutomationFiles.Complete (context, ReceiptName, new Receipt (context.Checkpoint.InputSha256, files.ToArray ()));
		}
	internal static void VerifyRetained (SubmissionWorkflowStepContext context)
		{
		string folder = Path.Combine (context.RunDirectory, DirectoryName);
		if (!SubmissionEvidence.SafeEvidencePath (folder, "initial-app-binding.json", out var binding) ||
		 AutomationFiles.Read<Binding> (binding) != Expected (context))
			throw new InvalidDataException ("Additional initial tests belong to another app attempt.");
		AutomationInstalledApp.VerifyRetained (folder);
		}
	internal static IEnumerable<SubmissionWorkflowReceipt> RetainedFiles (SubmissionWorkflowStepContext context)
		{
		VerifyRetained (context);
		string folder = Path.Combine (context.RunDirectory, DirectoryName);
		var receipt = AutomationFiles.Read<Receipt> (Path.Combine (folder, "installed-app-tests.json"));
		if (receipt.InputSha256 != context.Checkpoint.InputSha256)
			throw new InvalidDataException ("Additional initial results belong to another workflow.");
		var files = receipt.Files.Select (f => new SubmissionWorkflowReceipt (DirectoryName + "/" + f.RelativePath.Replace ('\\', '/'), f.Sha256)).ToList ();
		foreach (string name in new[] { "initial-app-binding.json", "installed-app-tests.json", "installed-app-intent.json" })
			{
			if (!SubmissionEvidence.SafeEvidencePath (folder, name, out var path))
				throw new InvalidDataException ("Additional initial producer provenance is missing or unsafe.");
			files.Add (new (DirectoryName + "/" + name, AutomationFiles.Hash (path)));
			}
		return files.ToArray ();
		}
	}
