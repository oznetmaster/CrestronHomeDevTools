// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using MailKit.Net.Smtp;
using MailKit.Security;

using MimeKit;

namespace CrestronHomeDevTools;

/// <summary>Authenticated SMTP over required TLS. Invoke through the authorized delivery journal.</summary>
public sealed class SubmissionSmtpMailer
	{
	private const int FormLimit = 64 * 1024 * 1024;
	private readonly string _host, _sender, _root;
	private readonly int _port;
	private readonly NetworkCredential _credential;
	private readonly TimeSpan _timeout;
	private readonly Func<ISubmissionSmtpSession> _sessionFactory;

	/// <summary>Port 465 uses implicit TLS; port 587 requires STARTTLS. The receipt directory must already be private.</summary>
	public SubmissionSmtpMailer (string host, int port, string sender, NetworkCredential credential,
		string privateReceiptDirectory, TimeSpan timeout)
		: this (host, port, sender, credential, privateReceiptDirectory, timeout, () => new MailKitSession ())
		{
		}

	internal SubmissionSmtpMailer (string host, int port, string sender, NetworkCredential credential,
		string privateReceiptDirectory, TimeSpan timeout, Func<ISubmissionSmtpSession> sessionFactory)
		{
		if (Uri.CheckHostName (host) != UriHostNameType.Dns || host.Any (char.IsWhiteSpace) || port is not (465 or 587))
			{
			throw new ArgumentException ("Configure a DNS SMTP host and TLS port 465 or 587.");
			}
		if (!MailboxAddress.TryParse (sender, out var address) || address.Address != sender || sender.Any (char.IsControl))
			{
			throw new ArgumentException ("Configure one plain sender address.");
			}
		ArgumentNullException.ThrowIfNull (credential);
		if (string.IsNullOrWhiteSpace (credential.UserName) || string.IsNullOrEmpty (credential.Password) || !string.IsNullOrEmpty (credential.Domain))
			{
			throw new ArgumentException ("Configure explicit SMTP credentials without a Windows domain.");
			}
		_root = Path.GetFullPath (privateReceiptDirectory);
		if (!Directory.Exists (_root) || (File.GetAttributes (_root) & FileAttributes.ReparsePoint) != 0)
			{
			throw new ArgumentException ("Create and protect the private receipt directory first.");
			}
		if (timeout < TimeSpan.FromSeconds (1) || timeout > TimeSpan.FromMinutes (10))
			{
			throw new ArgumentOutOfRangeException (nameof (timeout));
			}
		_host = host;
		_port = port;
		_sender = sender;
		_credential = new NetworkCredential (credential.UserName, credential.Password);
		_timeout = timeout;
		_sessionFactory = sessionFactory;
		}

	/// <summary>Send once and retain the SMTP acceptance response. Acceptance does not establish inbox delivery or certification.</summary>
	public Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload,
		Stream signedForm, string messageId, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (upload);
		string digest = SubmissionDelivery.PlanDigest (plan);
		return SendCoreAsync (digest, plan.Sender, plan.Recipient, plan.SignedFormFileName, plan.SignedFormSha256,
			"Driver Submission Package",
			"Please review the attached signed self-test form and the driver package at the following download link.\r\n\r\n" +
				upload.DownloadUrl + "\r\n\r\nPackage: " + plan.PackageFileName + "\r\nSHA-256: " + plan.PackageSha256 + "\r\n",
			upload, signedForm, messageId, cancellationToken);
		}

	/// <summary>Send the exact reviewed disposition and disclosure attachment without implying a signature or vendor decision.</summary>
	public Task<SubmissionMailReceipt> SendReviewAsync (SubmissionReviewDeliveryPlan plan, SubmissionUploadReceipt upload,
		Stream attachment, string messageId, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (upload);
		string digest = SubmissionDelivery.ReviewPlanDigest (plan);
		var correspondence = SubmissionDelivery.ReviewCorrespondence (plan);
		string body = plan.CorrespondenceOverride == null
			? correspondence.Body + "\r\nConfirmed package download link:\r\n" + upload.DownloadUrl + "\r\n"
			: correspondence.Body.Replace ("{{PACKAGE_DOWNLOAD_URL}}", upload.DownloadUrl, StringComparison.Ordinal);
		return SendCoreAsync (digest, plan.Sender, plan.Recipient, plan.AttachmentFileName, plan.AttachmentSha256,
			correspondence.Subject, body,
			upload, attachment, messageId, cancellationToken);
		}

	private async Task<SubmissionMailReceipt> SendCoreAsync (string digest, string sender, string recipient,
		string attachmentFileName, string attachmentSha256, string subject, string bodyText,
		SubmissionUploadReceipt upload, Stream attachmentStream, string messageId, CancellationToken cancellationToken)
		{
		if (sender != _sender || messageId != "<crestron-" + digest + "@submission.local>")
			{
			throw new InvalidDataException ("The configured sender and message ID must match the authorized plan.");
			}
		if (upload == null || string.IsNullOrWhiteSpace (upload.ProviderReceipt) ||
			!Uri.TryCreate (upload.DownloadUrl, UriKind.Absolute, out var download) ||
			download.Scheme != "https" || download.UserInfo != "" || upload.DownloadUrl.Any (char.IsControl))
			{
			throw new InvalidDataException ("A confirmed HTTPS upload receipt is required.");
			}
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (_timeout);
		using var snapshot = new MemoryStream ();
		var buffer = new byte[81920];
		int count;
		while ((count = await attachmentStream.ReadAsync (buffer, deadline.Token).ConfigureAwait (false)) != 0)
			{
			if (snapshot.Length + count > FormLimit)
				{
				throw new InvalidDataException ("The attachment exceeds 64 MiB.");
				}
			snapshot.Write (buffer, 0, count);
			}
		byte[] form = snapshot.ToArray ();
		if (form.Length == 0 || Hash (form) != attachmentSha256)
			{
			throw new InvalidDataException ("The attachment differs from the authorized plan.");
			}
		using var message = new MimeMessage ();
		message.From.Add (MailboxAddress.Parse (sender));
		message.To.Add (MailboxAddress.Parse (recipient));
		message.MessageId = messageId[1..^1];
		message.Subject = subject;
		var body = new BodyBuilder { TextBody = bodyText };
		body.Attachments.Add (attachmentFileName, form, new ContentType ("application", "pdf"));
		message.Body = body.ToMessageBody ();
		string attemptId = "mail-" + Guid.NewGuid ().ToString ("N");
		string attempt = Path.Combine (_root, attemptId);
		Directory.CreateDirectory (attempt);
		Write (attempt, "intent.json", new { PlanSha256 = digest, MessageId = messageId, Host = _host, Port = _port, StartedUtc = DateTimeOffset.UtcNow });
		bool sendAttempted = false;
		ISubmissionSmtpSession? session = null;
		try
			{
			using (var eml = new FileStream (Path.Combine (attempt, "message.eml"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				await message.WriteToAsync (eml, deadline.Token).ConfigureAwait (false);
				eml.Flush (flushToDisk: true);
				}
			session = _sessionFactory ();
			await session.ConnectAsync (_host, _port, _credential, deadline.Token).ConfigureAwait (false);
			Write (attempt, "send-intent.json", new { PlanSha256 = digest, MessageId = messageId, StartedUtc = DateTimeOffset.UtcNow });
			sendAttempted = true;
			string response = await session.SendAsync (message, deadline.Token).ConfigureAwait (false);
			// A successful SMTP completion can have an empty free-form response.
			Write (attempt, "accepted.json", new { PlanSha256 = digest, MessageId = messageId, Response = response, AcceptedUtc = DateTimeOffset.UtcNow });
			return new (JsonSerializer.Serialize (new { Provider = "SMTP", Attempt = attemptId, MessageId = messageId, PlanSha256 = digest, Accepted = true }));
			}
		catch (Exception error)
			{
			// Keep actionable server rejection details private, without exposing credentials in returned errors.
			var rejection = error is SmtpCommandException smtp ? new
				{
				StatusCode = (int)smtp.StatusCode,
				ErrorCode = smtp.ErrorCode.ToString (),
				Reply = smtp.Message.Replace (_credential.Password, "[redacted]", StringComparison.Ordinal)
				} : null;
			Write (attempt, "failed.json", new { SendAttempted = sendAttempted, Outcome = sendAttempted ? "RequiresReconciliation" : "NotSent", Rejection = rejection, FailedUtc = DateTimeOffset.UtcNow });
			cancellationToken.ThrowIfCancellationRequested ();
			throw new InvalidDataException ("SMTP submission did not complete. Inspect the private attempt; do not automatically resend.");
			}
		finally
			{
			// Closing the connection must not obscure a saved acceptance or the original send failure.
			try
				{
				session?.Dispose ();
				}
			catch
				{
				}
			}
		}

	private static string Hash (byte[] value) => Convert.ToHexString (SHA256.HashData (value)).ToLowerInvariant ();
	private static void Write (string directory, string name, object value)
		{
		using var file = new FileStream (Path.Combine (directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
		JsonSerializer.Serialize (file, value);
		file.Flush (flushToDisk: true);
		}

	internal sealed class MailKitSession : ISubmissionSmtpSession
		{
		private readonly SmtpClient _client = new ();
		public async Task ConnectAsync (string host, int port, NetworkCredential credential, CancellationToken token)
			{
			await _client.ConnectAsync (host, port, port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, token).ConfigureAwait (false);
			if (!_client.IsSecure)
				{
				throw new InvalidDataException ("SMTP requires TLS before authentication.");
				}
			await _client.AuthenticateAsync (credential, token).ConfigureAwait (false);
			}
		public Task<string> SendAsync (MimeMessage message, CancellationToken token) => _client.SendAsync (message, token);
		public void Dispose () => _client.Dispose ();
		}
	}

internal interface ISubmissionSmtpSession : IDisposable
	{
	Task ConnectAsync (string host, int port, NetworkCredential credential, CancellationToken token);
	Task<string> SendAsync (MimeMessage message, CancellationToken token);
	}
