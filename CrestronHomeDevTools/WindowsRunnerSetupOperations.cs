// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace CrestronHomeDevTools;

internal sealed class WindowsRunnerSetupOperations : IWindowsRunnerSetupOperations
	{
	public async Task<WindowsRunnerSetupObservation> InspectAsync (WindowsRunnerSetupPlan plan, CancellationToken token)
		{
		if (!OperatingSystem.IsWindows ())
			throw new PlatformNotSupportedException ();
		using var source = typeof (WindowsRunnerSetup).Assembly.GetManifestResourceStream ("CrestronHomeDevTools.WindowsRunnerInspection.ps1") ?? throw new IOException ("Runner inspection tool missing.");
		using var reader = new StreamReader (source);
		var start = new ProcessStartInfo (Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
			{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
			};
		start.Environment.Remove ("PSModulePath");
		foreach (string value in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String (Encoding.Unicode.GetBytes (await reader.ReadToEndAsync (token).ConfigureAwait (false))) })
			start.ArgumentList.Add (value);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (TimeSpan.FromSeconds (45));
		using var process = Process.Start (start) ?? throw new IOException ("Cannot inspect runner setup.");
		try
			{
			Task<string> output = process.StandardOutput.ReadToEndAsync (deadline.Token), error = process.StandardError.ReadToEndAsync (deadline.Token);
			await process.StandardInput.WriteAsync (JsonSerializer.Serialize (new
				{
				Plan = plan,
				ServiceName = WindowsRunnerSetup.ServiceName (plan)
				}).AsMemory (), deadline.Token).ConfigureAwait (false);
			process.StandardInput.Close ();
			await Task.WhenAll (output, error, process.WaitForExitAsync (deadline.Token)).ConfigureAwait (false);
			if (process.ExitCode != 0 || output.Result.Length > 65536)
				throw new IOException ("Runner inspection failed; check elevated Windows access and plan fields.");
			return JsonSerializer.Deserialize<WindowsRunnerSetupObservation> (output.Result) ?? throw new InvalidDataException ("Empty runner inspection.");
			}
		finally { if (!process.HasExited) { process.Kill (true); await process.WaitForExitAsync (CancellationToken.None).ConfigureAwait (false); } }
		}
	public async Task InstallAsync (WindowsRunnerSetupPlan plan, CancellationToken token)
		{
		if (!OperatingSystem.IsWindows ())
			throw new PlatformNotSupportedException ();
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (TimeSpan.FromMinutes (10));
		token = deadline.Token;
		using var identity = WindowsIdentity.GetCurrent ();
		if (!new WindowsPrincipal (identity).IsInRole (WindowsBuiltInRole.Administrator))
			throw new UnauthorizedAccessException ("Runner service installation requires elevation.");
		if (Directory.Exists (plan.InstallDirectory) || File.Exists (plan.InstallDirectory))
			throw new IOException ("Runner directory must be new.");
		var acl = new DirectorySecurity ();
		acl.SetAccessRuleProtection (true, false);
		foreach (string sid in new[] { identity.User!.Value, "S-1-5-18", "S-1-5-32-544" }.Distinct ())
			acl.AddAccessRule (new FileSystemAccessRule (new SecurityIdentifier (sid), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
		Directory.CreateDirectory (plan.InstallDirectory).SetAccessControl (acl);
		string archive = Path.Combine (plan.InstallDirectory, ".devtools-runner.zip");
		using var client = new HttpClient { Timeout = TimeSpan.FromMinutes (10) };
		using (var response = await client.GetAsync (WindowsRunnerSetup.ArchiveUrl (plan), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait (false))
			{
			response.EnsureSuccessStatusCode ();
			if (response.RequestMessage?.RequestUri?.Scheme != "https")
				throw new IOException ("Runner download must remain HTTPS.");
			await using var input = await response.Content.ReadAsStreamAsync (token).ConfigureAwait (false);
			await using var output = new FileStream (archive, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			byte[] buffer = new byte[65536];
			long total = 0;
			int read;
			while ((read = await input.ReadAsync (buffer, token).ConfigureAwait (false)) > 0)
				{
				total += read;
				if (total > 1024L * 1024 * 1024)
					throw new InvalidDataException ("Runner archive exceeds 1 GiB.");
				await output.WriteAsync (buffer.AsMemory (0, read), token).ConfigureAwait (false);
				}
			}
		await ExtractVerifiedAsync (archive, plan.ArchiveSha256, plan.InstallDirectory, token).ConfigureAwait (false);
		File.Delete (archive);
		}
	internal static async Task ExtractVerifiedAsync (string archive, string expectedHash, string directory, CancellationToken token)
		{
		await using (var bytes = File.OpenRead (archive))
			if (!Convert.ToHexString (await SHA256.HashDataAsync (bytes, token).ConfigureAwait (false)).Equals (expectedHash, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("Runner archive does not match the reviewed SHA-256.");
		using var zip = ZipFile.OpenRead (archive);
		string root = Path.TrimEndingDirectorySeparator (Path.GetFullPath (directory)) + Path.DirectorySeparatorChar;
		long size = 0;
		var seen = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		var entries = new List<(ZipArchiveEntry Entry, string Path)> ();
		foreach (var entry in zip.Entries)
			{
			token.ThrowIfCancellationRequested ();
			string name = entry.FullName;
			string path = Path.GetFullPath (Path.Combine (root, name));
			size += entry.Length;
			if (entries.Count > 50000 || size > 3L * 1024 * 1024 * 1024 || name.Contains (':') || name.Contains ('\\') ||
				name.TrimEnd ('/').Split ('/').Any (p => p is "" or "." or ".." || p.EndsWith ('.') || p.EndsWith (' ')) ||
				!path.StartsWith (root, StringComparison.OrdinalIgnoreCase) || !seen.Add (path) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
				throw new InvalidDataException ("Unsafe or oversized runner archive.");
			entries.Add ((entry, path));
			}
		if (!entries.Any (e => e.Entry.FullName == "bin/Runner.Listener.exe"))
			throw new InvalidDataException ("Runner executable missing from archive.");
		foreach (var (entry, path) in entries)
			{
			token.ThrowIfCancellationRequested ();
			if (entry.FullName.EndsWith ('/'))
				{
				Directory.CreateDirectory (path);
				continue;
				}
			Directory.CreateDirectory (Path.GetDirectoryName (path)!);
			entry.ExtractToFile (path, overwrite: false);
			}
		}
	internal static ProcessStartInfo ConfigurationStart (WindowsRunnerSetupPlan plan, WindowsRunnerSetupSecrets secrets)
		{
		var start = new ProcessStartInfo (Path.Combine (plan.InstallDirectory, "bin", "Runner.Listener.exe"))
			{
			WorkingDirectory = plan.InstallDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
			};
		foreach (var key in start.Environment.Keys.Where (key => key.StartsWith ("ACTIONS_RUNNER_INPUT_", StringComparison.OrdinalIgnoreCase)).ToArray ())
			start.Environment.Remove (key);
		foreach (string arg in new[] { "configure", "--unattended", "--url", plan.GitHubUrl, "--name", plan.RunnerName, "--work", "_work", "--labels", string.Join (',', plan.Labels), "--runasservice", "--windowslogonaccount", plan.ServiceAccount })
			start.ArgumentList.Add (arg);
		start.Environment["ACTIONS_RUNNER_INPUT_TOKEN"] = secrets.RegistrationToken;
		if (secrets.ServicePassword != null)
			start.Environment["ACTIONS_RUNNER_INPUT_WINDOWSLOGONPASSWORD"] = secrets.ServicePassword;
		return start;
		}
	public async Task ConfigureAsync (WindowsRunnerSetupPlan plan, WindowsRunnerSetupSecrets secrets, CancellationToken token)
		{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (TimeSpan.FromMinutes (5));
		var start = ConfigurationStart (plan, secrets);
		using var process = Process.Start (start) ?? throw new IOException ("Cannot start runner configuration.");
		start.Environment.Remove ("ACTIONS_RUNNER_INPUT_TOKEN");
		start.Environment.Remove ("ACTIONS_RUNNER_INPUT_WINDOWSLOGONPASSWORD");
		try
			{
			process.StandardInput.Close ();
			// Consume but never echo vendor output, which could include sensitive registration diagnostics.
			await Task.WhenAll (process.StandardOutput.BaseStream.CopyToAsync (Stream.Null, deadline.Token), process.StandardError.BaseStream.CopyToAsync (Stream.Null, deadline.Token), process.WaitForExitAsync (deadline.Token)).ConfigureAwait (false);
			if (process.ExitCode != 0)
				throw new IOException ("Runner configuration was not confirmed. Inspect private runner diagnostics and GitHub before recovery; registration is not retried.");
			}
		finally { if (!process.HasExited) { process.Kill (true); await process.WaitForExitAsync (CancellationToken.None).ConfigureAwait (false); } }
		}
	}
