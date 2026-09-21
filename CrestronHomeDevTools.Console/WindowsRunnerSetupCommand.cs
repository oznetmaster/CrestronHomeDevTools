// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

internal static class WindowsRunnerSetupCommand
	{
	internal static async Task<int> RunAsync (string[] args, TextWriter output, TextWriter error, CancellationToken token)
		{
		try
			{
			bool prepare = args[0] == "prepare-runner";
			string[] allowed = prepare ? ["--url", "--name", "--version", "--archive-sha256", "--directory", "--account", "--labels", "--output"] : args[0] == "inspect-runner" ? ["--plan"] : ["--plan", "--sha256", "--state", "--apply-reviewed"];
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (int i = 1; i < args.Length; i += 2)
				if (i + 1 >= args.Length || !allowed.Contains (args[i]) || !options.TryAdd (args[i], args[i + 1]))
					throw new ArgumentException ("Invalid runner setup options.");
			string Required (string name) => options.GetValueOrDefault (name) ?? throw new ArgumentException ($"Missing {name}.");
			var json = new JsonSerializerOptions { WriteIndented = true };
			if (prepare)
				{
				var plan = new WindowsRunnerSetupPlan (1, Environment.MachineName, Required ("--url"), Required ("--name"), Required ("--version"), Required ("--archive-sha256"),
					Required ("--directory"), Required ("--account"), Required ("--labels").Split (',', StringSplitOptions.TrimEntries));
				string digest = WindowsRunnerSetup.Digest (plan);
				using (var file = new FileStream (Required ("--output"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
					JsonSerializer.Serialize (file, plan, json);
				await output.WriteLineAsync (JsonSerializer.Serialize (new
					{
					Plan = plan,
					PlanSha256 = digest,
					ArchiveUrl = WindowsRunnerSetup.ArchiveUrl (plan),
					WindowsService = WindowsRunnerSetup.ServiceName (plan),
					AdministratorRequired = true,
					Actions = new[] { "Download and verify the selected official runner archive", "Register a new runner at the selected GitHub destination", "Grant its service account logon and runner-folder access through GitHub's configurator", "Install and start an automatic Windows service" },
					ExistingRunnerReplacement = false,
					CredentialsCopied = false,
					GitHubOnlineVerified = false,
					WorkloadVerified = false
					}, json));
				return 0;
				}
			if (args[0] != "inspect-runner" && Required ("--apply-reviewed") != "true")
				throw new ArgumentException ("Reviewed execution must be explicit.");
			WindowsRunnerSetupPlan reviewed;
			using (var file = File.OpenRead (Required ("--plan")))
				{
				if (file.Length > 65536)
					throw new InvalidDataException ("Oversized runner plan.");
				reviewed = JsonSerializer.Deserialize<WindowsRunnerSetupPlan> (file) ?? throw new InvalidDataException ("Empty runner plan.");
				}
			if (args[0] == "inspect-runner")
				{
				await output.WriteLineAsync (JsonSerializer.Serialize (await WindowsRunnerSetup.InspectAsync (reviewed, token), json));
				return 0;
				}
			if (WindowsRunnerSetup.Digest (reviewed) != Required ("--sha256"))
				throw new InvalidDataException ("Reviewed plan digest mismatch.");
			WindowsRunnerSetupSecrets secrets;
			if (Console.IsInputRedirected)
				{
				char[] buffer = new char[65537];
				using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
				deadline.CancelAfter (TimeSpan.FromSeconds (30));
				try
					{
					int count = 0, read;
					while (count < buffer.Length && (read = await Console.In.ReadAsync (buffer.AsMemory (count), deadline.Token)) != 0)
						count += read;
					if (count == 0 || count == buffer.Length)
						throw new InvalidDataException ("Missing or oversized private setup input.");
					secrets = ProtectedJsonInput.Deserialize<WindowsRunnerSetupSecrets> (buffer.AsSpan (0, count), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException ("Missing setup input.");
					}
				finally { Array.Clear (buffer); }
				}
			else
				{
				await error.WriteAsync ("Fresh GitHub runner registration token (hidden): ");
				string registration = ProfileSetup.ReadPassword ();
				string? password = null;
				if (!reviewed.ServiceAccount.StartsWith (@"NT AUTHORITY\", StringComparison.OrdinalIgnoreCase) && !reviewed.ServiceAccount.Equals ("LocalSystem", StringComparison.OrdinalIgnoreCase))
					{
					await error.WriteAsync ("Password for the reviewed Windows service account (hidden): ");
					password = ProfileSetup.ReadPassword ();
					}
				secrets = new (registration, password);
				}
			var state = await WindowsRunnerSetup.ApplyAsync (reviewed, Required ("--sha256"), Required ("--state"), secrets, new ReportProgress (error), token);
			await output.WriteLineAsync (JsonSerializer.Serialize (state, json));
			return state.State == "Completed" ? 0 : 3;
			}
		catch (Exception failure) when (failure is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or JsonException or OperationCanceledException or System.ComponentModel.Win32Exception or HttpRequestException or System.Security.SecurityException)
			{
			await error.WriteLineAsync ("Runner setup was not confirmed. Inspect the private journal, runner diagnostics and GitHub registration before recovery. No automatic registration retry, replacement or reboot occurs. Use resources --help.");
			return 2;
			}
		}
	private sealed class ReportProgress (TextWriter writer) : IProgress<string>
		{
		public void Report (string value) => writer.WriteLine (value);
		}
	}
