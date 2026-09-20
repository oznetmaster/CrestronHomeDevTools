// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

internal static class SubmissionPriorEvidenceCommand
	{
	internal static int Run (string[] args, TextWriter output, TextWriter error)
		{
		if (args.Length == 0 || args is ["--help"])
			{
			output.WriteLine ("""
                submission-import-prior-evidence --candidate FILE --candidate-sha256 REVIEWED_SHA256
                  --policy FILE --evidence DIRECTORY --output NEW_PRIVATE_JSON_FILE
                Import explicitly reviewed prior passing assertions without claiming new executions.
                The pinned target policy must name each approved source and change-impact review.
                Original evidence, requirements, times and identities are preserved and revalidated.
                Exit 0 validates only this subset. Compose it with fresh results and review the full policy.
                The caller approves behavioral equivalence and producer trust; no signature or delivery occurs.
                """);
			return 0;
			}
		try
			{
			string[] names = ["candidate", "candidate-sha256", "policy", "evidence", "output"];
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (var index = 0; index < args.Length; index += 2)
				if (index + 1 == args.Length || !args[index].StartsWith ("--", StringComparison.Ordinal) ||
					!names.Contains (args[index][2..], StringComparer.Ordinal) || string.IsNullOrWhiteSpace (args[index + 1]) ||
					!options.TryAdd (args[index][2..], args[index + 1]))
					throw new ArgumentException ("Supply each prior-evidence option exactly once; see --help.");
			if (options.Count != names.Length) throw new ArgumentException ("All prior-evidence options are required; see --help.");
			var path = Path.GetFullPath (options["output"]);
			if (Path.Exists (path) || !Directory.Exists (Path.GetDirectoryName (path)))
				throw new ArgumentException ("Use a new output file with an existing private parent directory.");
			var document = SubmissionPriorEvidence.ImportFiles (options["candidate"], options["candidate-sha256"],
				options["policy"], options["evidence"], DateTimeOffset.UtcNow);
			using var file = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			JsonSerializer.Serialize (file, document, new JsonSerializerOptions
				{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
				Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
				});
			output.WriteLine (JsonSerializer.Serialize (new { ReviewedPriorAssertions = document.Observations.Count, SubmissionReady = false }));
			return 0;
			}
		catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
			{
			error.WriteLine (exception is ArgumentException or InvalidDataException ? exception.Message : "Cannot import the private prior evidence.");
			return 2;
			}
		}
	}