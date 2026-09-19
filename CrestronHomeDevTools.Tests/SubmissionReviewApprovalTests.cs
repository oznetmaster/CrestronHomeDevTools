// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionReviewApprovalTests
	{
	private string _root = null!, _approvalPath = null!, _pin = null!;
	private SubmissionReviewDeliveryPlan _plan = null!;
	private SubmissionReviewApprovalDocument _approval = null!;
	private static readonly DateTimeOffset Now = new (2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
	private static readonly JsonSerializerOptions Options = new ()
		{ PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter () } };
	private static string Hash (byte[] data) => Convert.ToHexString (SHA256.HashData (data)).ToLowerInvariant ();

	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "review-approval-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		_approvalPath = Path.Combine (_root, "approval.json");
		_plan = new (new ('a', 64), new ('b', 64), new ('0', 64), new ('c', 64), new ('d', 64), "Example_Device_IP.pkg", "review.request.pdf",
			"sender@example.test", "recipient@example.test", SubmissionReviewMode.DeclaredGaps, SubmissionVerificationStatus.GapsDeclared,
			SubmissionReviewAttachmentKind.UnsignedSelfTest, new ('e', 64), "Recovery failed; endurance was not performed.", "Signature deliberately omitted.");
		var preview = SubmissionReviewApproval.Preview (_plan);
		_approval = new (1, preview.PacketSha256, preview.CorrespondenceSha256, Now.AddMinutes (10), true, true, true);
		Save ();
		}

	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);

	private void Save ()
		{
		File.WriteAllBytes (_approvalPath, JsonSerializer.SerializeToUtf8Bytes (_approval, Options));
		_pin = Hash (File.ReadAllBytes (_approvalPath));
		_plan = _plan with { AuthorizationSha256 = _pin };
		}
	private SubmissionDeliveryAuthorization Verify () => SubmissionReviewApproval.Verify (_plan, _approvalPath, _pin, Now);

	[Test]
	public void IndependentlyPinnedApprovalHasNoCircularHashAndPreservesExactExpiry ()
		{
		var result = Verify ();
		var preview = SubmissionReviewApproval.Preview (_plan);
		Assert.That (preview.DeliveryAuthorized, Is.False);
		Assert.That (preview.PacketSha256, Is.EqualTo (_approval.PacketSha256));
		Assert.That (preview.Correspondence.Subject, Is.EqualTo ("Driver Submission Package"));
		Assert.That (preview.Correspondence.Body, Does.Contain (_plan.GapSummary));
		Assert.That (preview.Correspondence.Body, Does.Contain (_plan.DocumentOmissions));
		Assert.That (result.PlanSha256, Is.EqualTo (SubmissionDelivery.ReviewPlanDigest (_plan)));
		Assert.That (result.PlanSha256, Is.Not.EqualTo (preview.PacketSha256));
		Assert.That (result.ExpiresUtc, Is.EqualTo (_approval.ExpiresUtc));
		Assert.That (SubmissionReviewApproval.Preview (_plan with { AuthorizationSha256 = new ('f', 64) }), Is.EqualTo (preview));
		Assert.That (SubmissionDelivery.ReviewPlanDigest (_plan with { AuthorizationSha256 = new ('f', 64) }), Is.Not.EqualTo (result.PlanSha256));
		}

	[TestCase ("candidate")]
	[TestCase ("review")]
	[TestCase ("package")]
	[TestCase ("attachment")]
	[TestCase ("package-name")]
	[TestCase ("attachment-name")]
	[TestCase ("recipient")]
	[TestCase ("sender")]
	[TestCase ("declarations")]
	[TestCase ("summary")]
	[TestCase ("omissions")]
	[TestCase ("kind")]
	[TestCase ("mode")]
	public void AnyReviewedPacketChangeInvalidatesTheExistingApproval (string change)
		{
		_plan = change switch
			{
			"candidate" => _plan with { CandidateSha256 = new ('f', 64) },
			"review" => _plan with { ReviewSha256 = new ('f', 64) },
			"package" => _plan with { PackageSha256 = new ('f', 64) },
			"attachment" => _plan with { AttachmentSha256 = new ('f', 64) },
			"package-name" => _plan with { PackageFileName = "Renamed_Device_IP.pkg" },
			"attachment-name" => _plan with { AttachmentFileName = "renamed.pdf" },
			"recipient" => _plan with { Recipient = "different@example.test" },
			"sender" => _plan with { Sender = "different@example.test" },
			"declarations" => _plan with { DeclarationsSha256 = new ('f', 64) },
			"summary" => _plan with { GapSummary = "Different outcome or explanation" },
			"omissions" => _plan with { DocumentOmissions = "Different omission reason" },
			"kind" => _plan with { AttachmentKind = SubmissionReviewAttachmentKind.DisclosureOnly },
			_ => _plan with { ReviewMode = SubmissionReviewMode.Complete, VerificationStatus = SubmissionVerificationStatus.CompleteAgainstInterpretedRequirements,
				AttachmentKind = SubmissionReviewAttachmentKind.SignedSelfTest, GapSummary = null, DocumentOmissions = null, DeclarationsSha256 = null }
			};
		Assert.Throws<InvalidDataException> (() => Verify ());
		}

	[TestCase ("visual")]
	[TestCase ("provenance")]
	[TestCase ("permission")]
	[TestCase ("expiry")]
	[TestCase ("packet")]
	[TestCase ("correspondence")]
	[TestCase ("schema")]
	public void MissingOrStaleAssertionsFailEvenWithAnUpdatedFilePin (string change)
		{
		_approval = change switch
			{
			"visual" => _approval with { VisualReviewCompleted = false },
			"provenance" => _approval with { ProducerAuthenticationConfirmed = false },
			"permission" => _approval with { DeliveryAuthorized = false },
			"expiry" => _approval with { ExpiresUtc = Now },
			"packet" => _approval with { PacketSha256 = new ('f', 64) },
			"correspondence" => _approval with { CorrespondenceSha256 = new ('f', 64) },
			_ => _approval with { SchemaVersion = 2 }
			};
		Save ();
		Assert.Throws<InvalidDataException> (() => Verify ());
		}

	[Test]
	public void ChangedApprovalBytesCannotReplaceIndependentAuthority ()
		{
		File.AppendAllText (_approvalPath, " ");
		Assert.Throws<InvalidDataException> (() => Verify ());
		string substituted = Hash (File.ReadAllBytes (_approvalPath));
		_plan = _plan with { AuthorizationSha256 = substituted };
		Assert.Throws<InvalidDataException> (() => Verify ());
		}

	[TestCase ("2026-09-19T12:10:00")]
	[TestCase ("2026-09-19T13:10:00+01:00")]
	public void ExpiryMustSpecifyUtcExplicitly (string expiry)
		{
		var json = JsonNode.Parse (File.ReadAllText (_approvalPath))!;
		json["expiresUtc"] = expiry;
		File.WriteAllText (_approvalPath, json.ToJsonString ());
		_pin = Hash (File.ReadAllBytes (_approvalPath));
		_plan = _plan with { AuthorizationSha256 = _pin };
		Assert.Throws<InvalidDataException> (() => Verify ());
		}

	[Test]
	public void DuplicateAuthorizationFieldsAreNotAccepted ()
		{
		string json = File.ReadAllText (_approvalPath).Replace ("\"deliveryAuthorized\":true", "\"deliveryAuthorized\":false,\"deliveryAuthorized\":true", StringComparison.Ordinal);
		File.WriteAllText (_approvalPath, json);
		_pin = Hash (File.ReadAllBytes (_approvalPath));
		_plan = _plan with { AuthorizationSha256 = _pin };
		Assert.Throws<JsonException> (() => Verify ());
		}

	[Test]
	public void ActualCliPreviewAndVerificationKeepPrivateCorrespondenceOutOfLogs ()
		{
		// CLI uses actual current time; keep the source test's fixed clock for the other cases.
		_approval = _approval with { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes (5) };
		Save ();
		string path = Path.Combine (_root, "plan.json"), previewPath = Path.Combine (_root, "preview.json"), resultPath = Path.Combine (_root, "verified.json");
		File.WriteAllBytes (path, JsonSerializer.SerializeToUtf8Bytes (_plan, Options));
		string pin = Hash (File.ReadAllBytes (path));
		using var output = new StringWriter ();
		using var error = new StringWriter ();
		Assert.That (SubmissionReviewApprovalCommand.Run (false, ["--plan", path, "--plan-file-sha256", pin, "--output", previewPath], output, error), Is.Zero);
		using var preview = JsonDocument.Parse (File.ReadAllText (previewPath));
		Assert.That (preview.RootElement.GetProperty ("deliveryAuthorized").GetBoolean (), Is.False);
		Assert.That (SubmissionReviewApprovalCommand.Run (true, ["--plan", path, "--plan-file-sha256", pin, "--approval", _approvalPath,
			"--approval-sha256", _pin, "--output", resultPath], output, error), Is.Zero);
		using var result = JsonDocument.Parse (File.ReadAllText (resultPath));
		Assert.That (result.RootElement.GetProperty ("planSha256").GetString (), Is.EqualTo (SubmissionDelivery.ReviewPlanDigest (_plan)));
		Assert.That (output.ToString () + error.ToString (), Does.Not.Contain (_plan.GapSummary));
		Assert.That (SubmissionReviewApprovalCommand.Run (false, ["--plan", path, "--plan-file-sha256", pin, "--output", previewPath], output, error), Is.EqualTo (2));
		File.AppendAllText (path, " ");
		Assert.That (SubmissionReviewApprovalCommand.Run (false, ["--plan", path, "--plan-file-sha256", pin,
			"--output", Path.Combine (_root, "wrong.json")], output, error), Is.EqualTo (2));
		Assert.That (File.Exists (Path.Combine (_root, "wrong.json")), Is.False);
		}
	}