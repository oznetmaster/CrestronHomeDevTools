// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrestronHomeDevTools;

public sealed record WindowsRunnerSetupPlan (int SchemaVersion, string MachineName, string GitHubUrl,
	string RunnerName, string Version, string ArchiveSha256, string InstallDirectory, string ServiceAccount, string[] Labels);
public sealed record WindowsRunnerSetupSecrets (string RegistrationToken, string? ServicePassword = null)
	{
	public override string ToString () => "Runner setup secrets (values hidden)";
	}
public sealed record WindowsRunnerSetupObservation (bool DirectoryExists, bool RegistrationMatches, bool ServiceMatches,
	bool ServiceRunning, bool ServiceAutomatic, bool ConflictingService);
public sealed record WindowsRunnerSetupState (string PlanSha256, string State, string Step, DateTimeOffset UpdatedUtc,
	bool GitHubOnlineVerified = false, bool WorkloadVerified = false);
public interface IWindowsRunnerSetupOperations
	{
	Task<WindowsRunnerSetupObservation> InspectAsync (WindowsRunnerSetupPlan plan, CancellationToken token);
	Task InstallAsync (WindowsRunnerSetupPlan plan, CancellationToken token);
	Task ConfigureAsync (WindowsRunnerSetupPlan plan, WindowsRunnerSetupSecrets secrets, CancellationToken token);
	}

/// <summary>Install a new Windows GitHub runner from an explicitly reviewed plan. Never replaces an existing runner or retries registration.</summary>
public static class WindowsRunnerSetup
	{
	public static string ServiceName (WindowsRunnerSetupPlan plan) => "actions.runner." + new Uri (plan.GitHubUrl).AbsolutePath.Trim ('/').Replace ('/', '-') + "." + plan.RunnerName;
	public static Uri ArchiveUrl (WindowsRunnerSetupPlan plan) => new ($"https://github.com/actions/runner/releases/download/v{plan.Version}/actions-runner-win-x64-{plan.Version}.zip");
	public static void Validate (WindowsRunnerSetupPlan plan)
		{
		ArgumentNullException.ThrowIfNull (plan);
		if (plan.SchemaVersion != 1 || string.IsNullOrWhiteSpace (plan.MachineName) ||
			!Regex.IsMatch (plan.GitHubUrl ?? "", @"^https://github\.com/[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)?$", RegexOptions.CultureInvariant) ||
			!Regex.IsMatch (plan.RunnerName ?? "", @"^[A-Za-z0-9_-]{1,40}$", RegexOptions.CultureInvariant) ||
			!Regex.IsMatch (plan.Version ?? "", @"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant) ||
			!Regex.IsMatch (plan.ArchiveSha256 ?? "", @"^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant) ||
			!Path.IsPathFullyQualified (plan.InstallDirectory) || plan.InstallDirectory.StartsWith (@"\\", StringComparison.Ordinal) ||
			string.IsNullOrWhiteSpace (plan.ServiceAccount) || plan.ServiceAccount.Any (char.IsControl) ||
			plan.Labels == null || plan.Labels.Length == 0 || plan.Labels.Any (label => !Regex.IsMatch (label ?? "", @"^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)))
			throw new ArgumentException ("Provide a GitHub repository or organization URL, version, trusted archive hash, new local directory, service account and labels.");
		if (ServiceName (plan).Length > 80 || Path.GetPathRoot (plan.InstallDirectory) == Path.GetFullPath (plan.InstallDirectory))
			throw new ArgumentException ("Choose a shorter runner name and a dedicated installation directory below the drive root.");
		}
	public static string Digest (WindowsRunnerSetupPlan plan)
		{
		Validate (plan);
		return Convert.ToHexStringLower (SHA256.HashData (JsonSerializer.SerializeToUtf8Bytes (plan)));
		}
	/// <summary>Read the local registration and service for a plan without installing, changing a journal or contacting GitHub.</summary>
	public static Task<WindowsRunnerSetupObservation> InspectAsync (WindowsRunnerSetupPlan plan, CancellationToken token = default)
		{
		Validate (plan);
		if (!plan.MachineName.Equals (Environment.MachineName, StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException ("Inspect the plan on its intended Windows computer.");
		return new WindowsRunnerSetupOperations ().InspectAsync (plan, token);
		}
	public static Task<WindowsRunnerSetupState> ApplyAsync (WindowsRunnerSetupPlan plan, string approvedPlanSha256,
		string stateDirectory, WindowsRunnerSetupSecrets secrets, IProgress<string>? progress = null, CancellationToken token = default)
		{
		if (!OperatingSystem.IsWindows ())
			throw new PlatformNotSupportedException ("Windows runner setup requires Windows.");
		return Task.Run (() =>
			{
				using var mutex = new Mutex (false, @"Global\CrestronHomeDevTools-WindowsRunnerSetup");
				bool held;
				try
					{
					held = mutex.WaitOne (0);
					}
				catch (AbandonedMutexException) { held = true; }
				if (!held)
					throw new InvalidOperationException ("Another runner setup is in progress.");
				try
					{
					return ApplyCoreAsync (plan, approvedPlanSha256, stateDirectory, secrets, new WindowsRunnerSetupOperations (), progress, token).GetAwaiter ().GetResult ();
					}
				finally { mutex.ReleaseMutex (); }
			}, token);
		}
	internal static async Task<WindowsRunnerSetupState> ApplyCoreAsync (WindowsRunnerSetupPlan plan, string approvedPlanSha256,
		string stateDirectory, WindowsRunnerSetupSecrets secrets, IWindowsRunnerSetupOperations operations, IProgress<string>? progress, CancellationToken token)
		{
		string digest = Digest (plan);
		if (digest != approvedPlanSha256 || !plan.MachineName.Equals (Environment.MachineName, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException ("Reviewed runner plan does not match this computer or digest.");
		if (!Path.IsPathFullyQualified (stateDirectory) || stateDirectory.StartsWith (@"\\", StringComparison.Ordinal))
			throw new ArgumentException ("Use a private local setup journal directory.");
		string stateRoot = Path.GetFullPath (stateDirectory).TrimEnd (Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string installRoot = Path.GetFullPath (plan.InstallDirectory).TrimEnd (Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (stateRoot.StartsWith (installRoot, StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException ("Keep setup state outside the new runner directory.");
		Directory.CreateDirectory (stateDirectory);
		using var guard = new FileStream (Path.Combine (stateDirectory, "runner-setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		string path = Path.Combine (stateDirectory, "runner-state.json");
		var prior = File.Exists (path) ? JsonSerializer.Deserialize<WindowsRunnerSetupState> (File.ReadAllBytes (path)) : null;
		if (prior != null && prior.PlanSha256 != digest)
			throw new InvalidOperationException ("The runner journal belongs to another plan.");
		WindowsRunnerSetupState Record (string state, string step)
			{
			var value = new WindowsRunnerSetupState (digest, state, step, DateTimeOffset.UtcNow);
			using (var file = new FileStream (path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
				{
				JsonSerializer.Serialize (file, value);
				file.Flush (true);
				}
			SubmissionJournalFile.Replace (path + ".tmp", path);
			progress?.Report (state + ": " + step);
			return value;
			}
		var observation = await operations.InspectAsync (plan, token).ConfigureAwait (false);
		if (prior != null && observation.RegistrationMatches && observation.ServiceMatches && observation.ServiceRunning && observation.ServiceAutomatic && !observation.ConflictingService)
			return Record ("Completed", "Local registration and automatic running service observed; verify GitHub online status and a representative job separately");
		if (prior != null || observation.DirectoryExists || observation.ConflictingService)
			return Record ("InspectionRequired", "Existing or interrupted setup; inspect GitHub registration and Windows service before any manual recovery. Registration was not replayed");
		if (string.IsNullOrWhiteSpace (secrets.RegistrationToken))
			throw new ArgumentException ("A fresh registration token is required.");
		Record ("Applying", "Download and verify official runner archive");
		await operations.InstallAsync (plan, token).ConfigureAwait (false);
		observation = await operations.InspectAsync (plan, token).ConfigureAwait (false);
		if (!observation.DirectoryExists || observation.ConflictingService || observation.RegistrationMatches)
			return Record ("InspectionRequired", "Installation or service collision must be inspected before registration");
		Record ("Applying", "Configure new runner and Windows service");
		await operations.ConfigureAsync (plan, secrets, token).ConfigureAwait (false);
		observation = await operations.InspectAsync (plan, token).ConfigureAwait (false);
		return Record (observation.RegistrationMatches && observation.ServiceMatches && observation.ServiceRunning && observation.ServiceAutomatic && !observation.ConflictingService ? "Completed" : "InspectionRequired",
			"Local setup observation only; verify GitHub online status and a representative job separately");
		}
	}