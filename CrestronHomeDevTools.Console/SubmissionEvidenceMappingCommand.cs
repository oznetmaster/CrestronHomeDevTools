// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

internal static class SubmissionEvidenceMappingCommand
	{
	internal static int Run (string[] args, TextWriter output, TextWriter error)
		{
		if (args.Length == 0 || args is ["--help"])
			{
			output.WriteLine ("""
                submission-map-evidence --evidence DIRECTORY --mapping RELATIVE_FILE --mapping-sha256 REVIEWED_SHA256
                  --source-policy RELATIVE_FILE --source-observations RELATIVE_FILE --destination-policy RELATIVE_FILE
                  --output NEW_PRIVATE_DIRECTORY
                Import a reviewed one-to-one mapping without changing measured results or original files.
                Input files must be retained inside the evidence directory. Output must be a new directory.
                Exit 0 validates only the mapped subset; report.json lists remaining requirement IDs.
                Authenticate the source producer and review behavioral equivalence before approving the mapping.
                This command does not approve the full submission, sign a form or send anything.
                """);
			return 0;
			}
		try
			{
			string[] names = ["evidence", "mapping", "mapping-sha256", "source-policy", "source-observations", "destination-policy", "output"];
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (var index = 0; index < args.Length; index += 2)
				if (index + 1 == args.Length || !args[index].StartsWith ("--", StringComparison.Ordinal) ||
					!names.Contains (args[index][2..], StringComparer.Ordinal) || string.IsNullOrWhiteSpace (args[index + 1]) ||
					!options.TryAdd (args[index][2..], args[index + 1]))
					throw new ArgumentException ("Supply each supported mapping option exactly once; see --help.");
			if (options.Count != names.Length)
				throw new ArgumentException ("All mapping options are required; see --help.");
			var directory = Path.GetFullPath (options["output"]);
			if (Path.Exists (directory) || !Directory.Exists (Path.GetDirectoryName (directory)))
				throw new ArgumentException ("Output must be a new directory with an existing parent.");
			var report = SubmissionEvidenceMapping.MapFiles (options["evidence"], options["mapping"], options["mapping-sha256"],
				options["source-policy"], options["source-observations"], options["destination-policy"], DateTimeOffset.UtcNow);
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
			if (report.MappingChecksPassed)
				Write ("observations.json", report.Observations!);
			output.WriteLine (JsonSerializer.Serialize (new
				{
				report.MappingChecksPassed, UnmappedRequirements = report.UnmappedRequirementIds.Count, SubmissionReady = false
				}));
			return report.MappingChecksPassed ? 0 : 1;
			}
		catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
			{
			error.WriteLine (exception is ArgumentException or InvalidDataException ? exception.Message : "Cannot read or retain evidence mapping inputs/results.");
			return 2;
			}
		}
	}