// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

internal static class SubmissionReviewBundleCommand
	{
	internal static int Run (bool create, string[] args, TextWriter output, TextWriter error)
		{
		if (args.Length == 0 || args is ["--help"])
			{
			output.WriteLine (create ? """
                submission-review-bundle-create --output NEW_PRIVATE_ZIP --candidate FILE --candidate-sha256 REVIEWED_SHA256
                  --package FILE --policy FILE --template FILE --observations FILE --evidence DIRECTORY
                  --declarations FILE --declarations-sha256 REVIEWED_SHA256 --mode complete|declared-gaps
                Retain and revalidate the exact candidate, declared gaps and referenced evidence as a private review bundle.
                """ : """
                submission-review-bundle-check --bundle PRIVATE_ZIP --bundle-sha256 REVIEWED_SHA256
                  --candidate-sha256 REVIEWED_SHA256 --declarations-sha256 REVIEWED_SHA256
                  --mode complete|declared-gaps --scratch PRIVATE_DIRECTORY
                Revalidate the archive's own bytes against independently retained pins and explicit review mode.
                """);
			output.WriteLine ("Exit 0 means eligible for internal review, not all tests passed, authorized delivery or Crestron acceptance.");
			return 0;
			}
		try
			{
			string[] names = create
				? ["output", "candidate", "candidate-sha256", "package", "policy", "template", "observations", "evidence", "declarations", "declarations-sha256", "mode"]
				: ["bundle", "bundle-sha256", "candidate-sha256", "declarations-sha256", "mode", "scratch"];
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (var index = 0; index < args.Length; index += 2)
				if (index + 1 == args.Length || !args[index].StartsWith ("--", StringComparison.Ordinal) ||
					!names.Contains (args[index][2..], StringComparer.Ordinal) || string.IsNullOrWhiteSpace (args[index + 1]) ||
					!options.TryAdd (args[index][2..], args[index + 1]))
					throw new ArgumentException ("Supply each supported review bundle option exactly once; see --help.");
			if (options.Count != names.Length) throw new ArgumentException ("All review bundle options are required; see --help.");
			var mode = options["mode"] switch
				{
					"complete" => SubmissionReviewMode.Complete,
					"declared-gaps" => SubmissionReviewMode.DeclaredGaps,
					_ => throw new ArgumentException ("Review mode must be complete or declared-gaps.")
				};
			var report = create
				? SubmissionBundle.CreateReview (options["output"], options["candidate"], options["candidate-sha256"], options["package"],
					options["policy"], options["template"], options["observations"], options["evidence"], options["declarations"],
					options["declarations-sha256"], mode, DateTimeOffset.UtcNow)
				: SubmissionBundle.CheckReview (options["bundle"], options["bundle-sha256"], options["candidate-sha256"], options["scratch"],
					options["declarations-sha256"], mode, DateTimeOffset.UtcNow);
			output.WriteLine (JsonSerializer.Serialize (report, new JsonSerializerOptions
				{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
				Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
				}));
			return report.ReadyForReview ? 0 : 1;
			}
		catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
			{
			error.WriteLine (exception is ArgumentException or InvalidDataException ? exception.Message : "Cannot retain or check the private review bundle.");
			return 2;
			}
		}
	}