// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

/// <summary>Dependency compatibility using disposable synthetic keys; not advisory exploit validation.</summary>
[TestFixture]
public sealed class PkcsDependencyTests
{
	[Test]
	public void SignedCmsAcceptsValidSyntheticSignatureAndRejectsAlteredContent ()
	{
		using var key = RSA.Create (2048);
		using var certificate = Certificate (key);
		var content = new byte[] { 10, 20, 30, 40 };
		var signed = new SignedCms (new ContentInfo (content), detached: true);
		signed.ComputeSignature (new CmsSigner (certificate));
		var encoded = signed.Encode ();
		var valid = new SignedCms (new ContentInfo (content), detached: true);
		valid.Decode (encoded);
		Assert.DoesNotThrow (() => valid.CheckSignature (verifySignatureOnly: true));
		var invalid = new SignedCms (new ContentInfo (new byte[] { 10, 20, 30, 41 }), detached: true);
		invalid.Decode (encoded);
		Assert.Throws<CryptographicException> (() => invalid.CheckSignature (verifySignatureOnly: true));
	}

	[Test]
	public void EnvelopedCmsRoundTripsSyntheticRecipientContent ()
	{
		using var key = RSA.Create (2048);
		using var certificate = Certificate (key);
		var content = new byte[] { 1, 3, 5, 7 };
		var envelope = new EnvelopedCms (new ContentInfo (content));
		envelope.Encrypt (new CmsRecipient (certificate));
		var decoded = new EnvelopedCms ();
		decoded.Decode (envelope.Encode ());
		decoded.Decrypt (new X509Certificate2Collection (certificate));
		Assert.That (decoded.ContentInfo.Content, Is.EqualTo (content));
	}

	[Test]
	public void MalformedCmsIsRejected ()
	{
		var invalid = new byte[] { 1, 2, 3 };
		Assert.Throws<CryptographicException> (() => new SignedCms ().Decode (invalid));
		Assert.Throws<CryptographicException> (() => new EnvelopedCms ().Decode (invalid));
	}

	private static X509Certificate2 Certificate (RSA key)
	{
		var request = new CertificateRequest ("CN=ephemeral-pkcs-offline-fixture", key,
			HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		var now = DateTimeOffset.UtcNow;
		return request.CreateSelfSigned (now.AddMinutes (-1), now.AddMinutes (5));
	}
}
