// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Runtime.Versioning;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
[SupportedOSPlatform ("windows")]
public sealed class DevToolsPrivateStoreTests
	{
	[Test]
	public void UserStoreEncryptsInputsAndBindsCredentialUse ()
		{
		if (!OperatingSystem.IsWindows ())
			{
			Assert.Ignore ("Windows DPAPI test.");
			return;
			}
		string path = Path.Combine (TestContext.CurrentContext.WorkDirectory, "private-store-" + Guid.NewGuid ().ToString ("N"));
		var store = DevToolsPrivateStore.Create (path);
		var credential = new DevToolsStoredCredential (DevToolsCredentialPurpose.Smtp, "mail.example.invalid", "synthetic", "SYNTHETIC-SECRET", 587, "sender@example.invalid");
		store.SaveCredential ("mail", credential);
		Assert.That (store.ListNames (), Is.EqualTo (new[] { "mail" }));
		Assert.That (Encoding.UTF8.GetString (File.ReadAllBytes (Path.Combine (path, "mail.private"))), Does.Not.Contain (credential.Password));
		Assert.That (DevToolsPrivateStore.Open (path).LoadCredential ("mail", DevToolsCredentialPurpose.Smtp, "MAIL.EXAMPLE.INVALID"), Is.EqualTo (credential));
		Assert.Throws<InvalidOperationException> (() => store.LoadCredential ("mail", DevToolsCredentialPurpose.Windows, credential.Host));
		Assert.Throws<InvalidOperationException> (() => store.LoadCredential ("mail", DevToolsCredentialPurpose.Smtp, "other.example.invalid"));
		Assert.Throws<IOException> (() => store.SaveCredential ("mail", credential));
		Assert.That (credential.ToString (), Does.Not.Contain (credential.Password));
		store.Remove ("mail");
		Assert.That (store.ListNames (), Is.Empty);
		}

	[Test]
	public void ProvisionCopiesOnlySelectedEntryWithReadOnlyServiceAccess ()
		{
		if (!OperatingSystem.IsWindows ())
			{
			Assert.Ignore ("Windows DPAPI test.");
			return;
			}
		string root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "private-service-" + Guid.NewGuid ().ToString ("N"));
		var user = DevToolsPrivateStore.Create (Path.Combine (root, "user"));
		var service = DevToolsPrivateStore.Create (Path.Combine (root, "service"), "S-1-5-19");
		user.SaveCredential ("mail", new (DevToolsCredentialPurpose.Smtp, "mail.example.invalid", "synthetic", "SYNTHETIC-SECRET"));
		user.SaveSignature ("signature", [1, 2, 3, 4], ".jpg");
		user.Provision ("mail", service);
		Assert.That (service.ListNames (), Is.EqualTo (new[] { "mail" }));
		Assert.That (service.LoadCredential ("mail", DevToolsCredentialPurpose.Smtp, "mail.example.invalid").Password, Is.EqualTo ("SYNTHETIC-SECRET"));
		Assert.Throws<FileNotFoundException> (() => service.LoadSignature ("signature"));
		var acl = new DirectoryInfo (service.DirectoryPath).GetAccessControl ();
		Assert.That (acl.AreAccessRulesProtected, Is.True);
		var rules = acl.GetAccessRules (true, true, typeof (SecurityIdentifier)).Cast<FileSystemAccessRule> ().ToArray ();
		var serviceRule = rules.Single (r => r.IdentityReference.Value == "S-1-5-19");
		Assert.That ((int)(serviceRule.FileSystemRights & FileSystemRights.Write), Is.Zero);
		var signature = user.LoadSignature ("signature");
		Assert.That (signature.Image, Is.EqualTo (new byte[] { 1, 2, 3, 4 }));
		Assert.Throws<ArgumentException> (() => user.Remove ("../escape"));
		File.Copy (Path.Combine (root, "user", "signature.private"), Path.Combine (root, "user", "renamed.private"));
		Assert.Throws<InvalidDataException> (() => user.LoadSignature ("renamed"));
		}
	}