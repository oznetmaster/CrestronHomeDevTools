// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

internal static class WindowsSshSetupCommand
	{
	internal static async Task<int> RunAsync (string[] args, TextWriter output, TextWriter error, CancellationToken token)
		{
		try
			{
			bool prepare = args[0] == "prepare-ssh";
			string[] allowed = prepare ? ["--remote-address", "--output"] : ["--plan", "--sha256", "--state", "--apply-reviewed", "--resume-after-inspection"];
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (int i = 1; i < args.Length; i += 2)
				if (i + 1 >= args.Length || !allowed.Contains (args[i]) || !options.TryAdd (args[i], args[i + 1]))
					throw new ArgumentException ("Invalid SSH setup options.");
			string Required (string name) => options.GetValueOrDefault (name) ?? throw new ArgumentException ($"Missing {name}.");
			var json = new JsonSerializerOptions { WriteIndented = true };
			if (prepare)
				{
				var plan = new WindowsSshSetupPlan (1, Environment.MachineName, Required ("--remote-address"));
				string hash = WindowsSshSetup.Digest (plan);
				using (var file = new FileStream (Required ("--output"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
					JsonSerializer.Serialize (file, plan, json);
				await output.WriteLineAsync (JsonSerializer.Serialize (new
					{
					Plan = plan,
					PlanSha256 = hash,
					Actions = new[] { "Install Windows OpenSSH Server if missing", "Start sshd and set Automatic startup", "Scope the DevTools SSH rule and standard OpenSSH firewall rule to the selected source" },
					AutomaticReboot = false,
					OtherFirewallRulesChanged = false,
					AdministratorRequired = true
					}, json));
				return 0;
				}
			if (Required ("--apply-reviewed") != "true")
				throw new ArgumentException ("Explicit reviewed setup execution is required.");
			bool resume = options.GetValueOrDefault ("--resume-after-inspection", "false") switch
				{
					"true" => true,
					"false" => false,
					_ => throw new ArgumentException ("Use true or false for resume-after-inspection.")
					};
			WindowsSshSetupPlan reviewed;
			using (var file = File.OpenRead (Required ("--plan")))
				{
				if (file.Length > 65536)
					throw new InvalidDataException ("Oversized setup plan.");
				reviewed = JsonSerializer.Deserialize<WindowsSshSetupPlan> (file) ?? throw new InvalidDataException ("Empty setup plan.");
				}
			var state = await WindowsSshSetup.ApplyAsync (reviewed, Required ("--sha256"), Required ("--state"), new ReportProgress (error), token, resume);
			await output.WriteLineAsync (JsonSerializer.Serialize (state, json));
			return state.State switch
				{
					"Completed" => 0,
					"RestartRequired" => 3010,
					_ => 3
					};
			}
		catch (Exception failure) when (failure is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or JsonException or OperationCanceledException or System.ComponentModel.Win32Exception)
			{
			await error.WriteLineAsync ("SSH setup did not complete. Retain the state directory and inspect administrator access, the reviewed plan and Windows servicing. Use resources --help. No automatic retry or reboot occurs.");
			return 2;
			}
		}
	private sealed class ReportProgress (TextWriter writer) : IProgress<string>
		{
		public void Report (string value) => writer.WriteLine (value);
		}
	}