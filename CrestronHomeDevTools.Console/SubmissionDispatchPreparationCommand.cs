// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using CrestronHomeDevTools;

internal sealed record SubmissionDispatchPreparationSettings (int SchemaVersion, string PreparedDirectory,
	string PreparationSettingsPath, string DeliveryReviewSha256, string JournalDirectory, string AttemptsDirectory,
	string UploadReceiptDirectory, string ReviewedUploadFormSha256, string AcceptedUploadTermsSha256,
	string MailReceiptDirectory, string SmtpHost, int SmtpPort, int RevalidationTimeoutSeconds,
	int UploadTimeoutSeconds, int MailTimeoutSeconds);

/// <summary>Creates private dispatch settings for independent review. Never grants approval or contacts a provider.</summary>
internal static class SubmissionDispatchPreparationCommand
	{
	private static readonly JsonSerializerOptions Options = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		AllowDuplicateProperties = false,
		RespectNullableAnnotations = true,
		RespectRequiredConstructorParameters = true
		};

	internal static async Task<int> RunAsync (string[] args, TextWriter output, TextWriter error, CancellationToken token = default)
		{
		if (args is ["--help"] or ["help"] or ["-h"])
			{
			await output.WriteLineAsync ("submission-delivery-settings --settings PRIVATE_JSON --output NEW_PRIVATE_JSON\nCreate settings for review from this installed console and a completed delivery preparation. No credentials, approval, upload or email are created.");
			return 0;
			}
		try
			{
			if (args is not ["--settings", var source, "--output", var destination])
				throw new ArgumentException ("Specify private setup settings and a new private output file.");
			await SubmissionToolsCommand.VerifyInstalledBundleAsync (token);
			var settings = JsonSerializer.Deserialize<SubmissionDispatchPreparationSettings> (await ReadAsync (source, token), Options)
				?? throw new InvalidDataException ("Missing setup settings.");
			var prepared = await PrepareAsync (settings, AppContext.BaseDirectory, destination, token);
			byte[] bytes = JsonSerializer.SerializeToUtf8Bytes (prepared, Options);
			await using (var file = new FileStream (destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				await file.WriteAsync (bytes, token);
				file.Flush (flushToDisk: true);
				}
			await output.WriteLineAsync (JsonSerializer.Serialize (new
				{
				schemaVersion = 1,
				state = "DispatchSettingsPrepared",
				settingsSha256 = Hash (bytes),
				approvalRequired = true,
				deliveryAttempted = false
				}));
			return 0;
			}
		catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
			JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OperationCanceledException)
			{
			await error.WriteLineAsync ("Delivery settings were not prepared. Check the private inputs, completed preparation, console download and new output path. Error type: " + exception.GetType ().Name);
			return exception is OperationCanceledException ? 130 : 2;
			}
		}

	internal static async Task<SubmissionDispatchSettings> PrepareAsync (SubmissionDispatchPreparationSettings input,
		string consoleDirectory, string destination, CancellationToken token)
		{
		if (input.SchemaVersion != 1)
			throw new InvalidDataException ("Unsupported setup settings.");
		foreach (string path in new[] { input.PreparedDirectory, input.PreparationSettingsPath, destination })
			if (!Path.IsPathFullyQualified (path))
				throw new InvalidDataException ("Use absolute private paths.");
		var bundle = await SubmissionDeliveryRevalidation.PrepareBundledSettingsAsync (consoleDirectory,
			input.PreparationSettingsPath, input.PreparedDirectory, input.AttemptsDirectory, input.DeliveryReviewSha256,
			TimeSpan.FromSeconds (input.RevalidationTimeoutSeconds), token);
		byte[] receiptBytes = await ReadAsync (Path.Combine (input.PreparedDirectory, "delivery-review-receipt.json"), token);
		if (Hash (receiptBytes) != input.DeliveryReviewSha256 ||
			System.Text.Encoding.ASCII.GetString (await ReadAsync (Path.Combine (input.PreparedDirectory, "COMPLETE"), token)).Trim () != input.DeliveryReviewSha256)
			throw new InvalidDataException ("Select the completed, reviewed delivery preparation.");
		using var receipt = Parse (receiptBytes);
		var root = receipt.RootElement;
		if (root.GetProperty ("schemaVersion").GetInt32 () != 1 || root.GetProperty ("state").GetString () != "DeliveryPlanPrepared" ||
			root.GetProperty ("deliveryAuthorized").ValueKind != JsonValueKind.True || root.GetProperty ("deliveryAttempted").ValueKind != JsonValueKind.False ||
			root.GetProperty ("submissionReady").ValueKind != JsonValueKind.False || root.GetProperty ("expiresUtc").GetDateTimeOffset () <= DateTimeOffset.UtcNow)
			throw new InvalidDataException ("Delivery preparation is incomplete or expired.");
		byte[] planBytes = await ReadAsync (Path.Combine (input.PreparedDirectory, "delivery-plan.json"), token);
		if (Hash (planBytes) != root.GetProperty ("planFileSha256").GetString ())
			throw new InvalidDataException ("Prepared plan changed.");
		var plan = JsonSerializer.Deserialize<SubmissionDeliveryPlan> (planBytes, Options) ?? throw new InvalidDataException ("Missing delivery plan.");
		_ = SubmissionDelivery.PlanDigest (plan);
		if (plan.ReviewSha256 != root.GetProperty ("signedReviewSha256").GetString () ||
			plan.AuthorizationSha256 != root.GetProperty ("authorizationSha256").GetString ())
			throw new InvalidDataException ("Prepared plan does not match its reviewed approval.");
		var settings = new SubmissionDispatchSettings (2, plan, input.JournalDirectory,
			Path.Combine (input.PreparedDirectory, "delivery", plan.PackageFileName),
			Path.Combine (input.PreparedDirectory, "delivery", plan.SignedFormFileName), null,
			input.UploadReceiptDirectory, input.ReviewedUploadFormSha256, input.AcceptedUploadTermsSha256, input.UploadTimeoutSeconds,
			input.MailReceiptDirectory, input.SmtpHost, input.SmtpPort, input.MailTimeoutSeconds, bundle);
		SubmissionDispatchCommand.Validate (settings);
		// Keep output and mutable receipts outside the extracted program and all retained preparation inputs.
		using var preparation = Parse (await ReadAsync (input.PreparationSettingsPath, token));
		if (preparation.RootElement.GetProperty ("output").GetString () is not string preparedOutput ||
			!Path.GetFullPath (preparedOutput).TrimEnd ('/', '\\').Equals (Path.GetFullPath (input.PreparedDirectory).TrimEnd ('/', '\\'), StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException ("Preparation settings refer to a different delivery directory.");
		string[] immutable = [consoleDirectory, input.PreparedDirectory, input.PreparationSettingsPath,
			preparation.RootElement.GetProperty ("signedReviewDirectory").GetString ()!,
			preparation.RootElement.GetProperty ("reviewDirectory").GetString ()!,
			preparation.RootElement.GetProperty ("authorization").GetString ()!];
		string[] mutable = [destination, input.AttemptsDirectory, input.JournalDirectory, input.UploadReceiptDirectory, input.MailReceiptDirectory];
		foreach (string first in mutable)
			foreach (string second in immutable)
				if (!Path.IsPathFullyQualified (second) || Overlap (first, second))
					throw new InvalidDataException ("Keep mutable outputs separate from retained inputs.");
		foreach (string directory in mutable.Skip (1))
			if (Overlap (destination, directory))
				throw new InvalidDataException ("Keep reviewed settings outside mutable receipt directories.");
		if (File.Exists (destination) || Directory.Exists (destination))
			throw new IOException ("Use a new settings file.");
		return settings;
		}

	private static bool Overlap (string first, string second)
		{
		first = Path.TrimEndingDirectorySeparator (Path.GetFullPath (first)) + Path.DirectorySeparatorChar;
		second = Path.TrimEndingDirectorySeparator (Path.GetFullPath (second)) + Path.DirectorySeparatorChar;
		return first.StartsWith (second, StringComparison.OrdinalIgnoreCase) || second.StartsWith (first, StringComparison.OrdinalIgnoreCase);
		}
	private static string Hash (byte[] bytes) => Convert.ToHexStringLower (SHA256.HashData (bytes));
	private static async Task<byte[]> ReadAsync (string path, CancellationToken token)
		{
		if (!Path.IsPathFullyQualified (path) || (File.GetAttributes (path) & FileAttributes.ReparsePoint) != 0)
			throw new InvalidDataException ("Use an absolute, unredirected input file.");
		await using var file = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (file.Length is <= 0 or > 1024 * 1024)
			throw new InvalidDataException ("Input exceeds the supported size.");
		byte[] bytes = new byte[(int)file.Length];
		await file.ReadExactlyAsync (bytes, token);
		return bytes;
		}
	private static JsonDocument Parse (byte[] bytes) => JsonDocument.Parse (bytes, new JsonDocumentOptions { AllowDuplicateProperties = false });
	}