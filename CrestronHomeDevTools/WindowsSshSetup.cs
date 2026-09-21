// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record WindowsSshSetupPlan (int SchemaVersion, string MachineName, string RemoteAddress);
public sealed record WindowsSshSetupSnapshot (bool Installed, bool ServiceReady, bool FirewallReady, DateTimeOffset BootUtc);
public sealed record WindowsSshSetupState (string PlanSha256, string State, string? Step, DateTimeOffset BootUtc, DateTimeOffset UpdatedUtc);

/// <summary>Operations used by the setup coordinator. Implementations must report actual observations and never reboot.</summary>
public interface IWindowsSshSetupOperations
	{
	Task<WindowsSshSetupSnapshot> InspectAsync (WindowsSshSetupPlan plan, CancellationToken token);
	Task<bool> InstallAsync (WindowsSshSetupPlan plan, CancellationToken token);
	Task ConfigureServiceAsync (WindowsSshSetupPlan plan, CancellationToken token);
	Task ConfigureFirewallAsync (WindowsSshSetupPlan plan, CancellationToken token);
	}

/// <summary>Reviewed, resumable SSH prerequisite setup. Does not reboot, install other tools or claim remote access works.</summary>
public static class WindowsSshSetup
	{
	public static void Validate (WindowsSshSetupPlan plan)
		{
		ArgumentNullException.ThrowIfNull (plan);
		bool address = plan.RemoteAddress == "LocalSubnet" || IPAddress.TryParse (plan.RemoteAddress, out _);
		if (plan.SchemaVersion != 1 || string.IsNullOrWhiteSpace (plan.MachineName) || !address)
			throw new ArgumentException ("Use this computer's name and one source IP address or LocalSubnet for SSH access.");
		}

	public static string Digest (WindowsSshSetupPlan plan)
		{
		Validate (plan);
		return Convert.ToHexString (SHA256.HashData (JsonSerializer.SerializeToUtf8Bytes (plan))).ToLowerInvariant ();
		}

	public static Task<WindowsSshSetupState> ApplyAsync (WindowsSshSetupPlan plan, string approvedPlanSha256,
		string stateDirectory, IProgress<string>? progress = null, CancellationToken token = default, bool resumeAfterInspection = false)
		{
		if (!OperatingSystem.IsWindows ())
			throw new PlatformNotSupportedException ("Windows setup requires Windows.");
		return Task.Run (() =>
			{
				// Across Windows sessions, only one setup operation may change SSH on this machine at once.
				using var mutex = new Mutex (false, @"Global\CrestronHomeDevTools-WindowsSshSetup");
				bool held;
				try
					{
					held = mutex.WaitOne (0);
					}
				catch (AbandonedMutexException) { held = true; }
				if (!held)
					throw new InvalidOperationException ("Another Windows setup operation is in progress.");
				try
					{
					// Keep mutex ownership on its acquiring thread while asynchronous operations run.
					return ApplyCoreAsync (plan, approvedPlanSha256, stateDirectory, new WindowsSshSetupOperations (), progress, token, resumeAfterInspection).GetAwaiter ().GetResult ();
					}
				finally { mutex.ReleaseMutex (); }
			}, token);
		}

	internal static async Task<WindowsSshSetupState> ApplyCoreAsync (WindowsSshSetupPlan plan, string approvedPlanSha256,
		string stateDirectory, IWindowsSshSetupOperations operations, IProgress<string>? progress, CancellationToken token, bool resumeAfterInspection = false)
		{
		string digest = Digest (plan);
		if (digest != approvedPlanSha256 || !plan.MachineName.Equals (Environment.MachineName, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException ("The reviewed setup plan does not match this computer or digest.");
		if (!Path.IsPathFullyQualified (stateDirectory) || stateDirectory.StartsWith (@"\\", StringComparison.Ordinal))
			throw new ArgumentException ("Use a private local setup state directory.");
		Directory.CreateDirectory (stateDirectory);
		using var guard = new FileStream (Path.Combine (stateDirectory, "setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		string statePath = Path.Combine (stateDirectory, "state.json");
		WindowsSshSetupState? previous = File.Exists (statePath) ? JsonSerializer.Deserialize<WindowsSshSetupState> (File.ReadAllBytes (statePath)) : null;
		if (previous != null && previous.PlanSha256 != digest)
			throw new InvalidOperationException ("This journal belongs to another setup plan.");
		var observed = await operations.InspectAsync (plan, token).ConfigureAwait (false);
		WindowsSshSetupState Record (string state, string? step)
			{
			var value = new WindowsSshSetupState (digest, state, step, observed.BootUtc, DateTimeOffset.UtcNow);
			string temporary = statePath + ".tmp";
			using (var file = new FileStream (temporary, FileMode.Create, FileAccess.Write, FileShare.None))
				{
				JsonSerializer.Serialize (file, value);
				file.Flush (true);
				}
			SubmissionJournalFile.Replace (temporary, statePath);
			progress?.Report (state + (step == null ? "" : ": " + step));
			return value;
			}
		if (previous?.State == "RestartRequired" && observed.BootUtc <= previous.BootUtc)
			return previous;
		if (!resumeAfterInspection && previous?.State == "Applying" && previous.Step == "Install" && !observed.Installed && observed.BootUtc <= previous.BootUtc)
			return Record ("InspectionRequired", "Interrupted installation; inspect Windows servicing before retrying");
		if (!resumeAfterInspection && previous?.State == "InspectionRequired" && !observed.Installed && observed.BootUtc <= previous.BootUtc)
			return previous;
		if (!observed.Installed)
			{
			Record ("Applying", "Install");
			progress?.Report ("Installing Windows OpenSSH Server; Windows servicing may take several minutes.");
			if (await operations.InstallAsync (plan, token).ConfigureAwait (false))
				return Record ("RestartRequired", "Install");
			observed = await operations.InspectAsync (plan, token).ConfigureAwait (false);
			if (!observed.Installed)
				return Record ("InspectionRequired", "Windows did not report OpenSSH Server installed");
			}
		if (!observed.ServiceReady)
			{
			Record ("Applying", "Service");
			await operations.ConfigureServiceAsync (plan, token).ConfigureAwait (false);
			}
		if (!observed.FirewallReady)
			{
			Record ("Applying", "Firewall");
			await operations.ConfigureFirewallAsync (plan, token).ConfigureAwait (false);
			}
		observed = await operations.InspectAsync (plan, token).ConfigureAwait (false);
		return Record (observed.Installed && observed.ServiceReady && observed.FirewallReady ? "Completed" : "InspectionRequired", null);
		}
	}

internal sealed class WindowsSshSetupOperations : IWindowsSshSetupOperations
	{
	public async Task<WindowsSshSetupSnapshot> InspectAsync (WindowsSshSetupPlan plan, CancellationToken token) =>
		JsonSerializer.Deserialize<WindowsSshSetupSnapshot> (await InvokeAsync ("Inspect", plan, token).ConfigureAwait (false)) ?? throw new InvalidDataException ("No setup observation.");
	public async Task<bool> InstallAsync (WindowsSshSetupPlan plan, CancellationToken token)
		{
		using var result = JsonDocument.Parse (await InvokeAsync ("Install", plan, token).ConfigureAwait (false));
		return result.RootElement.GetProperty ("RestartRequired").GetBoolean ();
		}
	public async Task ConfigureServiceAsync (WindowsSshSetupPlan plan, CancellationToken token) => _ = await InvokeAsync ("Service", plan, token).ConfigureAwait (false);
	public async Task ConfigureFirewallAsync (WindowsSshSetupPlan plan, CancellationToken token) => _ = await InvokeAsync ("Firewall", plan, token).ConfigureAwait (false);
	private static async Task<string> InvokeAsync (string operation, WindowsSshSetupPlan plan, CancellationToken token)
		{
		using var source = typeof (WindowsSshSetup).Assembly.GetManifestResourceStream ("CrestronHomeDevTools.WindowsSshSetup.ps1") ?? throw new InvalidOperationException ("Setup tool missing.");
		using var reader = new StreamReader (source);
		string script = "& {\r\n" + await reader.ReadToEndAsync (token).ConfigureAwait (false) + "\r\n} -Operation '" + operation + "' -RemoteAddress '" + plan.RemoteAddress + "'";
		var start = new ProcessStartInfo (Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
			{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
			};
		start.Environment.Remove ("PSModulePath");
		foreach (string value in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String (Encoding.Unicode.GetBytes (script)) })
			start.ArgumentList.Add (value);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (TimeSpan.FromMinutes (operation == "Install" ? 30 : 2));
		using var process = Process.Start (start) ?? throw new IOException ("Cannot start Windows prerequisite setup.");
		try
			{
			process.StandardInput.Close ();
			Task<string> output = process.StandardOutput.ReadToEndAsync (deadline.Token), error = process.StandardError.ReadToEndAsync (deadline.Token);
			await Task.WhenAll (output, error, process.WaitForExitAsync (deadline.Token)).ConfigureAwait (false);
			if (process.ExitCode != 0 || output.Result.Length > 65536)
				throw new IOException ("Windows prerequisite step failed; inspect the retained setup state and Windows servicing logs.");
			return output.Result;
			}
		finally { if (!process.HasExited) { process.Kill (true); await process.WaitForExitAsync (CancellationToken.None).ConfigureAwait (false); } }
		}
	}