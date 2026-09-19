// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

internal static class SubmissionReviewAssessmentCommand
	{
	internal static int Run (string[] args, TextWriter output, TextWriter error)
		{
		if (args.Length == 0 || args is ["--help"])
			{
			output.WriteLine ("""
                submission-assess-review --candidate FILE --candidate-sha256 REVIEWED_SHA256
                  --package FILE --policy FILE --template FILE --observations FILE --evidence DIRECTORY
                  --mode complete|declared-gaps --declarations FILE --declarations-sha256 REVIEWED_SHA256
                Assess the entire interpreted requirement policy, preserving original results and every gap.
                Complete is against our interpretation of Crestron's requirements, never Crestron acceptance.
                All options are required. Complete mode requires an empty, candidate-bound declarations list.
                Output is private JSON: 0 means eligible for further review, 1 needs correction, 2 invalid input.
                Exit 0 does not mean all tests passed; inspect verificationStatus and the original validation.
                Authenticate producers and approve full policy coverage separately. No form is signed or sent.
                """);
			return 0;
			}
		try
			{
			string[] names = ["candidate", "candidate-sha256", "package", "policy", "template", "observations", "evidence", "mode", "declarations", "declarations-sha256"];
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (var index = 0; index < args.Length; index += 2)
				if (index + 1 == args.Length || !args[index].StartsWith ("--", StringComparison.Ordinal) ||
					!names.Contains (args[index][2..], StringComparer.Ordinal) || string.IsNullOrWhiteSpace (args[index + 1]) ||
					!options.TryAdd (args[index][2..], args[index + 1]))
					throw new ArgumentException ("Supply each supported review option exactly once; see --help.");
			if (options.Count != names.Length) throw new ArgumentException ("All review assessment options are required; see --help.");
			var mode = options["mode"] switch
				{
					"complete" => SubmissionReviewMode.Complete,
					"declared-gaps" => SubmissionReviewMode.DeclaredGaps,
					_ => throw new ArgumentException ("Review mode must be complete or declared-gaps.")
				};
			var report = SubmissionReviewFiles.Check (options["candidate"], options["candidate-sha256"], options["package"], options["policy"],
				options["template"], options["observations"], options["evidence"], options["declarations"], options["declarations-sha256"], mode, DateTimeOffset.UtcNow);
			output.WriteLine (JsonSerializer.Serialize (report, new JsonSerializerOptions
				{
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
				Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
				}));
			return report.ReadyForReview ? 0 : 1;
			}
		catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
			{
			error.WriteLine (exception is ArgumentException or InvalidDataException ? exception.Message : "Cannot read or assess the private submission inputs.");
			return 2;
			}
		}
	}