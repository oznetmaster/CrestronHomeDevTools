// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ProcessorCredentialSettingsTests
	{
	[Test]
	public void NamedProcessorCredentialsBindTargetTrustAndPortAndRejectMixedLogins ()
		{
		if (!OperatingSystem.IsWindows ())
			{
			Assert.Ignore ("Windows DPAPI test.");
			return;
			}
		string root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "processor-bindings-" + Guid.NewGuid ().ToString ("N"));
		var store = DevToolsPrivateStore.Create (Path.Combine (root, "store"));
		var credential = new DevToolsStoredCredential (DevToolsCredentialPurpose.Processor, "192.0.2.1", "synthetic", "SYNTHETIC-PROCESSOR", CertificateSha256: new string ('A', 64), SshFingerprint: "synthetic-ssh");
		store.SaveCredential ("processor", credential);
		string bindings = Path.Combine (root, "bindings.json");
		File.WriteAllText (bindings, JsonSerializer.Serialize (new DevToolsCredentialBindings (store.DirectoryPath, Processor: "processor")));
		var result = ProcessorCredentialSettings.Resolve (bindings, "192.0.2.1", new ());
		Assert.That (result.Password, Is.EqualTo (credential.Password));
		Assert.That (result.CertificateSha256, Is.EqualTo (credential.CertificateSha256));
		Assert.That (result.SshFingerprint, Is.EqualTo (credential.SshFingerprint));
		Assert.Throws<InvalidOperationException> (() => ProcessorCredentialSettings.Resolve (bindings, "192.0.2.2", new ()));
		Assert.Throws<InvalidOperationException> (() => ProcessorCredentialSettings.Resolve (bindings, "192.0.2.1", new () { HttpsPort = 8443 }));
		Assert.Throws<ArgumentException> (() => ProcessorCredentialSettings.Resolve (bindings, "192.0.2.1", new () { Password = "other" }));
		Assert.Throws<ArgumentException> (() => ProcessorCredentialSettings.Resolve (bindings, "192.0.2.1", new () { SshFingerprint = "other" }));
		store.SaveCredential ("processor", credential with
			{
			CertificateSha256 = null
			}, replace: true);
		Assert.Throws<InvalidOperationException> (() => ProcessorCredentialSettings.Resolve (bindings, "192.0.2.1", new ()));
		}
	}