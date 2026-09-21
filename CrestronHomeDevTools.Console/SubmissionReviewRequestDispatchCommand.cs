// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;

using CrestronHomeDevTools;

internal sealed record SubmissionReviewRequestDispatchSettings (int SchemaVersion, SubmissionReviewDeliveryPlan Plan,
	string RequestDirectory, string ApprovalPath, string ApprovalSha256, string JournalDirectory, string ScratchDirectory,
	string UploadReceiptDirectory, string ReviewedUploadFormSha256, string AcceptedUploadTermsSha256, int UploadTimeoutSeconds,
	string MailReceiptDirectory, string SmtpHost, int SmtpPort, int MailTimeoutSeconds,
	SubmissionReviewRequestTooling? Tooling = null);

internal sealed record SubmissionReviewRequestTooling (string ConsoleDirectory, IReadOnlyList<SubmissionEvidenceFile> ConsoleFiles);

/// <summary>Protected dispatch of an independently approved review using stdin or named encrypted credentials.</summary>
internal static class SubmissionReviewRequestDispatchCommand
	{
	internal static Task<int> RunAsync (string[] args, TextReader input, TextWriter output, TextWriter error,
		CancellationToken token = default,
		Func<SubmissionReviewRequestDispatchSettings, SubmissionDispatchCredentials, CancellationToken, Task<SubmissionDeliveryReceipt>>? execute = null)
		=> SubmissionDispatchCommand.RunProtectedAsync (args, input, output, error, Validate, execute ?? ExecuteAsync, token,
			(settings, path) => SubmissionDispatchCommand.ResolveCredentials (path, settings.SmtpHost, settings.SmtpPort, settings.Plan.Sender));

	internal static void Validate (SubmissionReviewRequestDispatchSettings settings)
		{
		static bool Hash (string value) => value is { Length: 64 } && value.All (c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
		if (settings.SchemaVersion != 1 || settings.Plan == null || !Hash (settings.ApprovalSha256) ||
			settings.ApprovalSha256 != settings.Plan.AuthorizationSha256 || !Path.IsPathFullyQualified (settings.ApprovalPath) ||
			!Hash (settings.ReviewedUploadFormSha256) || !Hash (settings.AcceptedUploadTermsSha256) ||
			settings.UploadTimeoutSeconds is < 1 or > 600 || settings.MailTimeoutSeconds is < 1 or > 600 ||
			settings.SmtpPort is not (465 or 587) || Uri.CheckHostName (settings.SmtpHost) != UriHostNameType.Dns)
			throw new InvalidDataException ("Unsupported review request delivery settings.");
		_ = SubmissionDelivery.ReviewPlanDigest (settings.Plan);
		if (settings.Plan.ReviewMode != SubmissionReviewMode.DeclaredGaps ||
			settings.Plan.AttachmentKind is not (SubmissionReviewAttachmentKind.SignedSelfTest or SubmissionReviewAttachmentKind.UnsignedSelfTest or SubmissionReviewAttachmentKind.DisclosureOnly))
			throw new InvalidDataException ("This command requires a prepared review with declared gaps.");
		if (settings.Plan.AttachmentKind == SubmissionReviewAttachmentKind.SignedSelfTest && !string.IsNullOrEmpty (settings.Plan.DocumentOmissions))
			throw new InvalidDataException ("A prepared signed review cannot also claim document or signature omissions.");
		string[] directories = [settings.RequestDirectory, settings.JournalDirectory, settings.ScratchDirectory,
			settings.UploadReceiptDirectory, settings.MailReceiptDirectory];
		foreach (var directory in directories)
			{
			if (!Path.IsPathFullyQualified (directory) || !Directory.Exists (directory))
				throw new InvalidDataException ("Provide existing private absolute directories.");
			for (string? path = directory; path != null; path = Path.GetDirectoryName (path))
				if ((File.GetAttributes (path) & FileAttributes.ReparsePoint) != 0)
					throw new InvalidDataException ("Private directories cannot traverse redirected links.");
			}
		var comparison = OperatingSystem.IsWindows () ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		for (int i = 0; i < directories.Length; i++)
			for (int j = i + 1; j < directories.Length; j++)
				{
				string first = Path.TrimEndingDirectorySeparator (Path.GetFullPath (directories[i])) + Path.DirectorySeparatorChar;
				string second = Path.TrimEndingDirectorySeparator (Path.GetFullPath (directories[j])) + Path.DirectorySeparatorChar;
				if (first.StartsWith (second, comparison) || second.StartsWith (first, comparison))
					throw new InvalidDataException ("Prepared request, journal, scratch and provider receipt directories must be separate and non-nested.");
				}
		}

	private static async Task<SubmissionDeliveryReceipt> ExecuteAsync (SubmissionReviewRequestDispatchSettings settings,
		SubmissionDispatchCredentials credentials, CancellationToken token)
		{
		using var uploader = new CrestronSubmissionUploader (new NetworkCredential (credentials.UploadUserName, credentials.UploadPassword),
			settings.ReviewedUploadFormSha256, settings.AcceptedUploadTermsSha256, settings.UploadReceiptDirectory, TimeSpan.FromSeconds (settings.UploadTimeoutSeconds));
		var mailer = new SubmissionSmtpMailer (settings.SmtpHost, settings.SmtpPort, settings.Plan.Sender,
			new NetworkCredential (credentials.SmtpUserName, credentials.SmtpPassword), settings.MailReceiptDirectory, TimeSpan.FromSeconds (settings.MailTimeoutSeconds));
		return await DispatchAsync (settings, new CrestronSubmissionTransport (uploader, mailer), token);
		}
	internal static async Task<SubmissionDeliveryReceipt> DispatchAsync (SubmissionReviewRequestDispatchSettings settings,
		ISubmissionReviewDeliveryTransport transport, CancellationToken token) =>
		(await SubmissionReviewRequestDelivery.ExecuteAsync (settings.RequestDirectory, settings.Plan, settings.ApprovalPath,
			settings.ApprovalSha256, settings.JournalDirectory, settings.ScratchDirectory, transport, cancellationToken: token)).Delivery;
	}