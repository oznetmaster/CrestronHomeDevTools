// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class SubmissionSignatureInputTests
	{
	[Test]
	public void NamedSignatureUsesMemoryOnlyAndClearsBytesAfterUse ()
		{
		if (!OperatingSystem.IsWindows ())
			{
			Assert.Ignore ("Windows DPAPI test.");
			return;
			}
		string root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "signature-bindings-" + Guid.NewGuid ().ToString ("N"));
		var store = DevToolsPrivateStore.Create (Path.Combine (root, "store"));
		store.SaveSignature ("test", [1, 2, 3], ".png");
		string bindings = Path.Combine (root, "bindings.json");
		File.WriteAllText (bindings, JsonSerializer.Serialize (new DevToolsCredentialBindings (store.DirectoryPath, Signature: "test")));
		string[] before = Directory.GetFiles (root, "*", SearchOption.AllDirectories);
		var input = SubmissionSignatureInput.Prepare (["sign-self-test-form", "--authorization", "approval.json", "--credentials", bindings]);
		Assert.That (input.Arguments, Is.EqualTo (new[] { "sign-self-test-form", "--authorization", "approval.json", "--signature-stdin" }));
		Assert.That (input.Image, Is.EqualTo (new byte[] { 1, 2, 3 }));
		input.Dispose ();
		Assert.That (input.Image, Is.EqualTo (new byte[] { 0, 0, 0 }));
		Assert.That (Directory.GetFiles (root, "*", SearchOption.AllDirectories), Is.EquivalentTo (before));
		}

	[Test]
	public void OtherCommandsAndAmbiguousSignatureSourcesCannotUseSavedImage ()
		{
		Assert.Throws<ArgumentException> (() => SubmissionSignatureInput.Prepare (["build-help", "--credentials", "unused"]));
		Assert.Throws<ArgumentException> (() => SubmissionSignatureInput.Prepare (["sign-self-test-form", "--signature-image", "image", "--credentials", "unused"]));
		Assert.Throws<ArgumentException> (() => SubmissionSignatureInput.Prepare (["sign-self-test-form", "--signature-stdin", "--credentials", "unused"]));
		Assert.Throws<ArgumentException> (() => SubmissionSignatureInput.Prepare (["sign-self-test-form", "--credentials", "first", "--credentials", "second"]));
		}
    [Test]
    public void SigningPlanCannotResolveTheNamedCredentialPath()
        {
        string[] arguments = ["prepare-signing-plan", "--credentials", "nonexistent-private-binding"];
        using var input = SubmissionSignatureInput.Prepare(arguments);
        Assert.That(input.Image, Is.Null);
        Assert.That(input.Arguments, Is.EqualTo(arguments));
        }
    [Test]
    public void WrongReviewPinIsRejectedBeforeNamedSignatureStoreIsOpened()
        {
        string root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"signing-preflight-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
            {
            File.WriteAllText(Path.Combine(root,"review-receipt.json"),"{}");
            File.WriteAllText(Path.Combine(root,"COMPLETE"),new string('0',64));
            var settings=Path.Combine(root,"settings.json");
            File.WriteAllText(settings,JsonSerializer.Serialize(new {reviewDirectory=root,authorization=Path.Combine(root,"missing-approval")}));
            Assert.Throws<InvalidDataException>(()=>SubmissionSignatureInput.Prepare([
                "prepare-signed-review","--settings",settings,"--review-sha256",new string('0',64),
                "--authorization-sha256",new string('0',64),"--credentials",Path.Combine(root,"missing-store")]));
            }
        finally { Directory.Delete(root,true); }
        }

	[TestCase (false)]
	[TestCase (true)]
	public void CombinedAuthorityIsCheckedBeforeAnyNamedSignatureAccess (bool deliveryAuthorized)
		{
		string root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "combined-signing-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (root);
		try
			{
			string packet = Path.Combine (root, "packet.json"), approval = Path.Combine (root, "approval.json");
			File.WriteAllText (packet, "{\"schemaVersion\":1}");
			string Digest (string path) => Convert.ToHexStringLower (SHA256.HashData (File.ReadAllBytes (path)));
			File.WriteAllText (approval, JsonSerializer.Serialize (new
				{
				packetSha256 = Digest (packet), approvalMode = "CombinedSigningAndSubmission",
				signingAuthorized = true, deliveryAuthorized, visualReviewCompleted = true,
				expiresUtc = DateTimeOffset.UtcNow.AddMinutes (5)
				}));
			string[] args = ["sign-reviewed-packet", "--packet", packet, "--packet-sha256", Digest (packet),
				"--authorization", approval, "--authorization-sha256", Digest (approval)];
			if (deliveryAuthorized)
				{
				Assert.DoesNotThrow (() => SubmissionSignatureInput.CheckCombinedIdentity (args));
				File.AppendAllText (packet, " ");
				}
			Assert.Throws<InvalidDataException> (() => SubmissionSignatureInput.Prepare ([.. args, "--credentials", Path.Combine (root, "must-not-be-read.json")]));
			}
		finally { Directory.Delete (root, true); }
		}

	}
