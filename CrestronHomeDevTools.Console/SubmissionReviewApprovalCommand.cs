// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

/// <summary>Offline approval preview/verification. Never creates approval, connects to a provider or delivers anything.</summary>
internal static class SubmissionReviewApprovalCommand
	{
	private static readonly JsonSerializerOptions Options = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false,
		RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
		Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
		};

	internal static int Run (bool verify, string[] args, TextWriter output, TextWriter error)
		{
		if (args.Length == 0 || args is ["--help"])
			{
			output.WriteLine (verify
				? "submission-review-approval-check --plan FILE --plan-file-sha256 REVIEWED_SHA256 --approval FILE --approval-sha256 REVIEWED_SHA256 --output NEW_PRIVATE_JSON"
				: "submission-review-approval-preview --plan FILE --plan-file-sha256 REVIEWED_SHA256 --output NEW_PRIVATE_JSON");
			output.WriteLine ("Preview or verify independently approved packet and correspondence. No approval is created, evidence revalidated, or submission delivered.");
			return 0;
			}
		try
			{
			string[] names = verify ? ["plan", "plan-file-sha256", "approval", "approval-sha256", "output"] : ["plan", "plan-file-sha256", "output"];
			var options = new Dictionary<string, string> (StringComparer.Ordinal);
			for (int index = 0; index < args.Length; index += 2)
				if (index + 1 == args.Length || !args[index].StartsWith ("--", StringComparison.Ordinal) ||
					!names.Contains (args[index][2..], StringComparer.Ordinal) || string.IsNullOrWhiteSpace (args[index + 1]) ||
					!options.TryAdd (args[index][2..], args[index + 1]))
					throw new ArgumentException ("Supply each supported option exactly once; see --help.");
			if (options.Count != names.Length || !Path.IsPathFullyQualified (options["plan"]) || !Path.IsPathFullyQualified (options["output"]))
				throw new ArgumentException ("All options and absolute private input/output paths are required.");
			if ((File.GetAttributes (options["plan"]) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("Linked plans are unsupported.");
			using var input = new FileStream (options["plan"], FileMode.Open, FileAccess.Read, FileShare.Read);
			if (input.Length is <= 0 or > 1024 * 1024) throw new InvalidDataException ("The plan must fit within 1 MiB.");
			byte[] bytes = new byte[(int)input.Length];
			input.ReadExactly (bytes);
			if (input.ReadByte () != -1 || Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant () != options["plan-file-sha256"])
				throw new InvalidDataException ("The plan differs from the trusted pin.");
			var plan = JsonSerializer.Deserialize<SubmissionReviewDeliveryPlan> (bytes, Options) ?? throw new InvalidDataException ("Missing plan.");
			object report = verify
				? SubmissionReviewApproval.Verify (plan, options["approval"], options["approval-sha256"], DateTimeOffset.UtcNow)
				: SubmissionReviewApproval.Preview (plan);
			byte[] result = JsonSerializer.SerializeToUtf8Bytes (report, Options);
			using var file = new FileStream (options["output"], FileMode.CreateNew, FileAccess.Write, FileShare.None);
			file.Write (result); file.Flush (flushToDisk: true);
			output.WriteLine (verify ? "Exact approval verified privately. Artifact and evidence revalidation are still required before each delivery step."
				: "Packet and correspondence preview retained privately. No approval or delivery occurred.");
			return 0;
			}
		catch (Exception exception) when (exception is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
			{
			error.WriteLine ("Review approval operation failed. Inspect the private inputs; no submission was delivered.");
			return 2;
			}
		}
	}