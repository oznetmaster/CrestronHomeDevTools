// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeDevTools;

/// <summary>Retains the rehearsal upload locally and delivers exact artifacts to the approved test mailbox via normal SMTP.</summary>
public sealed class SubmissionRehearsalMailTransport : ISubmissionDeliveryTransport
	{
	private readonly SubmissionRehearsalTransport _archive;
	private readonly SubmissionSmtpMailer _mailer;
	public SubmissionDeliveryEnvironment Environment => SubmissionDeliveryEnvironment.Rehearsal;
	public SubmissionRehearsalMailTransport (string privateDirectory, SubmissionDeliveryPlan plan, SubmissionSmtpMailer mailer)
		{
		ArgumentNullException.ThrowIfNull (mailer);
		_archive = new SubmissionRehearsalTransport (privateDirectory, plan, true);
		_mailer = mailer;
		}
	public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken cancellationToken) =>
		_archive.UploadAsync (package, filename, cancellationToken);
	public async Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload,
		Stream signedForm, string messageId, CancellationToken cancellationToken)
		{
		using var package = _archive.OpenPackageForMail (plan, upload, messageId);
		return await _mailer.SendRehearsalAsync (plan, upload, package, signedForm, messageId, cancellationToken).ConfigureAwait (false);
		}
	}
