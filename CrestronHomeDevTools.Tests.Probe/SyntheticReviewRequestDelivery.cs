// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using CrestronHomeDevTools;

/// <summary>Test-only integration with an actual prepared request. Approval and both providers are simulated.
/// This executable has no implementation that uploads a package or sends email through this route.</summary>
internal static class SyntheticReviewRequestDelivery
	{
	internal static async Task<int> Run (string requestDirectory, string workDirectory, string change)
		{
		var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			Converters = { new JsonStringEnumConverter () } };
		string requestPath = Path.Combine (requestDirectory, "request-receipt.json");
		var receipt = JsonSerializer.Deserialize<SubmissionReviewRequestReceipt> (File.ReadAllBytes (requestPath), options)!;
		Directory.CreateDirectory (workDirectory);
		string journal = Path.Combine (workDirectory, "journal"), scratch = Path.Combine (workDirectory, "scratch");
		Directory.CreateDirectory (journal); Directory.CreateDirectory (scratch);
		string uploadReceipts = Directory.CreateDirectory (Path.Combine (workDirectory, "uploads")).FullName;
		string mailReceipts = Directory.CreateDirectory (Path.Combine (workDirectory, "mail")).FullName;
		string approvalPath = Path.Combine (workDirectory, "synthetic-approval.json");
		var plan = new SubmissionReviewDeliveryPlan (receipt.CandidateSha256, Digest (requestPath), new ('0', 64),
			receipt.PackageSha256, receipt.AttachmentSha256, receipt.PackageFileName, receipt.AttachmentFileName,
			"synthetic@example.test", "synthetic-recipient@example.test", SubmissionReviewMode.DeclaredGaps,
			SubmissionVerificationStatus.GapsDeclared, receipt.AttachmentKind, receipt.DeclarationsSha256,
			"SYNTHETIC: the generated PDF retains every unperformed test and reason.", "SYNTHETIC: signature and any explicitly omitted form are absent.");
		var preview = SubmissionReviewApproval.Preview (plan);
		File.WriteAllBytes (approvalPath, JsonSerializer.SerializeToUtf8Bytes (new SubmissionReviewApprovalDocument (1,
			preview.PacketSha256, preview.CorrespondenceSha256, DateTimeOffset.UtcNow.AddMinutes (10), true, true, true), options));
		plan = plan with { AuthorizationSha256 = Digest (approvalPath) };
		var settings = new SubmissionReviewRequestDispatchSettings (1, plan, requestDirectory, approvalPath, plan.AuthorizationSha256,
			journal, scratch, uploadReceipts, new ('a', 64), new ('b', 64), 30, mailReceipts, "smtp.example.test", 587, 30);
		string settingsPath = Path.Combine (workDirectory, "settings.json");
		File.WriteAllBytes (settingsPath, JsonSerializer.SerializeToUtf8Bytes (settings, options));
		var transport = new Transport ();
		if (change != "none") transport.AfterUpload = () => File.AppendAllText (
			change == "approval" ? approvalPath : Path.Combine (requestDirectory, "evidence.zip"), "changed by synthetic test");
		async Task<int> Invoke ()
			{
			using var output = new StringWriter ();
			using var error = new StringWriter ();
			using var credentials = new StringReader ("{\"uploadUserName\":\"synthetic\",\"uploadPassword\":\"synthetic-upload\",\"smtpUserName\":\"synthetic\",\"smtpPassword\":\"synthetic-mail\"}");
			return await SubmissionReviewRequestDispatchCommand.RunAsync (
				["--settings", settingsPath, "--settings-sha256", Digest (settingsPath), "--execute-approved"], credentials, output, error,
				execute: (parsed, _, token) => SubmissionReviewRequestDispatchCommand.DispatchAsync (parsed, transport, token));
			}
		int code = await Invoke ();
		if (code == 0 && await Invoke () != 0) throw new InvalidDataException ("Completed delivery was not idempotent.");
		Console.WriteLine (JsonSerializer.Serialize (new { state = SubmissionDelivery.ReadReview (journal, plan)?.Delivery.State.ToString (),
			transport.Uploads, transport.Sends, verification = plan.VerificationStatus.ToString (), syntheticTransport = true }));
		return code;
		}
	private static string Digest (string path) => Convert.ToHexStringLower (SHA256.HashData (File.ReadAllBytes (path)));
	private sealed class Transport : ISubmissionReviewDeliveryTransport
		{
		internal int Uploads, Sends;
		internal Action? AfterUpload;
		public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken cancellationToken)
			{
			Uploads++; AfterUpload?.Invoke ();
			return Task.FromResult (new SubmissionUploadReceipt ("https://synthetic.example.test/no-network", "simulated upload"));
			}
		public Task<SubmissionMailReceipt> SendReviewAsync (SubmissionReviewDeliveryPlan plan, SubmissionUploadReceipt upload,
			Stream attachment, string messageId, CancellationToken cancellationToken)
			{
			Sends++;
			return Task.FromResult (new SubmissionMailReceipt ("simulated mail"));
			}
		}
	}