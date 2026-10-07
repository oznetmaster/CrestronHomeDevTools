// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;

namespace CrestronHomeDevTools;

/// <summary>
/// Local rehearsal destination for the shared delivery coordinator. Never opens a network connection.
/// Receipts confirm local retention only, never Crestron upload, email delivery or certification.
/// Use a separate private directory and journal from production; the caller still validates evidence and signing authority.
/// </summary>
public sealed class SubmissionRehearsalTransport : ISubmissionDeliveryTransport
	{
	public SubmissionDeliveryEnvironment Environment => SubmissionDeliveryEnvironment.Rehearsal;
	private const int MAX_FILE_BYTES = 64 * 1024 * 1024;
	private static readonly JsonSerializerOptions Json = new () { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
	private sealed record Binding (int SchemaVersion, string Mode, string PlanSha256, bool ExternalDeliveryAttempted);
	private readonly string _root;
	private readonly SubmissionDeliveryPlan _plan;
	private readonly string _digest;
	private SubmissionUploadReceipt UploadReceipt => new (_plan.RehearsalPackageDownloadUrl ?? "https://rehearsal.invalid/" + _digest,
			(_plan.RehearsalPackageDownloadUrl == null ? "rehearsal-local-upload:" : "rehearsal-retained-package-link:") + _digest);

	public SubmissionRehearsalTransport (string privateDirectory, SubmissionDeliveryPlan plan)
		: this (privateDirectory, plan, false) { }

	internal SubmissionRehearsalTransport (string privateDirectory, SubmissionDeliveryPlan plan, bool sendEmail, string? reviewDigest = null)
		{
		string artifactDigest = SubmissionDelivery.PlanDigest (plan);
		_digest = reviewDigest ?? artifactDigest;
		if (plan.Environment != Environment || plan.SendRehearsalEmail != sendEmail) throw new InvalidDataException ("Select an approved rehearsal plan.");
		_plan = plan;
		if (!Path.IsPathFullyQualified (privateDirectory) || !Directory.Exists (privateDirectory))
			throw new DirectoryNotFoundException ("Provide an existing private rehearsal destination.");
		_root = Path.GetFullPath (privateDirectory);
		CheckDirectory ();
		string marker = Path.Combine (_root, "rehearsal-destination.json");
		if (!File.Exists (marker))
			{
			if (Directory.EnumerateFileSystemEntries (_root).Any ())
				throw new InvalidDataException ("An unbound rehearsal destination must be empty.");
			WriteJson ("rehearsal-destination.json", new Binding (1, "Rehearsal", _digest, false));
			}
		CheckBinding ();
		}

	public async Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken cancellationToken)
		{
		CheckBinding ();
		if (filename != _plan.PackageFileName)
			throw new InvalidDataException ("Rehearsal package filename differs from the bound plan.");
		byte[] bytes = await ReadVerified (package, _plan.PackageSha256, cancellationToken).ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
		WriteBytes ("package.pkg", bytes);
		WriteJson ("upload-receipt.json", new { schemaVersion = 1, mode = "Rehearsal", externalDeliveryAttempted = false,
			planSha256 = _digest, filename, packageSha256 = _plan.PackageSha256, receipt = UploadReceipt });
		return UploadReceipt;
		}

	public async Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload,
		Stream signedForm, string messageId, CancellationToken cancellationToken)
		{
		if (_plan.SendRehearsalEmail) throw new InvalidDataException ("An email rehearsal cannot be completed by local-only retention.");
		using var retainedPackage = OpenPackageForMail (plan, upload, messageId);
		byte[] bytes = await ReadVerified (signedForm, _plan.SignedFormSha256, cancellationToken).ConfigureAwait (false);
		cancellationToken.ThrowIfCancellationRequested ();
		WriteBytes ("signed-form.pdf", bytes);
		var mail = new SubmissionMailReceipt ("rehearsal-local-mail:" + _digest);
		WriteJson ("mail-receipt.json", new { schemaVersion = 1, mode = "Rehearsal", externalDeliveryAttempted = false,
			planSha256 = _digest, messageId, sender = plan.Sender, recipient = plan.Recipient,
			signedFormSha256 = plan.SignedFormSha256, downloadUrl = upload.DownloadUrl, receipt = mail });
		return mail;
		}

	internal Stream OpenPackageForMail (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload, string messageId)
		{
		return OpenPackageForMail (SubmissionDelivery.PlanDigest (plan), upload, messageId);
		}

	internal Stream OpenPackageForMail (string digest, SubmissionUploadReceipt upload, string messageId)
		{
		CheckBinding ();
		if (digest != _digest || upload != UploadReceipt ||
			messageId != "<crestron-" + _digest + "@submission.local>")
			throw new InvalidDataException ("Rehearsal send differs from its bound plan or upload.");
		// Require the actual locally accepted upload, not a receipt invented by a caller.
		using (var receipt = JsonDocument.Parse (ReadRetained ("upload-receipt.json", 16384)))
			{
			var saved = receipt.RootElement;
			if (saved.GetProperty ("mode").GetString () != "Rehearsal" || saved.GetProperty ("externalDeliveryAttempted").GetBoolean () ||
				saved.GetProperty ("planSha256").GetString () != _digest || saved.GetProperty ("packageSha256").GetString () != _plan.PackageSha256 ||
				saved.GetProperty ("filename").GetString () != _plan.PackageFileName ||
				saved.GetProperty ("receipt").Deserialize<SubmissionUploadReceipt> (Json) != UploadReceipt)
				throw new InvalidDataException ("Retained rehearsal upload changed.");
			}
		byte[] bytes = ReadRetained ("package.pkg", MAX_FILE_BYTES);
		if (Hash (bytes) != _plan.PackageSha256)
			throw new InvalidDataException ("Retained rehearsal package changed.");
		return new MemoryStream (bytes, writable: false);
		}

	private void CheckDirectory ()
		{
		for (string? path = _root; path != null; path = Path.GetDirectoryName (path))
			if ((File.GetAttributes (path) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("Rehearsal storage cannot be redirected.");
		}
	private void CheckBinding ()
		{
		CheckDirectory ();
		var binding = SubmissionValidation.Read<Binding> (ReadRetained ("rehearsal-destination.json", 16384));
		if (binding != new Binding (1, "Rehearsal", _digest, false))
			throw new InvalidDataException ("Rehearsal destination belongs to another plan or environment.");
		}
	private byte[] ReadRetained (string name, int maximum)
		{
		if (!SubmissionEvidence.SafeEvidencePath (_root, name, out string path))
			throw new InvalidDataException ("Rehearsal evidence is missing or redirected.");
		using var input = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (input.Length > maximum)
			throw new InvalidDataException ("Rehearsal evidence exceeds its limit.");
		var bytes = new byte[checked((int)input.Length)];
		input.ReadExactly (bytes);
		return bytes;
		}
	private void WriteJson<T> (string name, T value) => WriteBytes (name, JsonSerializer.SerializeToUtf8Bytes (value, Json));
	private void WriteBytes (string name, byte[] bytes)
		{
		CheckDirectory ();
		using var output = new FileStream (Path.Combine (_root, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
		output.Write (bytes);
		output.Flush (flushToDisk: true);
		}
	private static string Hash (byte[] bytes) => Convert.ToHexStringLower (SHA256.HashData (bytes));
	private static async Task<byte[]> ReadVerified (Stream stream, string expected, CancellationToken token)
		{
		ArgumentNullException.ThrowIfNull (stream);
		using var output = new MemoryStream ();
		var buffer = new byte[81920];
		while (true)
			{
			int read = await stream.ReadAsync (buffer.AsMemory (), token).ConfigureAwait (false);
			if (read == 0)
				break;
			if (output.Length + read > MAX_FILE_BYTES)
				throw new InvalidDataException ("Rehearsal delivery file exceeds 64 MiB.");
			output.Write (buffer, 0, read);
			}
		byte[] bytes = output.ToArray ();
		if (Hash (bytes) != expected)
			throw new InvalidDataException ("Rehearsal file differs from the bound plan.");
		return bytes;
		}
	}