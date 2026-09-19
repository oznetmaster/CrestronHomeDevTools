// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

internal static class SubmissionEvidenceCompositionCommand
	{
	internal static int Run (string[] args, TextWriter output, TextWriter error)
		{
		if (args.Length == 0 || args is ["--help"])
			{
			output.WriteLine ("""
                submission-combine-evidence --evidence DIRECTORY --plan RELATIVE_FILE --plan-sha256 REVIEWED_SHA256
                  --policy RELATIVE_FILE
                  --output NEW_PRIVATE_DIRECTORY
                Combine a reviewed inventory of observation documents without changing outcomes or selecting duplicates.
                Input files must be retained inside the evidence directory. Output must be a new directory.
                Exit 0 requires every policy scope. Exit 1 retains the incomplete report but no observations.json.
                Authenticate each producer and approve the complete source inventory and policy before pinning the plan.
                This command does not approve the full submission, sign a form or send anything.
                """);
			return 0;
			}
		try
			{
			string[] names = ["evidence", "plan", "plan-sha256", "policy", "output"];
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (var index = 0; index < args.Length; index += 2)
				if (index + 1 == args.Length || !args[index].StartsWith ("--", StringComparison.Ordinal) ||
					!names.Contains (args[index][2..], StringComparer.Ordinal) || string.IsNullOrWhiteSpace (args[index + 1]) ||
					!options.TryAdd (args[index][2..], args[index + 1]))
					throw new ArgumentException ("Supply each supported composition option exactly once; see --help.");
			if (options.Count != names.Length)
				throw new ArgumentException ("All composition options are required; see --help.");
			var directory = Path.GetFullPath (options["output"]);
			if (Path.Exists (directory) || !Directory.Exists (Path.GetDirectoryName (directory)))
				throw new ArgumentException ("Output must be a new directory with an existing parent.");
			var report = SubmissionEvidenceComposition.CombineFiles (options["evidence"], options["plan"], options["plan-sha256"],
				options["policy"], DateTimeOffset.UtcNow);
			Directory.CreateDirectory (directory);
			var json = new JsonSerializerOptions
				{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
				Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
				};
			void Write (string name, object value)
				{
				using var file = new FileStream (Path.Combine (directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
				JsonSerializer.Serialize (file, value, json);
				}
			Write ("report.json", report);
			if (report.CompositionChecksPassed)
				Write ("observations.json", report.Observations!);
			output.WriteLine (JsonSerializer.Serialize (new
				{
				report.CompositionChecksPassed, EvidenceIssues = report.Evidence.Issues.Count, SubmissionReady = false
				}));
			return report.CompositionChecksPassed ? 0 : 1;
			}
		catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
			{
			error.WriteLine (exception is ArgumentException or InvalidDataException ? exception.Message : "Cannot read or retain evidence composition inputs/results.");
			return 2;
			}
		}
	}