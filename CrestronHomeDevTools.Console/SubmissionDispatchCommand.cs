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

internal sealed record SubmissionDispatchCredentials (string UploadUserName, string UploadPassword, string SmtpUserName, string SmtpPassword)
	{
	public override string ToString () => "Submission credentials (values hidden)";
	}

internal sealed record SubmissionRehearsalMailSettings (string ReceiptDirectory, string Host, int Port, int TimeoutSeconds);

internal sealed record SubmissionRehearsalDispatchSettings (int SchemaVersion, SubmissionDeliveryEnvironment Environment,
	SubmissionDeliveryPlan Plan, string JournalDirectory, string PackagePath, string SignedFormPath,
	string DestinationDirectory, SubmissionDeliveryRevalidationSettings? Revalidation = null,
	SubmissionBundledRevalidationSettings? BundledRevalidation = null, SubmissionRehearsalMailSettings? Mail = null);

/// <summary>Shared pinned delivery entry point with separate production and local rehearsal providers.</summary>
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

	internal static Task<int> RunAsync (string[] args, TextReader input, TextWriter output, TextWriter error,
		CancellationToken token = default,
		Func<SubmissionDispatchSettings, SubmissionDispatchCredentials, CancellationToken, Task<SubmissionDeliveryReceipt>>? execute = null,
		Func<SubmissionRehearsalDispatchSettings, CancellationToken, Task<SubmissionDeliveryReceipt>>? executeRehearsal = null)
		{
		if (args.FirstOrDefault () == "--rehearsal")
			{
			string[] rehearsalArgs = args[1..];
			string? bindingsPath = null;
			if (rehearsalArgs is ["--settings", _, "--settings-sha256", _, "--execute-approved", "--credentials", var bindings])
				{
				bindingsPath = bindings;
				rehearsalArgs = rehearsalArgs[..5];
				}
			return RunConfiguredAsync<SubmissionRehearsalDispatchSettings> (rehearsalArgs, output, error, ValidateRehearsal, async (settings, cancellation) =>
				{
				if (settings.Mail == null && bindingsPath != null) throw new InvalidDataException ("Local rehearsal does not use credentials.");
				if (executeRehearsal != null) return await executeRehearsal (settings, cancellation);
				if (settings.Mail == null)
					{
					if (bindingsPath != null) throw new InvalidDataException ("Local rehearsal does not use credentials.");
					return await ExecuteRehearsalAsync (settings, cancellation);
					}
				var credentials = await ReadCredentialsAsync (input, cancellation, bindingsPath == null ? null :
					() => ResolveSmtpCredentials (bindingsPath, settings.Mail.Host, settings.Mail.Port, settings.Plan.Sender), requireUpload: false);
				var mailer = new SubmissionSmtpMailer (settings.Mail.Host, settings.Mail.Port, settings.Plan.Sender,
					new NetworkCredential (credentials.SmtpUserName, credentials.SmtpPassword), settings.Mail.ReceiptDirectory,
					TimeSpan.FromSeconds (settings.Mail.TimeoutSeconds));
				return await ExecuteRehearsalAsync (settings, cancellation, mailer: mailer);
				}, SubmissionDeliveryEnvironment.Rehearsal, token);
			}
		return RunProtectedAsync (args, input, output, error, Validate, execute ?? ExecuteAsync, token,
			(settings, path) => ResolveCredentials (path, settings.SmtpHost, settings.SmtpPort, settings.Plan.Sender));
		}

	internal static Task<int> RunProtectedAsync<TSettings> (string[] args, TextReader input, TextWriter output, TextWriter error,
		Action<TSettings> validate, Func<TSettings, SubmissionDispatchCredentials, CancellationToken, Task<SubmissionDeliveryReceipt>> execute,
		CancellationToken token, Func<TSettings, string, SubmissionDispatchCredentials> resolve) where TSettings : class
		{
		string? bindingsPath = null;
		if (args is ["--settings", _, "--settings-sha256", _, "--execute-approved", "--credentials", var bindings])
			{
			bindingsPath = bindings;
			args = args[..5];
			}
		return RunConfiguredAsync (args, output, error, validate, async (settings, cancellation) =>
			{
			var credentials = await ReadCredentialsAsync (input, cancellation,
				bindingsPath == null ? null : () => resolve (settings, bindingsPath));
			return await execute (settings, credentials, cancellation);
			}, SubmissionDeliveryEnvironment.Production, token);
		}

	private static async Task<SubmissionDispatchCredentials> ReadCredentialsAsync (TextReader input, CancellationToken token,
		Func<SubmissionDispatchCredentials>? resolve, bool requireUpload = true)
		{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (TimeSpan.FromSeconds (30));
		char[] buffer = new char[32769];
		try
			{
			SubmissionDispatchCredentials credentials;
			if (resolve != null) credentials = resolve ();
			else
				{
				int count = 0;
				while (count < buffer.Length)
					{
					int read = await input.ReadAsync (buffer.AsMemory (count), deadline.Token);
					if (read == 0) break;
					count += read;
					}
				if (count == 0 || count == buffer.Length)
					throw new InvalidDataException ("Missing or oversized private credentials.");
				credentials = ProtectedJsonInput.Deserialize<SubmissionDispatchCredentials> (buffer.AsSpan (0, count), Options)
					?? throw new InvalidDataException ("Missing private credentials.");
				}
			if ((requireUpload && (string.IsNullOrWhiteSpace (credentials.UploadUserName) || string.IsNullOrEmpty (credentials.UploadPassword))) ||
				string.IsNullOrWhiteSpace (credentials.SmtpUserName) || string.IsNullOrEmpty (credentials.SmtpPassword))
				throw new InvalidDataException ("Incomplete private credentials.");
			return credentials;
			}
		finally { Array.Clear (buffer); }
		}

	private static async Task<int> RunConfiguredAsync<TSettings> (string[] args, TextWriter output, TextWriter error,
		Action<TSettings> validate, Func<TSettings, CancellationToken, Task<SubmissionDeliveryReceipt>> execute,
		SubmissionDeliveryEnvironment environment, CancellationToken token) where TSettings : class
		{
		try
			{
			if (args is not ["--settings", var path, "--settings-sha256", var expected, "--execute-approved"] ||
				!Path.IsPathFullyQualified (path) || !Hash (expected))
				throw new ArgumentException ("Exact protected delivery arguments are required.");
			if ((File.GetAttributes (path) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("Settings cannot be a redirected file.");
			// Hold the reviewed settings open through execution; modifications require a new pin.
			await using var settingsFile = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
			if (settingsFile.Length is <= 0 or > 1024 * 1024)
				throw new InvalidDataException ("Settings exceed the supported size.");
			byte[] bytes = new byte[(int)settingsFile.Length];
			await settingsFile.ReadExactlyAsync (bytes, token);
			if (Convert.ToHexStringLower (SHA256.HashData (bytes)) != expected)
				throw new InvalidDataException ("Settings differ from their trusted pin.");
			var settings = JsonSerializer.Deserialize<TSettings> (bytes, Options) ?? throw new InvalidDataException ("Missing settings.");
			validate (settings);
			var receipt = await execute (settings, token);
			if (receipt.Environment != environment)
				throw new InvalidDataException ("Delivery receipt belongs to another environment.");
			bool complete = receipt.State == SubmissionDeliveryState.Submitted;
			bool rehearsal = environment == SubmissionDeliveryEnvironment.Rehearsal;
			await output.WriteLineAsync (JsonSerializer.Serialize (new
				{
				SchemaVersion = 1,
				Environment = environment.ToString (),
				State = complete && rehearsal ? "RehearsalCompleted" : receipt.State.ToString (),
				Submitted = complete && !rehearsal,
				RehearsalCompleted = complete && rehearsal
				}));
			return complete ? 0 : 2;
			}
		catch (Exception exception)
			{
			await error.WriteLineAsync ("Submission delivery did not complete. Inspect the private journal and provider receipts before any retry. Error type: " + exception.GetType ().Name);
			return exception is OperationCanceledException ? 130 : 2;
			}
		}

	internal static SubmissionDispatchCredentials ResolveCredentials (string path, string smtpHost, int smtpPort, string sender)
		{
		if (!OperatingSystem.IsWindows ())
			throw new PlatformNotSupportedException ("Saved credentials require Windows.");
		var bindings = DevToolsCredentialBindings.Read (path);
		var smtp = bindings.Resolve (DevToolsCredentialPurpose.Smtp, smtpHost, smtpPort, sender);
		var uploader = bindings.Resolve (DevToolsCredentialPurpose.Uploader, "uploader.crestron.com");
		if (uploader.Port is not (null or 443))
			throw new InvalidDataException ("Saved uploader port does not match HTTPS.");
		return new (uploader.UserName, uploader.Password, smtp.UserName, smtp.Password);
		}

	internal static SubmissionDispatchCredentials ResolveSmtpCredentials (string path, string host, int port, string sender)
		{
		if (!OperatingSystem.IsWindows ()) throw new PlatformNotSupportedException ("Saved credentials require Windows.");
		var smtp = DevToolsCredentialBindings.Read (path).Resolve (DevToolsCredentialPurpose.Smtp, host, port, sender);
		return new ("", "", smtp.UserName, smtp.Password);
		}

	private static bool Hash (string value) => value is { Length: 64 } && value.All (c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

	internal static void Validate (SubmissionDispatchSettings settings)
		{
		bool legacy = settings.SchemaVersion == 1 && settings.Revalidation != null && settings.BundledRevalidation == null;
		bool bundled = settings.SchemaVersion == 2 && settings.Revalidation == null && settings.BundledRevalidation != null;
		if (!(legacy || bundled) || settings.Plan == null || settings.Plan.Environment != SubmissionDeliveryEnvironment.Production ||
			!Hash (settings.ReviewedUploadFormSha256) || !Hash (settings.AcceptedUploadTermsSha256) ||
			settings.UploadTimeoutSeconds is < 1 or > 600 || settings.MailTimeoutSeconds is < 1 or > 600 ||
			settings.SmtpPort is not (465 or 587) || Uri.CheckHostName (settings.SmtpHost) != UriHostNameType.Dns)
			throw new InvalidDataException ("Unsupported delivery settings.");
		ValidateArtifacts (settings.Plan, settings.PackagePath, settings.SignedFormPath,
			[settings.JournalDirectory, settings.UploadReceiptDirectory, settings.MailReceiptDirectory,
			settings.Revalidation?.AttemptsDirectory ?? settings.BundledRevalidation!.AttemptsDirectory]);
		}

	internal static void ValidateRehearsal (SubmissionRehearsalDispatchSettings settings)
		{
		if (settings.SchemaVersion != 1 || settings.Environment != SubmissionDeliveryEnvironment.Rehearsal ||
			settings.Plan.Environment != SubmissionDeliveryEnvironment.Rehearsal ||
			settings.Plan.SendRehearsalEmail != (settings.Mail != null) ||
			(settings.Revalidation == null) == (settings.BundledRevalidation == null))
			throw new InvalidDataException ("Rehearsal requires its explicit environment and exactly one revalidation configuration.");
		if (settings.Mail is { } mail && (mail.Port is not (465 or 587) || mail.TimeoutSeconds is < 1 or > 600 ||
			Uri.CheckHostName (mail.Host) != UriHostNameType.Dns)) throw new InvalidDataException ("Configure the rehearsal SMTP destination.");
		ValidateArtifacts (settings.Plan, settings.PackagePath, settings.SignedFormPath,
			[settings.JournalDirectory, settings.DestinationDirectory,
			settings.Revalidation?.AttemptsDirectory ?? settings.BundledRevalidation!.AttemptsDirectory,
			.. settings.Mail == null ? Array.Empty<string> () : new[] { settings.Mail.ReceiptDirectory }]);
		}

	private static void ValidateArtifacts (SubmissionDeliveryPlan plan, string packagePath, string signedFormPath, string[] directories)
		{
		_ = SubmissionDelivery.PlanDigest (plan);
		if (!Path.IsPathFullyQualified (packagePath) || !Path.IsPathFullyQualified (signedFormPath))
			throw new InvalidDataException ("Use absolute prepared artifact paths.");
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

	internal static Task<SubmissionDeliveryReceipt> ExecuteRehearsalAsync (SubmissionRehearsalDispatchSettings settings,
		CancellationToken token, Func<SubmissionDeliveryStep, CancellationToken, Task<SubmissionDeliveryAuthorization>>? revalidate = null,
		SubmissionSmtpMailer? mailer = null)
		{
		ValidateRehearsal (settings);
		if ((settings.Mail == null) != (mailer == null)) throw new InvalidDataException ("Rehearsal provider must match reviewed mail settings.");
		ISubmissionDeliveryTransport transport = mailer == null
			? new SubmissionRehearsalTransport (settings.DestinationDirectory, settings.Plan)
			: new SubmissionRehearsalMailTransport (settings.DestinationDirectory, settings.Plan, mailer);
		return SubmissionDelivery.ExecuteAuthorizedAsync (settings.JournalDirectory, settings.Plan, settings.PackagePath, settings.SignedFormPath,
			transport,
			revalidate ?? ((step, cancellation) => settings.BundledRevalidation != null
				? SubmissionDeliveryRevalidation.CheckAsync (settings.BundledRevalidation, settings.Plan, step, cancellation)
				: SubmissionDeliveryRevalidation.CheckAsync (settings.Revalidation!, settings.Plan, step, cancellation)), cancellationToken: token);
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
