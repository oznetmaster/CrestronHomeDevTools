// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

using Renci.SshNet;

namespace CrestronHomeDevTools;

/// <summary>The local start timestamp is diagnostic only and can vary by a second between reads.</summary>
public sealed record ProcessorUptimeSnapshot (TimeSpan Uptime, DateTime LocalStartedAt, DateTimeOffset RequestSentUtc, DateTimeOffset ObservedUtc)
	{
	// Bounds account for request latency and the console's hundredth-second duration precision.
	// Consumers must pin one original window, not move it forward after each observation.
	public DateTimeOffset EarliestStartUtc => RequestSentUtc - Uptime - TimeSpan.FromMilliseconds (10);
	public DateTimeOffset LatestStartUtc => ObservedUtc - Uptime + TimeSpan.FromMilliseconds (10);
	}

/// <summary>Reads one uptime response over authenticated, pinned SSH without changing processor state.</summary>
public static class ProcessorUptime
	{
	// Background log output can begin immediately after '>' without a newline.
	private static readonly Regex Prompt = new (@"(?:^|[\r\n])[A-Za-z0-9][A-Za-z0-9_.:-]{0,80}>", RegexOptions.CultureInvariant);
	private static readonly Regex Duration = new (@"^The system has been running for ([0-9]+) days ([0-9]{2}):([0-9]{2}):([0-9]{2})\.([0-9]{2})$", RegexOptions.CultureInvariant);
	private const string StartPrefix = "The system last started on: ";

	public static async Task<ProcessorUptimeSnapshot> ReadAsync (string host, NetworkCredential credential,
		string sshFingerprint, TimeSpan timeout, CancellationToken token = default)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (host);
		ArgumentNullException.ThrowIfNull (credential);
		ArgumentException.ThrowIfNullOrWhiteSpace (sshFingerprint);
		ValidateTimeout (timeout);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (timeout);
		using var client = new SshClient (host, credential.UserName, credential.Password);
		client.ConnectionInfo.Timeout = timeout;
		client.HostKeyReceived += (_, e) => e.CanTrust = string.Equals (e.FingerPrintSHA256, sshFingerprint, StringComparison.Ordinal);
		await client.ConnectAsync (deadline.Token).ConfigureAwait (false);
		using var shell = client.CreateShellStream ("xterm", 100, 24, 800, 600, 65536);
		return await ReadCoreAsync (new ConsoleSession (client, shell), timeout, deadline.Token).ConfigureAwait (false);
		}

	internal static async Task<ProcessorUptimeSnapshot> ReadCoreAsync (IUptimeSession session, TimeSpan timeout, CancellationToken token)
		{
		ValidateTimeout (timeout);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (timeout);
		var buffer = new StringBuilder ();
		bool sent = false;
		DateTimeOffset requestSentUtc = default;
		while (true)
			{
			deadline.Token.ThrowIfCancellationRequested ();
			if (!session.IsConnected) throw new IOException ("Processor disconnected before uptime was verified.");
			buffer.Append (session.ReadAvailable ());
			if (buffer.Length > 65536) throw new InvalidDataException ("Processor uptime response exceeded its limit.");
			string text = buffer.ToString ();
			if (!sent && Prompt.IsMatch (text))
				{
				// Discard every pre-command byte, including any earlier console output.
				buffer.Clear ();
				deadline.Token.ThrowIfCancellationRequested ();
				sent = true;
				requestSentUtc = DateTimeOffset.UtcNow;
				session.WriteLine ("uptime");
				}
			else if (sent && TryParse (text, requestSentUtc, DateTimeOffset.UtcNow, out var result)) return result!;
			await Task.Delay (50, deadline.Token).ConfigureAwait (false);
			}
		}

	internal static bool TryParse (string text, DateTimeOffset requestSentUtc, DateTimeOffset observedUtc, out ProcessorUptimeSnapshot? result)
		{
		if (observedUtc < requestSentUtc) throw new InvalidDataException ("The observation clock moved backwards.");
		result = null;
		// Only newline-terminated complete console lines count. Log lines have prefixes and cannot impersonate them.
		string[] lines = text.Split ('\n')[..^1].Select (line => line.TrimEnd ('\r')).ToArray ();
		string[] durations = lines.Where (line => line.StartsWith ("The system has been running for ", StringComparison.Ordinal)).ToArray ();
		string[] starts = lines.Where (line => line.StartsWith (StartPrefix, StringComparison.Ordinal)).ToArray ();
		if (durations.Length > 1 || starts.Length > 1) throw new InvalidDataException ("Ambiguous processor uptime response.");
		if (durations.Length == 0 || starts.Length == 0) return false;
		var match = Duration.Match (durations[0]);
		if (!match.Success || !int.TryParse (match.Groups[1].Value, CultureInfo.InvariantCulture, out int days) || days > 100000 ||
			!DateTime.TryParseExact (starts[0][StartPrefix.Length..], "dddd, MMMM d, yyyy 'at' HH:mm:ss", CultureInfo.InvariantCulture,
				DateTimeStyles.None, out var start))
			throw new InvalidDataException ("Unrecognized processor uptime response.");
		int hours = int.Parse (match.Groups[2].Value, CultureInfo.InvariantCulture);
		int minutes = int.Parse (match.Groups[3].Value, CultureInfo.InvariantCulture);
		int seconds = int.Parse (match.Groups[4].Value, CultureInfo.InvariantCulture);
		if (hours > 23 || minutes > 59 || seconds > 59) throw new InvalidDataException ("Invalid processor uptime duration.");
		int hundredths = int.Parse (match.Groups[5].Value, CultureInfo.InvariantCulture);
		var uptime = new TimeSpan (days, hours, minutes, seconds, hundredths * 10);
		if (uptime + TimeSpan.FromMilliseconds (10) > requestSentUtc - DateTimeOffset.MinValue)
			throw new InvalidDataException ("The uptime lies outside the observation calendar.");
		result = new (uptime, DateTime.SpecifyKind (start, DateTimeKind.Unspecified), requestSentUtc, observedUtc);
		return true;
		}

	private static void ValidateTimeout (TimeSpan timeout)
		{
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (2)) throw new ArgumentOutOfRangeException (nameof (timeout));
		}

	private sealed class ConsoleSession (SshClient client, ShellStream shell) : IUptimeSession
		{
		public bool IsConnected => client.IsConnected;
		public string ReadAvailable () => shell.DataAvailable ? shell.Read () : string.Empty;
		public void WriteLine (string command) => shell.WriteLine (command);
		}
	}

internal interface IUptimeSession
	{
	bool IsConnected { get; }
	string ReadAvailable ();
	void WriteLine (string command);
	}