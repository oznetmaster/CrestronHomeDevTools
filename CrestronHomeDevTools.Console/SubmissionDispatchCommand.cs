// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using CrestronHomeDevTools;

internal sealed record SubmissionDispatchSettings (int SchemaVersion, SubmissionDeliveryPlan Plan,
	string JournalDirectory, string PackagePath, string SignedFormPath,
	SubmissionDeliveryRevalidationSettings? Revalidation, string UploadReceiptDirectory,
	string ReviewedUploadFormSha256, string AcceptedUploadTermsSha256, int UploadTimeoutSeconds,
	string MailReceiptDirectory, string SmtpHost, int SmtpPort, int MailTimeoutSeconds,
	SubmissionBundledRevalidationSettings? BundledRevalidation = null);

internal sealed record SubmissionDispatchCredentials (string UploadUserName, string UploadPassword, string SmtpUserName, string SmtpPassword);

/// <summary>Protected, noninteractive delivery entry point; credentials arrive only through bounded standard input.</summary>
internal static class SubmissionDispatchCommand
	{
	private static readonly JsonSerializerOptions Options = new ()
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
			AllowDuplicateProperties = false,
			RespectNullableAnnotations = true,
			RespectRequiredConstructorParameters = true,
			Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
			};

	internal static async Task<int> RunAsync (string[] args, TextReader input, TextWriter output, TextWriter error,
		CancellationToken token = default,
		Func<SubmissionDispatchSettings, SubmissionDispatchCredentials, CancellationToken, Task<SubmissionDeliveryReceipt>>? execute = null)
		=> await RunProtectedAsync (args, input, output, error, Validate, execute ?? ExecuteAsync, token);

	internal static async Task<int> RunProtectedAsync<TSettings> (string[] args, TextReader input, TextWriter output, TextWriter error,
		Action<TSettings> validate, Func<TSettings, SubmissionDispatchCredentials, CancellationToken, Task<SubmissionDeliveryReceipt>> execute,
		CancellationToken token) where TSettings : class
		{
		try
			{
			if (args is not ["--settings", var path, "--settings-sha256", var expected, "--execute-approved"] ||
				!Path.IsPathFullyQualified (path) || !Hash (expected))
				throw new ArgumentException ("Exact protected delivery arguments are required.");
			if ((File.GetAttributes (path) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("Settings cannot be a redirected file.");
			// Retain the reviewed file handle for this invocation; a changed configuration needs a new reviewed hash.
			await using var settingsFile = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (settingsFile.Length is <= 0 or > 1024 * 1024) throw new InvalidDataException ("Settings exceed the supported size.");
			byte[] bytes = new byte[(int)settingsFile.Length];
			await settingsFile.ReadExactlyAsync (bytes, token);
			if (Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant () != expected)
				throw new InvalidDataException ("Settings differ from their trusted pin.");
			var settings = JsonSerializer.Deserialize<TSettings> (bytes, Options) ?? throw new InvalidDataException ("Missing settings.");
			validate (settings);
			using var inputDeadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			inputDeadline.CancelAfter (TimeSpan.FromSeconds (30));
			char[] buffer = new char[32769]; int count = 0;
			try
				{
				while (count < buffer.Length)
					{
					int read = await input.ReadAsync (buffer.AsMemory (count), inputDeadline.Token);
					if (read == 0) break;
					count += read;
					}
				if (count == 0 || count == buffer.Length) throw new InvalidDataException ("Missing or oversized private credentials.");
				var credentials = JsonSerializer.Deserialize<SubmissionDispatchCredentials> (new string (buffer, 0, count), Options)
					?? throw new InvalidDataException ("Missing private credentials.");
				if (string.IsNullOrWhiteSpace (credentials.UploadUserName) || string.IsNullOrEmpty (credentials.UploadPassword) ||
					string.IsNullOrWhiteSpace (credentials.SmtpUserName) || string.IsNullOrEmpty (credentials.SmtpPassword))
					throw new InvalidDataException ("Incomplete private credentials.");
				var receipt = await execute (settings, credentials, token);
				await output.WriteLineAsync (JsonSerializer.Serialize (new { SchemaVersion = 1, State = receipt.State.ToString (), Submitted = receipt.State == SubmissionDeliveryState.Submitted }));
				return receipt.State == SubmissionDeliveryState.Submitted ? 0 : 2;
				}
			finally { Array.Clear (buffer); }
			}
		catch (Exception exception)
			{
			// Provider replies, account names, private links and form contents belong only in protected receipts.
			await error.WriteLineAsync ("Submission delivery did not complete. Inspect the private journal and provider receipts before any retry. Error type: " + exception.GetType ().Name);
			return exception is OperationCanceledException ? 130 : 2;
			}
		}

	private static bool Hash (string value) => value is { Length: 64 } && value.All (c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

	internal static void Validate (SubmissionDispatchSettings settings)
		{
		bool legacy = settings.SchemaVersion == 1 && settings.Revalidation != null && settings.BundledRevalidation == null;
		bool bundled = settings.SchemaVersion == 2 && settings.Revalidation == null && settings.BundledRevalidation != null;
		if (!(legacy || bundled) || settings.Plan == null ||
			!Hash (settings.ReviewedUploadFormSha256) || !Hash (settings.AcceptedUploadTermsSha256) ||
			settings.UploadTimeoutSeconds is < 1 or > 600 || settings.MailTimeoutSeconds is < 1 or > 600 ||
			settings.SmtpPort is not (465 or 587) || Uri.CheckHostName (settings.SmtpHost) != UriHostNameType.Dns)
			throw new InvalidDataException ("Unsupported delivery settings.");
		_ = SubmissionDelivery.PlanDigest (settings.Plan);
		if (!Path.IsPathFullyQualified (settings.PackagePath) || !Path.IsPathFullyQualified (settings.SignedFormPath))
			throw new InvalidDataException ("Use absolute prepared artifact paths.");
		string[] directories = [settings.JournalDirectory, settings.UploadReceiptDirectory, settings.MailReceiptDirectory,
			settings.Revalidation?.AttemptsDirectory ?? settings.BundledRevalidation!.AttemptsDirectory];
		foreach (string directory in directories)
			if (!Path.IsPathFullyQualified (directory) || !Directory.Exists (directory) || (File.GetAttributes (directory) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("Prepare protected local receipt directories before delivery.");
		var comparison = OperatingSystem.IsWindows () ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		for (int i = 0; i < directories.Length; i++)
			for (int j = i + 1; j < directories.Length; j++)
				{
				string first = Path.TrimEndingDirectorySeparator (Path.GetFullPath (directories[i])) + Path.DirectorySeparatorChar;
				string second = Path.TrimEndingDirectorySeparator (Path.GetFullPath (directories[j])) + Path.DirectorySeparatorChar;
				if (first.StartsWith (second, comparison) || second.StartsWith (first, comparison))
					throw new InvalidDataException ("Journal, provider receipts and revalidation attempts must have separate, non-nested directories.");
				}
		}

	private static async Task<SubmissionDeliveryReceipt> ExecuteAsync (SubmissionDispatchSettings settings, SubmissionDispatchCredentials credentials, CancellationToken token)
		{
		using var uploader = new CrestronSubmissionUploader (new NetworkCredential (credentials.UploadUserName, credentials.UploadPassword),
			settings.ReviewedUploadFormSha256, settings.AcceptedUploadTermsSha256, settings.UploadReceiptDirectory, TimeSpan.FromSeconds (settings.UploadTimeoutSeconds));
		var mailer = new SubmissionSmtpMailer (settings.SmtpHost, settings.SmtpPort, settings.Plan.Sender,
			new NetworkCredential (credentials.SmtpUserName, credentials.SmtpPassword), settings.MailReceiptDirectory, TimeSpan.FromSeconds (settings.MailTimeoutSeconds));
		return await DispatchAsync (settings, new CrestronSubmissionTransport (uploader, mailer),
			(step, cancellation) => RevalidateAsync (settings, step, cancellation), token);
		}

	internal static Task<SubmissionDeliveryAuthorization> RevalidateAsync (SubmissionDispatchSettings settings,
		SubmissionDeliveryStep step, CancellationToken token) => settings.SchemaVersion == 2
		? SubmissionDeliveryRevalidation.CheckAsync (settings.BundledRevalidation!, settings.Plan, step, token)
		: SubmissionDeliveryRevalidation.CheckAsync (settings.Revalidation!, settings.Plan, step, token);

	internal static Task<SubmissionDeliveryReceipt> DispatchAsync (SubmissionDispatchSettings settings, ISubmissionDeliveryTransport transport,
		Func<SubmissionDeliveryStep, CancellationToken, Task<SubmissionDeliveryAuthorization>> revalidate, CancellationToken token) =>
		SubmissionDelivery.ExecuteAuthorizedAsync (settings.JournalDirectory, settings.Plan, settings.PackagePath, settings.SignedFormPath,
			transport, revalidate, cancellationToken: token);
	}