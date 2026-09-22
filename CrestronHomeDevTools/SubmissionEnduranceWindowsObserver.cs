// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Renci.SshNet;
using Renci.SshNet.Common;

namespace CrestronHomeDevTools;

public sealed record SubmissionEnduranceWindowsTask (string TaskName, string StateDirectory, string TaskPath = "\\");
public sealed record SubmissionEnduranceObserverEndpoint (string Host, int Port, string HostKeyAlgorithm, string HostKeyFingerprint);

/// <summary>Read-only Windows task/scheduler observation, locally or over explicitly pinned SSH. Does not query a processor.</summary>
public static class SubmissionEnduranceWindowsObserver
	{
	private const int OutputLimit = 8 * 1024 * 1024;
	private static readonly JsonSerializerOptions JsonOptions = new ()
		{
		PropertyNameCaseInsensitive = true,
		Converters = { new JsonStringEnumConverter () }
		};

	/// <summary>Assess one fresh observation. A query failure is an alert, never a reused healthy snapshot.</summary>
	public static Task<SubmissionEnduranceHealthReport> AssessAsync (SubmissionEndurancePlan plan, SubmissionEnduranceWindowsTask task,
		SubmissionEnduranceObserverEndpoint? endpoint = null, NetworkCredential? credential = null, CancellationToken token = default) =>
		AssessCoreAsync (plan, ct => endpoint == null ? ReadLocalAsync (task, ct) :
			ReadRemoteAsync (task, endpoint, credential ?? throw new ArgumentException ("Remote observation requires Windows SSH credentials."), ct), token);

	internal static async Task<SubmissionEnduranceHealthReport> AssessCoreAsync (SubmissionEndurancePlan plan,
		Func<CancellationToken, Task<SubmissionEnduranceHealthSnapshot>> observe, CancellationToken token)
		{
		SubmissionEndurance.ValidatePlan (plan);
		token.ThrowIfCancellationRequested ();
		try
			{
			var snapshot = await observe (token).ConfigureAwait (false);
			return SubmissionEnduranceHealth.Evaluate (plan, snapshot, DateTimeOffset.UtcNow, TimeSpan.FromMinutes (5), TimeSpan.FromSeconds (2));
			}
		catch (Exception failure) when (!token.IsCancellationRequested && failure is SshException or IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception or OperationCanceledException or TimeoutException)
			{
			return new (SubmissionEnduranceHealthState.AttentionRequired, ["observer-query-failed"], DateTimeOffset.UtcNow, null, SubmissionEndurance.PlanDigest (plan));
			}
		}

	internal static string Script (SubmissionEnduranceWindowsTask task)
		{
		ArgumentNullException.ThrowIfNull (task);
		foreach (string value in new[] { task.TaskName, task.StateDirectory, task.TaskPath })
			if (string.IsNullOrWhiteSpace (value) || value.Length > 1024 || value.Any (char.IsControl))
				throw new ArgumentException ("Provide bounded Windows task names and paths without control characters.");
		if (task.StateDirectory.Length < 3 || !char.IsAsciiLetter (task.StateDirectory[0]) || task.StateDirectory[1] != ':' || task.StateDirectory[2] != '\\' ||
			!task.TaskPath.StartsWith ('\\'))
			throw new ArgumentException ("Use a local absolute Windows scheduler-state directory and task-folder path.");
		using var source = typeof (SubmissionEnduranceWindowsObserver).Assembly.GetManifestResourceStream ("CrestronHomeDevTools.EnduranceHealthSnapshot.ps1")
			?? throw new InvalidOperationException ("The installed observer is missing its bundled snapshot reader.");
		using var reader = new StreamReader (source);
		static string Quote (string value) => "'" + value.Replace ("'", "''", StringComparison.Ordinal) + "'";
		return "& {\r\n" + reader.ReadToEnd () + "\r\n} -TaskName " + Quote (task.TaskName) +
			" -StateDirectory " + Quote (task.StateDirectory) + " -TaskPath " + Quote (task.TaskPath);
		}

	internal static string EncodedCommand (SubmissionEnduranceWindowsTask task)
		{
		// Compress the bundled reader to stay below the remote Windows command-line limit.
		using var compressed = new MemoryStream ();
		using (var gzip = new GZipStream (compressed, CompressionLevel.SmallestSize, leaveOpen: true))
			gzip.Write (Encoding.UTF8.GetBytes (Script (task)));
		string payload = Convert.ToBase64String (compressed.ToArray ());
		string bootstrap = "$ProgressPreference='SilentlyContinue';$m=[IO.MemoryStream]::new([Convert]::FromBase64String('" + payload + "'));" +
			"$g=[IO.Compression.GZipStream]::new($m,[IO.Compression.CompressionMode]::Decompress);" +
			"$r=[IO.StreamReader]::new($g,[Text.Encoding]::UTF8);try{& ([ScriptBlock]::Create($r.ReadToEnd()))}finally{$r.Dispose();$g.Dispose();$m.Dispose()}";
		string command = PowerShellRuntime.CommandName + " -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String (Encoding.Unicode.GetBytes (PowerShellRuntime.RequireSupportedVersion (bootstrap)));
		if (command.Length > 7900)
			throw new ArgumentException ("Observer arguments exceed the remote Windows command limit.");
		return command;
		}

	public static async Task<SubmissionEnduranceHealthSnapshot> ReadLocalAsync (SubmissionEnduranceWindowsTask task,
		CancellationToken token = default)
		{
		if (!OperatingSystem.IsWindows ())
			throw new PlatformNotSupportedException ("Local task observation requires Windows.");
		// Local process arguments are not subject to the remote cmd.exe limit.
		// Keep the script readable instead of decompressing executable text at runtime.
		string script = PowerShellRuntime.RequireSupportedVersion (Script (task));
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (TimeSpan.FromSeconds (30));
		var start = new ProcessStartInfo (PowerShellRuntime.LocalExecutable)
			{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
			};
		foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
			start.ArgumentList.Add (argument);
		using var process = Process.Start (start) ?? throw new IOException ("The passive Windows observer could not start.");
		try
			{
			var output = ReadBoundedAsync (process.StandardOutput, deadline.Token);
			var error = ReadBoundedAsync (process.StandardError, deadline.Token);
			await Task.WhenAll (output, error, process.WaitForExitAsync (deadline.Token)).ConfigureAwait (false);
			return Parse (process.ExitCode, await output, await error);
			}
		finally { if (!process.HasExited) { process.Kill (entireProcessTree: true); await process.WaitForExitAsync (CancellationToken.None).ConfigureAwait (false); } }
		}

	public static async Task<SubmissionEnduranceHealthSnapshot> ReadRemoteAsync (SubmissionEnduranceWindowsTask task,
		SubmissionEnduranceObserverEndpoint endpoint, NetworkCredential credential, CancellationToken token = default)
		{
		ArgumentNullException.ThrowIfNull (endpoint);
		ArgumentNullException.ThrowIfNull (credential);
		if (Uri.CheckHostName (endpoint.Host) == UriHostNameType.Unknown || endpoint.Port is < 1 or > 65535 ||
			string.IsNullOrWhiteSpace (endpoint.HostKeyFingerprint) || string.IsNullOrWhiteSpace (credential.UserName) || string.IsNullOrEmpty (credential.Password) || !string.IsNullOrEmpty (credential.Domain) ||
			endpoint.HostKeyAlgorithm is not ("ssh-ed25519" or "ecdsa-sha2-nistp256" or "ecdsa-sha2-nistp384" or "rsa-sha2-256" or "rsa-sha2-512"))
			throw new ArgumentException ("Configure explicit Windows SSH credentials, host, port and trusted host-key algorithm/fingerprint.");
		string commandText = EncodedCommand (task);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (TimeSpan.FromSeconds (30));
		var connection = new PasswordConnectionInfo (endpoint.Host, endpoint.Port, credential.UserName, credential.Password) { Timeout = TimeSpan.FromSeconds (15) };
		if (!connection.HostKeyAlgorithms.TryGetValue (endpoint.HostKeyAlgorithm, out var algorithm))
			throw new ArgumentException ("The selected SSH host-key algorithm is unavailable.");
		connection.HostKeyAlgorithms.Clear ();
		connection.HostKeyAlgorithms.Add (endpoint.HostKeyAlgorithm, algorithm);
		using var client = new SshClient (connection);
		client.HostKeyReceived += (_, e) => e.CanTrust = e.HostKeyName == endpoint.HostKeyAlgorithm && e.FingerPrintSHA256 == endpoint.HostKeyFingerprint;
		await client.ConnectAsync (deadline.Token).ConfigureAwait (false);
		using var command = client.CreateCommand (commandText);
		command.CommandTimeout = TimeSpan.FromSeconds (20);
		await command.ExecuteAsync (deadline.Token).ConfigureAwait (false);
		return Parse (command.ExitStatus ?? -1, command.Result, command.Error);
		}

	internal static SubmissionEnduranceHealthSnapshot Parse (int exitCode, string output, string error)
		{
		if (exitCode != 0 || !string.IsNullOrWhiteSpace (error) || output.Length > OutputLimit)
			throw new InvalidDataException ("The passive Windows observation did not complete; do not reuse an old snapshot.");
		try
			{
			return JsonSerializer.Deserialize<SubmissionEnduranceHealthSnapshot> (output, JsonOptions)
				?? throw new InvalidDataException ("The passive Windows observation was empty.");
			}
		catch (JsonException) { throw new InvalidDataException ("The passive Windows observation was malformed."); }
		}
	private static async Task<string> ReadBoundedAsync (TextReader reader, CancellationToken token)
		{
		var text = new StringBuilder ();
		var buffer = new char[4096];
		int count;
		while ((count = await reader.ReadAsync (buffer, token).ConfigureAwait (false)) != 0)
			{
			if (text.Length + count > OutputLimit)
				throw new InvalidDataException ("Passive observation output exceeds its limit.");
			text.Append (buffer, 0, count);
			}
		return text.ToString ();
		}
	}
