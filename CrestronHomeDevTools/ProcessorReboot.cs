// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.RegularExpressions;

using Renci.SshNet;
using Renci.SshNet.Common;

namespace CrestronHomeDevTools;

/// <summary>The selected processor, shown to the person confirming a whole-system reboot.</summary>
public sealed record ProcessorRebootTarget (string Host, string? SystemName = null);

public enum ProcessorRebootStatus
	{
	Cancelled, Accepted, Unconfirmed
	}

/// <summary>Accepted means the console acknowledged the request, not that startup has completed.</summary>
public sealed record ProcessorRebootResult (ProcessorRebootStatus Status);

/// <summary>Requests a whole-processor reboot through authenticated, pinned SSH. Never retries.</summary>
public static class ProcessorReboot
	{
	/// <summary>Requires a caller-provided confirmation UI after SSH authentication and before submission.</summary>
	public static async Task<ProcessorRebootResult> RequestAsync (
		 ProcessorRebootTarget target, NetworkCredential credential, string sshFingerprint,
		 Func<ProcessorRebootTarget, CancellationToken, Task<bool>> confirm,
		 TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (target);
		ArgumentNullException.ThrowIfNull (credential);
		ArgumentNullException.ThrowIfNull (confirm);
		ArgumentException.ThrowIfNullOrWhiteSpace (target.Host);
		ArgumentException.ThrowIfNullOrWhiteSpace (sshFingerprint);
		ValidateTimeout (timeout);
		using var client = new SshClient (target.Host, credential.UserName, credential.Password);
		client.ConnectionInfo.Timeout = timeout;
		client.HostKeyReceived += (_, e) => e.CanTrust = string.Equals (e.FingerPrintSHA256, sshFingerprint, StringComparison.Ordinal);
		using (var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken))
			{
			deadline.CancelAfter (timeout);
			await client.ConnectAsync (deadline.Token).ConfigureAwait (false);
			}
		using var shell = client.CreateShellStream ("xterm", 80, 24, 800, 600, 4096);
		return await RequestCoreAsync (target, new SshRebootSession (client, shell), confirm, timeout, cancellationToken).ConfigureAwait (false);
		}

	internal static async Task<ProcessorRebootResult> RequestCoreAsync (
		 ProcessorRebootTarget target, IRebootSession session,
		 Func<ProcessorRebootTarget, CancellationToken, Task<bool>> confirm,
		 TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		ValidateTimeout (timeout);
		await WaitForAsync (session, text => Regex.IsMatch (text, @"(?:^|[\r\n])[^\r\n]*>\s*$"), timeout, cancellationToken).ConfigureAwait (false);
		if (!await confirm (target, cancellationToken).ConfigureAwait (false))
			return new (ProcessorRebootStatus.Cancelled);
		cancellationToken.ThrowIfCancellationRequested ();
		try
			{
			// Once this write is attempted, a disconnect or cancellation cannot prove it was not executed.
			session.WriteLine ("REBOOT");
			await WaitForAsync (session, text => Regex.IsMatch (text, @"Rebooting system\.\s+Please wait\.\.\."), timeout, cancellationToken).ConfigureAwait (false);
			return new (ProcessorRebootStatus.Accepted);
			}
		catch (Exception exception) when (exception is IOException or SshException or OperationCanceledException or TimeoutException or ObjectDisposedException)
			{
			return new (ProcessorRebootStatus.Unconfirmed);
			}
		}

	private static void ValidateTimeout (TimeSpan timeout)
		{
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromHours (1))
			throw new ArgumentOutOfRangeException (nameof (timeout));
		}

	private static async Task WaitForAsync (IRebootSession session, Func<string, bool> matches, TimeSpan timeout, CancellationToken cancellationToken)
		{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (timeout);
		var text = string.Empty;
		try
			{
			while (true)
				{
				deadline.Token.ThrowIfCancellationRequested ();
				text += session.ReadAvailable ();
				if (matches (text))
					return;
				if (text.Length > 65536)
					text = text[^32768..];
				if (!session.IsConnected)
					throw new IOException ("Processor SSH connection closed.");
				await Task.Delay (50, deadline.Token).ConfigureAwait (false);
				}
			}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
			throw new TimeoutException ("The processor console did not respond before the deadline.");
			}
		}

	private sealed class SshRebootSession (SshClient client, ShellStream shell) : IRebootSession
		{
		public bool IsConnected => client.IsConnected;
		public string ReadAvailable () => shell.DataAvailable ? shell.Read () : string.Empty;
		public void WriteLine (string command) => shell.WriteLine (command);
		}
	}

internal interface IRebootSession
	{
	bool IsConnected
		{
		get;
		}
	string ReadAvailable ();
	void WriteLine (string command);
	}