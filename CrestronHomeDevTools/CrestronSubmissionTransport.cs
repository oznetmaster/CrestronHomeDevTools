// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeDevTools;

/// <summary>Combines the Crestron upload provider and SMTP sender for the authorized delivery journal.</summary>
public sealed class CrestronSubmissionTransport (CrestronSubmissionUploader uploader, SubmissionSmtpMailer mailer) : ISubmissionDeliveryTransport
	{
	public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken cancellationToken)
		=> uploader.UploadAsync (package, filename, cancellationToken);

	public Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload,
		Stream signedForm, string messageId, CancellationToken cancellationToken)
		=> mailer.SendAsync (plan, upload, signedForm, messageId, cancellationToken);
	}