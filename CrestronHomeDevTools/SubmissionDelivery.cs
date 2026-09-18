// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

public enum SubmissionDeliveryState { Prepared, UploadPending, Uploaded, SendPending, Submitted, OutcomeUnknown }
public enum SubmissionDeliveryStep { Upload, Send }
public sealed record SubmissionDeliveryPlan (string CandidateSha256, string ReviewSha256, string AuthorizationSha256,
	string PackageSha256, string SignedFormSha256, string PackageFileName, string SignedFormFileName, string Sender, string Recipient);
public sealed record SubmissionUploadReceipt (string DownloadUrl, string ProviderReceipt);
public sealed record SubmissionMailReceipt (string ProviderReceipt);
/// <summary>Returned only after the trusted workflow revalidates the exact plan and its current approval.</summary>
public sealed record SubmissionDeliveryAuthorization (string PlanSha256, DateTimeOffset ExpiresUtc);
public sealed record SubmissionDeliveryReconciliation (SubmissionDeliveryStep Step, bool Performed, string Evidence,
	DateTimeOffset RecordedUtc);
public sealed record SubmissionDeliveryReceipt (int SchemaVersion, string PlanSha256, SubmissionDeliveryState State,
	string MessageId, DateTimeOffset UpdatedUtc, SubmissionDeliveryStep? PendingStep = null,
	SubmissionUploadReceipt? Upload = null, SubmissionMailReceipt? Mail = null,
	IReadOnlyList<SubmissionDeliveryReconciliation>? Reconciliations = null);

/// <summary>Implementations must send once, disable automatic POST retries and return confirmed provider receipts.</summary>
public interface ISubmissionDeliveryTransport
	{
	Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken cancellationToken);
	Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload,
		Stream signedForm, string messageId, CancellationToken cancellationToken);
	}

/// <summary>Private delivery journal. The caller must verify signed-form authorization and evidence before invoking it.</summary>
public static class SubmissionDelivery
	{
	private static readonly JsonSerializerOptions JsonOptions = new ()
		{ PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter () }, WriteIndented = true };

	public static SubmissionDeliveryReceipt? Read (string privateJournalDirectory, SubmissionDeliveryPlan plan)
		{
		using var journal = new Journal (privateJournalDirectory, PlanDigest (plan), DeliveryKey (plan));
		return journal.Read ();
		}

	/// <summary>Uses one deterministic journal per plan. Unknown outcomes stop until independently reconciled.</summary>
	public static Task<SubmissionDeliveryReceipt> ExecuteAsync (string privateJournalDirectory,
		SubmissionDeliveryPlan plan, string packagePath, string signedFormPath, ISubmissionDeliveryTransport transport,
		CancellationToken cancellationToken = default) =>
		ExecuteCoreAsync (privateJournalDirectory, plan, packagePath, signedFormPath, transport, null, cancellationToken);

	/// <summary>
	/// Revalidate evidence and approval before each external step. The callback must be trusted and independently
	/// verify the pinned authorization; a matching digest alone does not authenticate an approver.
	/// Refusal leaves the last known state intact. An already submitted receipt requires no new authorization or delivery.
	/// </summary>
	public static Task<SubmissionDeliveryReceipt> ExecuteAuthorizedAsync (string privateJournalDirectory,
		SubmissionDeliveryPlan plan, string packagePath, string signedFormPath, ISubmissionDeliveryTransport transport,
		Func<SubmissionDeliveryStep, CancellationToken, Task<SubmissionDeliveryAuthorization>> revalidate,
		TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (revalidate);
		string digest = PlanDigest (plan);
		var clock = timeProvider ?? TimeProvider.System;
		async Task Check (SubmissionDeliveryStep step, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			var approval = await revalidate (step, token).ConfigureAwait (false);
			token.ThrowIfCancellationRequested ();
			if (approval == null || approval.PlanSha256 != digest || approval.ExpiresUtc <= clock.GetUtcNow ())
				throw new InvalidOperationException ("Delivery requires current authorization for the exact plan before each external step.");
			}
		return ExecuteCoreAsync (privateJournalDirectory, plan, packagePath, signedFormPath, transport, Check, cancellationToken);
		}

	private static async Task<SubmissionDeliveryReceipt> ExecuteCoreAsync (string privateJournalDirectory,
		SubmissionDeliveryPlan plan, string packagePath, string signedFormPath, ISubmissionDeliveryTransport transport,
		Func<SubmissionDeliveryStep, CancellationToken, Task>? authorize, CancellationToken cancellationToken)
		{
		ArgumentNullException.ThrowIfNull (transport);
		var digest = PlanDigest (plan);
		using var journal = new Journal (privateJournalDirectory, digest, DeliveryKey (plan));
		var receipt = journal.Read () ?? new (1, digest, SubmissionDeliveryState.Prepared,
			"<crestron-" + digest + "@submission.local>", DateTimeOffset.UtcNow);
		if (receipt.State == SubmissionDeliveryState.Submitted) return receipt;
		if (receipt.State is SubmissionDeliveryState.UploadPending or SubmissionDeliveryState.SendPending or SubmissionDeliveryState.OutcomeUnknown)
			throw new InvalidOperationException ("Delivery outcome is uncertain. Reconcile the provider result before continuing; no request was replayed.");
		if (receipt.State is not (SubmissionDeliveryState.Prepared or SubmissionDeliveryState.Uploaded))
			throw new InvalidDataException ("Unsupported delivery state.");
		using var package = OpenVerified (packagePath, plan.PackageFileName, plan.PackageSha256);
		using var form = OpenVerified (signedFormPath, plan.SignedFormFileName, plan.SignedFormSha256);
		journal.Write (receipt);
		if (receipt.State == SubmissionDeliveryState.Prepared)
			{
			if (authorize != null) await authorize (SubmissionDeliveryStep.Upload, cancellationToken).ConfigureAwait (false);
			cancellationToken.ThrowIfCancellationRequested ();
			receipt = receipt with { State = SubmissionDeliveryState.UploadPending, PendingStep = SubmissionDeliveryStep.Upload, UpdatedUtc = DateTimeOffset.UtcNow };
			journal.Write (receipt); // Flush intent before any external side effect.
			try
				{
				var upload = await transport.UploadAsync (package, plan.PackageFileName, cancellationToken).ConfigureAwait (false);
				RequireUpload (upload);
				receipt = receipt with { State = SubmissionDeliveryState.Uploaded, Upload = upload, PendingStep = null, UpdatedUtc = DateTimeOffset.UtcNow };
				journal.Write (receipt);
				}
			catch
				{
				journal.TryMarkUnknown (SubmissionDeliveryStep.Upload);
				throw;
				}
			}
		RequireUpload (receipt.Upload);
		if (authorize != null) await authorize (SubmissionDeliveryStep.Send, cancellationToken).ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
		receipt = receipt with { State = SubmissionDeliveryState.SendPending, PendingStep = SubmissionDeliveryStep.Send, UpdatedUtc = DateTimeOffset.UtcNow };
		journal.Write (receipt);
		try
			{
			var mail = await transport.SendAsync (plan, receipt.Upload!, form, receipt.MessageId, cancellationToken).ConfigureAwait (false);
			RequireMail (mail);
			receipt = receipt with { State = SubmissionDeliveryState.Submitted, Mail = mail, PendingStep = null, UpdatedUtc = DateTimeOffset.UtcNow };
			journal.Write (receipt);
			return receipt;
			}
		catch
			{
			journal.TryMarkUnknown (SubmissionDeliveryStep.Send);
			throw;
			}
		}

	/// <summary>Call only after a provider lookup or authorized operator establishes the outcome. Never infer non-delivery from a timeout.</summary>
	public static SubmissionDeliveryReceipt Reconcile (string privateJournalDirectory, SubmissionDeliveryPlan plan,
		SubmissionDeliveryStep step, bool performed, string evidence, SubmissionUploadReceipt? upload = null, SubmissionMailReceipt? mail = null)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (evidence);
		if (!Enum.IsDefined (step)) throw new ArgumentException ("Unknown delivery step.");
		using var journal = new Journal (privateJournalDirectory, PlanDigest (plan), DeliveryKey (plan));
		var receipt = journal.Read () ?? throw new InvalidOperationException ("There is no delivery attempt to reconcile.");
		if (receipt.State is not (SubmissionDeliveryState.UploadPending or SubmissionDeliveryState.SendPending or SubmissionDeliveryState.OutcomeUnknown) || receipt.PendingStep != step)
			throw new InvalidOperationException ("Only the outstanding uncertain step can be reconciled.");
		if (step == SubmissionDeliveryStep.Upload)
			{
			if (mail != null || (!performed && upload != null)) throw new ArgumentException ("Unexpected reconciliation receipt.");
			if (performed) RequireUpload (upload);
			receipt = receipt with { State = performed ? SubmissionDeliveryState.Uploaded : SubmissionDeliveryState.Prepared, Upload = upload };
			}
		else
			{
			if (upload != null || (!performed && mail != null)) throw new ArgumentException ("Unexpected reconciliation receipt.");
			RequireUpload (receipt.Upload);
			if (performed) RequireMail (mail);
			receipt = receipt with { State = performed ? SubmissionDeliveryState.Submitted : SubmissionDeliveryState.Uploaded, Mail = mail };
			}
		receipt = receipt with { PendingStep = null, UpdatedUtc = DateTimeOffset.UtcNow,
			Reconciliations = [.. receipt.Reconciliations ?? [], new (step, performed, evidence, DateTimeOffset.UtcNow)] };
		journal.Write (receipt);
		return receipt;
		}

	public static string PlanDigest (SubmissionDeliveryPlan plan)
		{
		ArgumentNullException.ThrowIfNull (plan);
		foreach (var digest in new[] { plan.CandidateSha256, plan.ReviewSha256, plan.AuthorizationSha256, plan.PackageSha256, plan.SignedFormSha256 })
			if (digest?.Length != 64 || !digest.All (c => char.IsAsciiDigit (c) || c is >= 'a' and <= 'f'))
				throw new ArgumentException ("Delivery requires independent lowercase SHA-256 pins.");
		foreach (var name in new[] { plan.PackageFileName, plan.SignedFormFileName })
			if (string.IsNullOrWhiteSpace (name) || name != Path.GetFileName (name) || name.IndexOfAny (['/', '\\', ':', '\r', '\n']) >= 0)
				throw new ArgumentException ("Delivery filenames must be plain basenames.");
		if (!plan.PackageFileName.EndsWith (".pkg", StringComparison.Ordinal) || !plan.SignedFormFileName.EndsWith (".pdf", StringComparison.Ordinal))
			throw new ArgumentException ("Delivery requires a driver package and signed PDF filename.");
		foreach (var address in new[] { plan.Sender, plan.Recipient })
			if (!MailAddress.TryCreate (address, out var parsed) || parsed.Address != address || address.IndexOfAny (['\r', '\n']) >= 0)
				throw new ArgumentException ("Delivery requires plain sender and recipient email addresses.");
		return Hash (JsonSerializer.SerializeToUtf8Bytes (plan, JsonOptions));
		}

	private static string Hash (byte[] bytes) => Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant ();
	private static string DeliveryKey (SubmissionDeliveryPlan plan) => Hash (JsonSerializer.SerializeToUtf8Bytes (
		new[] { plan.PackageSha256, plan.SignedFormSha256, plan.Sender.ToLowerInvariant (), plan.Recipient.ToLowerInvariant () }));
	private static MemoryStream OpenVerified (string path, string filename, string digest)
		{
		if (Path.GetFileName (path) != filename) throw new InvalidDataException ("Delivery filename differs from its authorized plan.");
		using var input = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (input.Length > 64L * 1024 * 1024) throw new InvalidDataException ("Each delivery file must fit within 64 MiB.");
		var bytes = new byte[checked((int)input.Length)];
		input.ReadExactly (bytes);
		if (input.ReadByte () != -1 || Hash (bytes) != digest)
			throw new InvalidDataException ("Delivery bytes differ from the authorized plan.");
		// Send these verified copies; later edits of the source path cannot change an upload.
		return new MemoryStream (bytes, writable: false);
		}
	private static void RequireUpload (SubmissionUploadReceipt? upload)
		{
		if (upload == null || !Uri.TryCreate (upload.DownloadUrl, UriKind.Absolute, out var url) || url.Scheme != "https" ||
			!string.IsNullOrEmpty (url.UserInfo) || string.IsNullOrWhiteSpace (upload.ProviderReceipt))
			throw new InvalidDataException ("A confirmed HTTPS download URL and provider receipt are required.");
		}
	private static void RequireMail (SubmissionMailReceipt? mail)
		{
		if (string.IsNullOrWhiteSpace (mail?.ProviderReceipt)) throw new InvalidDataException ("A confirmed mail-provider receipt is required.");
		}

	private sealed class Journal : IDisposable
		{
		private readonly string _digest;
		private readonly string _path;
		private readonly FileStream _lock;
		internal Journal (string directory, string digest, string deliveryKey)
			{
			if (!Path.IsPathFullyQualified (directory) || !Directory.Exists (directory)) throw new DirectoryNotFoundException ("Provide an existing private durable journal directory.");
			_digest = digest;
			_path = Path.Combine (directory, deliveryKey + ".json");
			_lock = new FileStream (Path.Combine (directory, deliveryKey + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			}
		internal SubmissionDeliveryReceipt? Read ()
			{
			if (!File.Exists (_path)) return null;
			var receipt = SubmissionValidation.ReadFile<SubmissionDeliveryReceipt> (_path);
			if (receipt.SchemaVersion != 1 || receipt.PlanSha256 != _digest || receipt.MessageId != "<crestron-" + _digest + "@submission.local>")
				throw new InvalidDataException ("Delivery journal identity is inconsistent.");
			if (!Enum.IsDefined (receipt.State) || receipt.UpdatedUtc == default ||
				(receipt.State == SubmissionDeliveryState.UploadPending && receipt.PendingStep != SubmissionDeliveryStep.Upload) ||
				(receipt.State == SubmissionDeliveryState.SendPending && receipt.PendingStep != SubmissionDeliveryStep.Send) ||
				(receipt.State == SubmissionDeliveryState.OutcomeUnknown && (receipt.PendingStep == null || !Enum.IsDefined (receipt.PendingStep.Value))) ||
				(receipt.State is SubmissionDeliveryState.Prepared or SubmissionDeliveryState.Uploaded or SubmissionDeliveryState.Submitted && receipt.PendingStep != null))
				throw new InvalidDataException ("Delivery journal state is inconsistent.");
			if (receipt.State is SubmissionDeliveryState.Uploaded or SubmissionDeliveryState.SendPending or SubmissionDeliveryState.Submitted || receipt.PendingStep == SubmissionDeliveryStep.Send)
				RequireUpload (receipt.Upload);
			if (receipt.State == SubmissionDeliveryState.Submitted) RequireMail (receipt.Mail);
			return receipt;
			}
		internal void Write (SubmissionDeliveryReceipt receipt)
			{
			var temporary = _path + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
			try
				{
				using (var output = new FileStream (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
					{
					JsonSerializer.Serialize (output, receipt, JsonOptions);
					output.Flush (flushToDisk: true);
					}
				File.Move (temporary, _path, overwrite: true);
				}
			finally { if (File.Exists (temporary)) File.Delete (temporary); }
			}
		internal void TryMarkUnknown (SubmissionDeliveryStep step)
			{
			try
				{
				var receipt = Read ();
				if (receipt != null) Write (receipt with { State = SubmissionDeliveryState.OutcomeUnknown, PendingStep = step, UpdatedUtc = DateTimeOffset.UtcNow });
				}
			catch { /* Preserve the original failure; a persisted Pending state also prohibits replay. */ }
			}
		public void Dispose () => _lock.Dispose ();
		}
	}