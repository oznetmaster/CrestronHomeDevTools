// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

internal static class SubmissionOutageEvidenceCommand
	{
	internal static int Run (string[] args, TextWriter output, TextWriter error)
		{
		if (args.Length == 0 || args is ["--help"])
			{
			output.WriteLine ("""
                submission-import-outage-evidence --evidence DIRECTORY
                  --plan RELATIVE_FILE --plan-sha256 REVIEWED_SHA256
                  --record RELATIVE_FILE --record-sha256 PRODUCER_SHA256
                  --policy RELATIVE_FILE --output NEW_PRIVATE_DIRECTORY
                Assess bounded interruption and functional-recovery measurements against a pinned plan.
                Preserve all input files and raw captures beneath the evidence directory.
                Output contains report.json and observations.json, including Partial/Failed outcomes.
                Exit 0: this scope passed; exit 1: incomplete or failed measurements; exit 2: invalid input.
                No hardware operation, signing or delivery. A pass is not full submission acceptance.
                """);
			return 0;
			}
		try
			{
			string[] names = ["evidence", "plan", "plan-sha256", "record", "record-sha256", "policy", "output"];
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (int i = 0; i < args.Length; i += 2)
				if (i + 1 == args.Length || !args[i].StartsWith ("--", StringComparison.Ordinal) ||
					 !names.Contains (args[i][2..], StringComparer.Ordinal) || string.IsNullOrWhiteSpace (args[i + 1]) ||
					 !options.TryAdd (args[i][2..], args[i + 1]))
					throw new ArgumentException ("Supply each outage evidence option exactly once; see --help.");
			if (options.Count != names.Length)
				throw new ArgumentException ("All outage evidence options are required; see --help.");
			var directory = Path.GetFullPath (options["output"]);
			if (Path.Exists (directory) || !Directory.Exists (Path.GetDirectoryName (directory)))
				throw new ArgumentException ("Use a new output directory with an existing private parent.");
			var result = SubmissionOutageEvidence.ImportFiles (options["evidence"], options["plan"], options["plan-sha256"],
				 options["record"], options["record-sha256"], options["policy"], DateTimeOffset.UtcNow);
			Directory.CreateDirectory (directory);
			var json = new JsonSerializerOptions
				{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				WriteIndented = true,
				Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
				};
			void Write (string name, object value)
				{
				using var file = new FileStream (Path.Combine (directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
				JsonSerializer.Serialize (file, value, json);
				}
			Write ("report.json", result.Measurements);
			Write ("observations.json", result.Observations);
			output.WriteLine (JsonSerializer.Serialize (new
				{
				result.Measurements.Outcome,
				result.Measurements.MeasurementChecksPassed,
				SubmissionReady = false
				}, json));
			return result.Measurements.MeasurementChecksPassed ? 0 : 1;
			}
		catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
			{
			error.WriteLine (exception is ArgumentException or InvalidDataException ? exception.Message : "Cannot import the private outage evidence.");
			return 2;
			}
		}
	}