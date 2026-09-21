// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Serialization;

using CrestronHomeDevTools;

internal static class ResourceSetupCommand
	{
	internal static async Task<int> RunAsync (string[] args, TextWriter output, TextWriter error, CancellationToken token)
		{
		if (args.FirstOrDefault () is "prepare-ssh" or "apply-ssh")
			return await WindowsSshSetupCommand.RunAsync (args, output, error, token);
		if (args.FirstOrDefault () is "prepare-runner" or "apply-runner" or "inspect-runner")
			return await WindowsRunnerSetupCommand.RunAsync (args, output, error, token);
		if (args.Length == 0 || args.SequenceEqual (["--help"]))
			{
			await output.WriteLineAsync ("""
				resources inspect-windows --work-directory DIRECTORY [--requirements PRIVATE_JSON] [--output NEW_PRIVATE_JSON]
				  Read this Windows computer's hardware, session, SDK and service state. Never installs or changes anything.
				  A requirements file specifies your combined workload budget. Rehearsal remains necessary.
				resources check --inventory PRIVATE_JSON
				  Validate the named processor and Windows computer inventory.
				resources select --inventory PRIVATE_JSON --kind CrestronProcessor|WindowsComputer --role ROLE [--capabilities NAME,NAME] [--name NAME]
				  Select one ready, permitted resource with recorded capabilities. Does not reserve or contact it.
				resources prepare-ssh --remote-address IP_OR_LocalSubnet --output NEW_PLAN_JSON
				  Prepare a reviewable plan for OpenSSH Server, automatic service startup and scoped firewall rules; no changes.
				resources apply-ssh --plan PLAN_JSON --sha256 REVIEWED_DIGEST --state PRIVATE_DIRECTORY --apply-reviewed true [--resume-after-inspection true]
				  Apply the reviewed plan in an elevated terminal. Exit 3010 means reboot is needed; run the same command afterwards.
				  Never reboots automatically. Use resume-after-inspection only after investigating an interrupted installation.
				resources prepare-runner --url GITHUB_URL --name NAME --version A.B.C --archive-sha256 TRUSTED_HASH --directory NEW_DIRECTORY --account WINDOWS_ACCOUNT --labels LABEL,LABEL --output NEW_PLAN_JSON
				  Prepare a reviewed GitHub runner/service plan for this computer. Does not register or install anything.
				resources apply-runner --plan PLAN_JSON --sha256 REVIEWED_DIGEST --state PRIVATE_DIRECTORY --apply-reviewed true
				  Apply in an elevated terminal; prompt for the short-lived registration token or read protected JSON stdin.
				  Never replaces existing runners or retries registration. Verify GitHub online status and a test job afterwards.
				resources inspect-runner --plan PLAN_JSON
				  Read the selected local runner configuration and service without changing anything or contacting GitHub.
				""");
			return 0;
			}
		try
			{
			var allowed = args[0] switch
				{
					"inspect-windows" => new[] { "--work-directory", "--requirements", "--output" },
					"check" => ["--inventory"],
					"select" => ["--inventory", "--kind", "--role", "--capabilities", "--name"],
					_ => throw new ArgumentException ("Unknown resource command.")
					};
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (int i = 1; i < args.Length; i += 2)
				if (i + 1 >= args.Length || !allowed.Contains (args[i]) || !options.TryAdd (args[i], args[i + 1]))
					throw new ArgumentException ("Invalid resource options.");
			string Required (string key) => options.GetValueOrDefault (key) ?? throw new ArgumentException ($"Missing {key}.");
			var json = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter () } };
			object result;
			int exit = 0;
			if (args[0] == "inspect-windows")
				{
				var snapshot = await WindowsHostAssessment.ReadLocalAsync (Required ("--work-directory"), token);
				WindowsHostAssessmentResult? assessment = null;
				if (options.TryGetValue ("--requirements", out var path))
					{
					using var file = File.OpenRead (path);
					if (file.Length > 65536)
						throw new InvalidDataException ("Workload requirements exceed their size limit.");
					var requirements = JsonSerializer.Deserialize<WindowsWorkloadRequirements> (file, json) ?? throw new InvalidDataException ("Empty workload requirements.");
					assessment = WindowsHostAssessment.Evaluate (snapshot, requirements);
					exit = assessment.DeclaredPrerequisitesSatisfied ? 0 : 3;
					}
				result = new
					{
					Snapshot = snapshot,
					Assessment = assessment,
					WorkloadRehearsalRequired = true
					};
				}
			else
				{
				var inventory = DevToolsResourceInventory.Load (Required ("--inventory"));
				if (args[0] == "check")
					result = new
						{
						Valid = true,
						ResourceCount = inventory.Resources.Count
						};
				else
					{
					if (!Enum.TryParse<DevToolsResourceKind> (Required ("--kind"), true, out var kind) || !Enum.IsDefined (kind) ||
						!Enum.TryParse<DevToolsResourceRole> (Required ("--role"), true, out var role) || !Enum.IsDefined (role))
						throw new ArgumentException ("Unknown resource kind or role.");
					result = inventory.Select (kind, role, options.GetValueOrDefault ("--capabilities", "").Split (',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), options.GetValueOrDefault ("--name"));
					}
				}
			string report = JsonSerializer.Serialize (result, json);
			if (options.TryGetValue ("--output", out var destination))
				{
				using var file = new FileStream (destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				using var writer = new StreamWriter (file);
				await writer.WriteAsync (report.AsMemory (), token);
				}
			await output.WriteLineAsync (report);
			return exit;
			}
		catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or OperationCanceledException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
			{
			await error.WriteLineAsync ("Resource assessment did not complete. Check the private inputs, local directory and account access. No setup or resource operation was performed.");
			return 2;
			}
		}
	}