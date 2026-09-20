// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class SubmissionReviewRequestDispatchTests
	{
	[TestCase ("valid")]
	[TestCase ("valid-signed")]
	[TestCase ("schema")]
	[TestCase ("pin")]
	[TestCase ("approval-pin")]
	[TestCase ("nested")]
	[TestCase ("unencrypted")]
	[TestCase ("signed")]
	[TestCase ("missing-flag")]
	[TestCase ("credentials")]
	[TestCase ("unknown")]
	[TestCase ("provider-error")]
	public async Task ProtectedRequestDispatchRejectsInvalidInputsWithoutExposingPrivateData (string variant)
		{
		string root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "request-command-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (root);
		try
			{
			string Dir (string name) => Directory.CreateDirectory (Path.Combine (root, name)).FullName;
			var plan = new SubmissionReviewDeliveryPlan (new ('a', 64), new ('b', 64), new ('c', 64), new ('d', 64), new ('e', 64),
				"Example_Device_IP.pkg", "request.pdf", "sender@example.test", "recipient@example.test", SubmissionReviewMode.DeclaredGaps,
				SubmissionVerificationStatus.GapsDeclared, SubmissionReviewAttachmentKind.UnsignedSelfTest, new ('f', 64), "Tests not performed.", "Signature omitted.");
			var settings = new SubmissionReviewRequestDispatchSettings (1, plan, Dir ("request"), Path.Combine (root, "approval.json"), plan.AuthorizationSha256,
				Dir ("journal"), Dir ("scratch"), Dir ("uploads"), new ('a', 64), new ('b', 64), 30, Dir ("mail"), "smtp.example.test", 587, 30);
			settings = variant switch
				{
				"schema" => settings with { SchemaVersion = 9 },
				"approval-pin" => settings with { ApprovalSha256 = new ('0', 64) },
				"nested" => settings with { ScratchDirectory = root },
				"unencrypted" => settings with { SmtpPort = 25 },
				"signed" => settings with { Plan = plan with { AttachmentKind = SubmissionReviewAttachmentKind.SignedSelfTest } },
				"valid-signed" => settings with { Plan = plan with { AttachmentKind = SubmissionReviewAttachmentKind.SignedSelfTest, DocumentOmissions = null } },
				_ => settings
				};
			var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter () } };
			var document = JsonSerializer.SerializeToNode (settings, options)!;
			if (variant == "unknown") document["unsupported"] = true;
			string path = Path.Combine (root, "settings.json");
			File.WriteAllText (path, document.ToJsonString ());
			string pin = Convert.ToHexStringLower (SHA256.HashData (File.ReadAllBytes (path)));
			string[] args = ["--settings", path, "--settings-sha256", variant == "pin" ? new ('0', 64) : pin, "--execute-approved"];
			if (variant == "missing-flag") args = args[..^1];
			using var input = new StringReader (variant == "credentials" ? "{}" :
				"{\"uploadUserName\":\"synthetic\",\"uploadPassword\":\"private-upload\",\"smtpUserName\":\"synthetic\",\"smtpPassword\":\"private-mail\"}");
			using var output = new StringWriter ();
			using var error = new StringWriter ();
			int calls = 0;
			int code = await SubmissionReviewRequestDispatchCommand.RunAsync (args, input, output, error,
				execute: (parsed, credentials, _) =>
					{
					calls++;
					Assert.That (credentials.SmtpPassword, Is.EqualTo ("private-mail"));
					if (variant == "provider-error") throw new IOException ("PRIVATE provider response https://secret.example.test");
					return Task.FromResult (new SubmissionDeliveryReceipt (1, SubmissionDelivery.ReviewPlanDigest (parsed.Plan),
						SubmissionDeliveryState.Submitted, "synthetic", DateTimeOffset.UtcNow));
					});
			Assert.That (code, Is.EqualTo (variant is "valid" or "valid-signed" ? 0 : 2));
			Assert.That (calls, Is.EqualTo (variant is "valid" or "valid-signed" or "provider-error" ? 1 : 0));
			Assert.That (output.ToString () + error, Does.Not.Contain ("private-mail").And.Not.Contain ("private-upload")
				.And.Not.Contain ("secret.example.test").And.Not.Contain (root));
			}
		finally { Directory.Delete (root, recursive: true); }
		}
	}