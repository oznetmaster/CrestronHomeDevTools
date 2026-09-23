// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using MimeKit;

namespace CrestronHomeDevTools;

/// <summary>Explicitly authorized notification destination. RunId identifies one immutable monitoring run.</summary>
public sealed record SubmissionEnduranceNotificationSettings (string RunId, string Label, string Host, int Port,
	string Sender, string Recipient, bool NotifyCompletion = true);
public sealed record SubmissionEnduranceNotificationResult (string State, string? MessageId, bool RequiresInspection);

/// <summary>
/// Sends operational alerts only, never a driver submission. Use one protected journal per run/destination.
/// SMTP acceptance is not proof of inbox delivery. Unknown delivery is never automatically repeated.
/// </summary>
public sealed class SubmissionEnduranceNotifier
	{
	private readonly SubmissionEnduranceNotificationSettings _settings;
	private readonly NetworkCredential _credential;
	private readonly string _root, _digest;
	private readonly Func<ISubmissionSmtpSession> _factory;
	private sealed record Journal (int SchemaVersion, string SettingsSha256, string State,
		SubmissionEnduranceHealthState Kind, DateTimeOffset ObservedUtc, string? MessageId, string? CompletionMessageId = null);

	public SubmissionEnduranceNotifier (SubmissionEnduranceNotificationSettings settings, NetworkCredential credential, string privateJournalDirectory)
		: this (settings, credential, privateJournalDirectory, () => new SubmissionSmtpMailer.MailKitSession ()) { }

	internal SubmissionEnduranceNotifier (SubmissionEnduranceNotificationSettings settings, NetworkCredential credential,
		string privateJournalDirectory, Func<ISubmissionSmtpSession> factory)
		{
		ArgumentNullException.ThrowIfNull (settings);
		ArgumentNullException.ThrowIfNull (credential);
		if (settings.RunId == null || settings.RunId.Length != 64 || !settings.RunId.All (char.IsAsciiHexDigit) ||
			string.IsNullOrWhiteSpace (settings.Label) || settings.Label.Length > 100 || settings.Label.Any (char.IsControl))
			throw new ArgumentException ("Provide a stable 64-character hexadecimal run identity and a short notification label.");
		if (Uri.CheckHostName (settings.Host) != UriHostNameType.Dns || settings.Host.Any (char.IsWhiteSpace) || settings.Port is not (465 or 587))
			throw new ArgumentException ("Configure a DNS SMTP host and required TLS port 465 or 587.");
		foreach (string address in new[] { settings.Sender, settings.Recipient })
			if (!MailboxAddress.TryParse (address, out var mailbox) || mailbox.Address != address || address.Any (char.IsControl))
				throw new ArgumentException ("Configure one plain sender and one explicitly approved recipient.");
		if (string.IsNullOrWhiteSpace (credential.UserName) || string.IsNullOrEmpty (credential.Password) || !string.IsNullOrEmpty (credential.Domain))
			throw new ArgumentException ("Configure explicit SMTP credentials without a Windows domain.");
		_root = Path.GetFullPath (privateJournalDirectory);
		if (!Directory.Exists (_root) || (File.GetAttributes (_root) & FileAttributes.ReparsePoint) != 0)
			throw new ArgumentException ("Create and protect the private notification journal first.");
		_settings = settings;
		_credential = new (credential.UserName, credential.Password);
		_factory = factory;
		_digest = Convert.ToHexString (SHA256.HashData (JsonSerializer.SerializeToUtf8Bytes (settings)));
		}

	/// <summary>Call only with a freshly evaluated trusted observation; no worker/processor access occurs here.</summary>
	public async Task<SubmissionEnduranceNotificationResult> NotifyAsync (SubmissionEnduranceHealthReport report,
		CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (report);
		var now = DateTimeOffset.UtcNow;
		if (!string.Equals (report.PlanSha256, _settings.RunId, StringComparison.OrdinalIgnoreCase) ||
			!Enum.IsDefined (report.State) || report.EvaluatedUtc > now.AddSeconds (2) || now - report.EvaluatedUtc > TimeSpan.FromMinutes (5) ||
			report.Reasons == null || report.Reasons.Count > 32 || report.Reasons.Any (r => string.IsNullOrEmpty (r) || r.Length > 80 || r.Any (c => !char.IsAsciiLetterOrDigit (c) && c != '-')) ||
			(report.State == SubmissionEnduranceHealthState.AttentionRequired) != (report.Reasons.Count > 0))
			throw new ArgumentException ("Supply a fresh, consistent health report containing reason codes only.");
		cancellationToken.ThrowIfCancellationRequested ();
		using var guard = Lock ();
		var previous = Read ();
		if (previous?.State is "Sending" or "Uncertain" or "NotSent") return new (previous.State, previous.MessageId, true);
		if (previous != null && report.EvaluatedUtc < previous.ObservedUtc)
			throw new InvalidDataException ("An older health report cannot replace the last observation.");
		bool quiet = report.State == SubmissionEnduranceHealthState.Collecting ||
			(report.State == SubmissionEnduranceHealthState.Completed && !_settings.NotifyCompletion);
		if (quiet)
			{
			Save (new (1, _digest, "Quiet", report.State, report.EvaluatedUtc, null, previous?.CompletionMessageId), previous);
			return new ("Quiet", null, false);
			}
		if (report.State == SubmissionEnduranceHealthState.Completed && previous?.CompletionMessageId != null)
			return new ("AlreadyAccepted", previous.CompletionMessageId, false);
		if (previous?.State == "Accepted" && previous.Kind == report.State)
			{
			Save (previous with { ObservedUtc = report.EvaluatedUtc }, previous);
			return new ("AlreadyAccepted", previous.MessageId, false);
			}
		string messageId = "endurance-" + Guid.NewGuid ().ToString ("N") + "@monitor.local";
		var sending = new Journal (1, _digest, "Sending", report.State, report.EvaluatedUtc, messageId, previous?.CompletionMessageId);
		using var message = new MimeMessage ();
		message.From.Add (MailboxAddress.Parse (_settings.Sender));
		message.To.Add (MailboxAddress.Parse (_settings.Recipient));
		message.MessageId = messageId;
		message.Subject = report.RequiresAttention ? "Endurance monitoring needs attention" : "Endurance collection completed";
		message.Body = new TextPart ("plain") { Text = _settings.Label + "\r\nState: " + report.State +
			"\r\nObserved UTC: " + report.EvaluatedUtc.ToUniversalTime ().ToString ("O") +
			"\r\nReasons: " + string.Join (", ", report.Reasons) +
			"\r\n\r\nInspect the monitoring journal. This operational notification is not submission acceptance.\r\n" };
		Save (sending);
		bool sendAttempted = false;
		ISubmissionSmtpSession? session = null;
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (TimeSpan.FromSeconds (30));
		try
			{
			session = _factory ();
			await session.ConnectAsync (_settings.Host, _settings.Port, _credential, deadline.Token).ConfigureAwait (false);
			sendAttempted = true;
			await session.SendAsync (message, deadline.Token).ConfigureAwait (false);
			Save (sending with { State = "Accepted", CompletionMessageId = report.State == SubmissionEnduranceHealthState.Completed ? messageId : sending.CompletionMessageId });
			return new ("Accepted", messageId, false);
			}
		catch
			{
			string state = sendAttempted ? "Uncertain" : "NotSent";
			Save (sending with { State = state });
			cancellationToken.ThrowIfCancellationRequested ();
			return new (state, messageId, true);
			}
		finally { try { session?.Dispose (); } catch { } }
		}

	/// <summary>Explicit operator reconciliation after independently determining delivery; never call automatically.</summary>
	public void Reconcile (string expectedMessageId, bool deliveryAccepted, string reviewReference)
		{
		if (string.IsNullOrWhiteSpace (reviewReference) || reviewReference.Length > 200 || reviewReference.Any (char.IsControl))
			throw new ArgumentException ("Provide a short private reference to the independent delivery review.");
		using var guard = Lock ();
		var journal = Read ();
		if (journal == null || journal.MessageId != expectedMessageId || journal.State is not ("Sending" or "Uncertain" or "NotSent"))
			throw new InvalidOperationException ("The notification journal does not match the reviewed unresolved attempt.");
		WriteNew (Path.Combine (_root, "review-" + Guid.NewGuid ().ToString ("N") + ".json"),
			new { MessageId = expectedMessageId, DeliveryAccepted = deliveryAccepted, ReviewReference = reviewReference, ReviewedUtc = DateTimeOffset.UtcNow });
		Save (journal with { State = deliveryAccepted ? "Accepted" : "Quiet",
			CompletionMessageId = deliveryAccepted && journal.Kind == SubmissionEnduranceHealthState.Completed ? expectedMessageId : journal.CompletionMessageId });
		}

	private string CheckedPath (string name)
		{
		string path = Path.Combine (_root, name);
		if (File.Exists (path) && (File.GetAttributes (path) & FileAttributes.ReparsePoint) != 0)
			throw new InvalidDataException ("Notification journal links are not permitted.");
		return path;
		}
	private FileStream Lock () => new (CheckedPath ("notification.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
	private Journal? Read ()
		{
		string path = CheckedPath ("notification.json");
		if (!File.Exists (path))
			{
			if (Directory.EnumerateFiles (_root, "event-*.json").Any ()) throw new InvalidDataException ("The notification checkpoint is missing; preserve history and inspect before sending.");
			return null;
			}
		if (new FileInfo (path).Length > 65536) throw new InvalidDataException ("Notification journal exceeds its size limit.");
		var value = JsonSerializer.Deserialize<Journal> (File.ReadAllBytes (path));
		if (value == null || value.SchemaVersion != 1 || value.SettingsSha256 != _digest || !Enum.IsDefined (value.Kind) ||
			value.State is not ("Quiet" or "Sending" or "Accepted" or "Uncertain" or "NotSent") ||
			(value.State != "Quiet" && string.IsNullOrWhiteSpace (value.MessageId)))
			throw new InvalidDataException ("Notification journal identity or state is invalid.");
		return value;
		}
	private void Save (Journal value, Journal? previous = null)
		{
		string path = CheckedPath ("notification.json");
		// A fresh observation time is not a state transition. Preserve actual delivery
		// transitions, while healthy/repeated observations update only the checkpoint.
		if (previous == null || (previous with { ObservedUtc = value.ObservedUtc }) != value)
			WriteNew (Path.Combine (_root, "event-" + Guid.NewGuid ().ToString ("N") + ".json"), value);
		string replacement = Path.Combine (_root, Guid.NewGuid ().ToString ("N") + ".tmp");
		try { WriteNew (replacement, value); SubmissionJournalFile.Replace (replacement, path); }
		finally { if (File.Exists (replacement)) File.Delete (replacement); }
		}
	private static void WriteNew (string path, object value)
		{
		using var file = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		JsonSerializer.Serialize (file, value);
		file.Flush (true);
		}
	}
