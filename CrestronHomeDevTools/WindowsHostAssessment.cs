// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record WindowsHostService (string Name, string Status, string StartType);
public sealed record WindowsHostSnapshot (string MachineName, DateTimeOffset ObservedUtc, string OperatingSystem,
	IReadOnlyList<string> ProcessorModels, int LogicalProcessors, long TotalMemoryMiB, long AvailableMemoryMiB,
	string WorkDirectory, long AvailableDiskMiB, bool? VirtualizationAvailable, int SessionId, bool UserInteractive,
	bool? DesktopUsable, IReadOnlyList<string> DotNetSdkVersions, IReadOnlyList<WindowsHostService> Services);

/// <summary>Combined resource budget for the intended concurrent workloads, chosen by their operator.</summary>
public sealed record WindowsWorkloadRequirements (string Name, int MinimumLogicalProcessors, long MinimumAvailableMemoryMiB,
	long MinimumAvailableDiskMiB, int? RequiredDotNetSdkMajor = null, bool RequiresVirtualization = false,
	bool RequiresDesktop = false, bool RequiresSshService = false, bool RequiresGitHubRunnerService = false);

public sealed record WindowsHostAssessmentResult (string Workload, bool DeclaredPrerequisitesSatisfied,
	IReadOnlyList<string> Missing, IReadOnlyList<string> Unverified, bool WorkloadRehearsalRequired = true);

/// <summary>Read-only prerequisite assessment. Does not install software, log in, benchmark or mark a resource ready.</summary>
public static class WindowsHostAssessment
	{
	public static WindowsHostAssessmentResult Evaluate (WindowsHostSnapshot snapshot, WindowsWorkloadRequirements requirements)
		{
		ArgumentNullException.ThrowIfNull (snapshot);
		ArgumentNullException.ThrowIfNull (requirements);
		if (string.IsNullOrWhiteSpace (requirements.Name) || requirements.MinimumLogicalProcessors < 1 ||
			requirements.MinimumAvailableMemoryMiB < 1 || requirements.MinimumAvailableDiskMiB < 1 || requirements.RequiredDotNetSdkMajor is < 1)
			throw new ArgumentException ("Provide a named workload and positive CPU, available memory and disk budgets.");
		var missing = new List<string> ();
		var unverified = new List<string> ();
		if (snapshot.LogicalProcessors < requirements.MinimumLogicalProcessors)
			missing.Add ("logical-processors");
		if (snapshot.AvailableMemoryMiB < requirements.MinimumAvailableMemoryMiB)
			missing.Add ("available-memory");
		if (snapshot.AvailableDiskMiB < requirements.MinimumAvailableDiskMiB)
			missing.Add ("available-work-disk");
		if (requirements.RequiredDotNetSdkMajor is int major && !snapshot.DotNetSdkVersions.Any (v =>
			int.TryParse (v.Split ('.')[0], out int found) && found == major))
			missing.Add ("required-dotnet-sdk");
		if (requirements.RequiresVirtualization)
			{
			if (snapshot.VirtualizationAvailable == false)
				missing.Add ("hardware-virtualization");
			else if (snapshot.VirtualizationAvailable == null)
				unverified.Add ("hardware-virtualization");
			unverified.Add ("emulator-acceleration-and-adb-rehearsal");
			}
		if (requirements.RequiresDesktop)
			{
			if (snapshot.SessionId == 0 || !snapshot.UserInteractive)
				missing.Add ("interactive-user-session");
			if (snapshot.DesktopUsable == false)
				missing.Add ("usable-desktop");
			else if (snapshot.DesktopUsable == null)
				unverified.Add ("unlocked-desktop-and-application-access");
			unverified.Add ("desktop-after-remote-disconnect-and-restart");
			}
		bool RunningAutomatic (string name, bool prefix) => snapshot.Services.Any (s =>
			(prefix ? s.Name.StartsWith (name, StringComparison.OrdinalIgnoreCase) : s.Name.Equals (name, StringComparison.OrdinalIgnoreCase)) &&
			s.Status.Equals ("Running", StringComparison.OrdinalIgnoreCase) && s.StartType.Equals ("Automatic", StringComparison.OrdinalIgnoreCase));
		if (requirements.RequiresSshService && !RunningAutomatic ("sshd", false))
			missing.Add ("running-automatic-ssh-service");
		if (requirements.RequiresSshService)
			unverified.Add ("ssh-access-from-orchestrator");
		if (requirements.RequiresGitHubRunnerService && !RunningAutomatic ("actions.runner.", true))
			missing.Add ("running-automatic-github-runner-service");
		if (requirements.RequiresGitHubRunnerService)
			unverified.Add ("github-runner-registration-and-access");
		return new (requirements.Name, missing.Count == 0 && unverified.Count == 0, missing, unverified);
		}

	/// <summary>Observe this account's Windows host; desktop usability remains unknown until exercised in its actual session.</summary>
	public static async Task<WindowsHostSnapshot> ReadLocalAsync (string workDirectory, CancellationToken token = default)
		{
		if (!OperatingSystem.IsWindows ())
			throw new PlatformNotSupportedException ("Windows host inspection requires Windows.");
		if (!Path.IsPathFullyQualified (workDirectory) || workDirectory.StartsWith (@"\\", StringComparison.Ordinal) || !Directory.Exists (workDirectory))
			throw new ArgumentException ("Choose an existing absolute local work directory.");
		using var resource = typeof (WindowsHostAssessment).Assembly.GetManifestResourceStream ("CrestronHomeDevTools.WindowsHostSnapshot.ps1")
			?? throw new InvalidOperationException ("Host assessment script is missing.");
		using var reader = new StreamReader (resource);
		string script = "& {\r\n" + await reader.ReadToEndAsync (token) + "\r\n} -WorkDirectory '" + workDirectory.Replace ("'", "''", StringComparison.Ordinal) + "'";
		var start = new ProcessStartInfo (Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
			{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
			};
		foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String (Encoding.Unicode.GetBytes (script)) })
			start.ArgumentList.Add (arg);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (TimeSpan.FromSeconds (45));
		using var process = Process.Start (start) ?? throw new IOException ("Could not start host inspection.");
		try
			{
			process.StandardInput.Close ();
			Task<string> output = process.StandardOutput.ReadToEndAsync (deadline.Token);
			Task<string> error = process.StandardError.ReadToEndAsync (deadline.Token);
			await Task.WhenAll (output, error, process.WaitForExitAsync (deadline.Token));
			if (process.ExitCode != 0 || output.Result.Length > 65536)
				throw new IOException ("Windows host inspection failed.");
			return JsonSerializer.Deserialize<WindowsHostSnapshot> (output.Result) ?? throw new InvalidDataException ("Missing host inspection result.");
			}
		finally
			{
			if (!process.HasExited)
				{
				process.Kill (true);
				await process.WaitForExitAsync (CancellationToken.None);
				}
			}
		}
	}