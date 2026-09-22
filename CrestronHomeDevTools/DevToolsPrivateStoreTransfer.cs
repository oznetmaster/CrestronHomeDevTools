// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Renci.SshNet;

namespace CrestronHomeDevTools;

/// <summary>Explicit destination for one selected entry. The console must be trusted tooling installed by the operator.</summary>
public sealed record DevToolsPrivateStoreDestination (string Host, int Port, string SshFingerprint,
	string ConsolePath, string StoreDirectory, string EntryName, bool Replace = false);

public sealed partial class DevToolsPrivateStore
	{
	/// <summary>Send one entry over pinned SSH to protected stdin, where the destination applies its own Windows encryption.
	/// Never retries. A lost response can mean import completed; inspect the destination before explicitly replacing.</summary>
	public async Task ProvisionRemoteAsync (string name, DevToolsPrivateStoreDestination destination,
		NetworkCredential windowsCredential, CancellationToken token = default)
		{
		ArgumentNullException.ThrowIfNull (destination);
		ArgumentNullException.ThrowIfNull (windowsCredential);
		string commandText = TransferCommand (destination);
		if (string.IsNullOrWhiteSpace (windowsCredential.UserName) || string.IsNullOrEmpty (windowsCredential.Password) || !string.IsNullOrEmpty (windowsCredential.Domain))
			throw new ArgumentException ("Provide the explicit Windows SSH account.");
		byte[] payload = ExportSelectedEntry (name, destination.EntryName);
		try
			{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (60));
			using var connection = new PasswordConnectionInfo (destination.Host, destination.Port, windowsCredential.UserName, windowsCredential.Password)
				{
				Timeout = TimeSpan.FromSeconds (15)
				};
			using var client = new SshClient (connection);
			client.HostKeyReceived += (_, e) => e.CanTrust = e.FingerPrintSHA256 == destination.SshFingerprint;
			await client.ConnectAsync (deadline.Token).ConfigureAwait (false);
			using var command = client.CreateCommand (commandText);
			command.CommandTimeout = TimeSpan.FromSeconds (45);
			Task running = command.ExecuteAsync (deadline.Token);
			try
				{
				using (Stream input = command.CreateInputStream ())
					await input.WriteAsync (payload, deadline.Token).ConfigureAwait (false);
				await running.ConfigureAwait (false);
				if (command.ExitStatus != 0 || command.Result.Trim () != "private-entry-imported")
					throw new IOException ("Remote import was not confirmed. Inspect the destination before retrying.");
				}
			finally
				{
				if (!running.IsCompleted)
					{
					deadline.Cancel ();
					try
						{
						await running.ConfigureAwait (false);
						}
					catch { /* Preserve the original transfer failure. */ }
					}
				}
			}
		finally { CryptographicOperations.ZeroMemory (payload); }
		}

	internal byte[] ExportSelectedEntry (string name, string destinationName)
		{
		_ = GetPath (destinationName);
		var entry = Load (name);
		try
			{
			return JsonSerializer.SerializeToUtf8Bytes (entry with
				{
				Name = destinationName
				}, Json);
			}
		finally { if (entry.Signature != null) CryptographicOperations.ZeroMemory (entry.Signature); }
		}

	/// <summary>Receive a selected entry on a trusted, protected stream. Saving is not authority to use it.</summary>
	public async Task ReceiveAsync (string name, Stream protectedInput, bool replace = false, CancellationToken token = default)
		{
		_ = GetPath (name);
		ArgumentNullException.ThrowIfNull (protectedInput);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (TimeSpan.FromSeconds (30));
		// Bounded to cover the maximum signature plus JSON/base64 overhead.
		byte[] buffer = new byte[12582913];
		try
			{
			int count = 0, read;
			while (count < buffer.Length && (read = await protectedInput.ReadAsync (buffer.AsMemory (count), deadline.Token).ConfigureAwait (false)) != 0)
				count += read;
			if (count == 0 || count == buffer.Length)
				throw new InvalidDataException ("Missing or oversized private entry.");
			var entry = JsonSerializer.Deserialize<Entry> (buffer.AsSpan (0, count), Json) ?? throw new InvalidDataException ("Empty private entry.");
			try
				{
				if (entry.SchemaVersion != 1 || entry.Name != name || (entry.Credential == null) == (entry.Signature == null))
					throw new InvalidDataException ("The transferred entry does not match the selected destination.");
				if (entry.Credential != null)
					SaveCredential (name, entry.Credential, replace);
				else
					SaveSignature (name, entry.Signature!, entry.Extension ?? "", replace);
				}
			finally { if (entry.Signature != null) CryptographicOperations.ZeroMemory (entry.Signature); }
			}
		finally { CryptographicOperations.ZeroMemory (buffer); }
		}

	internal static string TransferCommand (DevToolsPrivateStoreDestination destination)
		{
		if (Uri.CheckHostName (destination.Host) == UriHostNameType.Unknown || destination.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace (destination.SshFingerprint) ||
			string.IsNullOrEmpty (destination.EntryName) || destination.EntryName.Length > 64 || !destination.EntryName.All (c => char.IsAsciiLetterOrDigit (c) || c is '-' or '_'))
			throw new ArgumentException ("Provide a destination host, port, verified SSH fingerprint and entry name.");
		foreach (string path in new[] { destination.ConsolePath, destination.StoreDirectory })
			if (string.IsNullOrWhiteSpace (path) || path.Length > 2048 || path.Length < 3 || !char.IsAsciiLetter (path[0]) || path[1] != ':' || path[2] != '\\' || path.Any (c => char.IsControl (c) || c == '"'))
				throw new ArgumentException ("Use absolute local Windows destination paths without quotes or control characters.");
		if (!destination.ConsolePath.EndsWith (".exe", StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException ("Use the trusted complete Windows console executable.");
		static string Quote (string value) => "'" + value.Replace ("'", "''", StringComparison.Ordinal) + "'";
		// Only paths and entry references appear in the command. Secret bytes flow through encrypted SSH stdin.
		string script = $$"""
			$ErrorActionPreference = 'Stop'
			$process = $null
			try {
			    $start = New-Object System.Diagnostics.ProcessStartInfo
			    $start.FileName = {{Quote (destination.ConsolePath)}}
			    $start.Arguments = {{Quote ("credentials receive --store \"" + destination.StoreDirectory.TrimEnd ('\\') + "\" --name " + destination.EntryName + " --replace " + (destination.Replace ? "true" : "false"))}}
			    $start.UseShellExecute = $false
			    $start.CreateNoWindow = $true
			    $start.RedirectStandardInput = $true
			    $start.RedirectStandardOutput = $true
			    $start.RedirectStandardError = $true
			    $process = [System.Diagnostics.Process]::Start($start)
			    $out = $process.StandardOutput.ReadToEndAsync()
			    $err = $process.StandardError.ReadToEndAsync()
			    [Console]::OpenStandardInput().CopyTo($process.StandardInput.BaseStream)
			    $process.StandardInput.Close()
			    if (-not $process.WaitForExit(35000)) { throw 'Import timed out.' }
			    if ($process.ExitCode -ne 0 -or $out.Result.Trim() -ne 'private-entry-imported') { throw 'Import not confirmed.' }
			    [Console]::Out.WriteLine('private-entry-imported')
			} catch {
			    [Console]::Error.WriteLine('Remote private entry import not confirmed; inspect before retrying.')
			    exit 2
			} finally {
			    if ($null -ne $process) {
			        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
			        $process.Dispose()
			    }
			}
			""";
		return PowerShellRuntime.CommandName + " -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String (Encoding.Unicode.GetBytes (PowerShellRuntime.RequireSupportedVersion (script)));
		}
	}