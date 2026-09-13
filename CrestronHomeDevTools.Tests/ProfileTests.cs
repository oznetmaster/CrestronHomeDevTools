// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ProfileTests
	{
	[Test]
	public void SavedProfileIsEncryptedAndRoundTripsAllConnectionSettings ()
		{
		if (!OperatingSystem.IsWindows ())
			Assert.Ignore ("Windows account encryption test.");
		if (!OperatingSystem.IsWindows ())
			return;
		var folder = Path.Combine (Path.GetTempPath (), "CrestronHomeDevTools-tests", Guid.NewGuid ().ToString ("N"));
		try
			{
			var store = new ProfileStore (folder);
			var settings = new ConsoleSettings { Host = "processor.example", UserName = "test-user", Password = "private-password-sentinel", CertificateSha256 = new string ('A', 64), SshFingerprint = "example-ssh-fingerprint" };
			store.Save ("test", settings);
			Assert.That (store.Load ("test"), Is.EqualTo (settings));
			Assert.That (Encoding.UTF8.GetString (File.ReadAllBytes (store.GetPath ("test"))), Does.Not.Contain ("private-password-sentinel").And.Not.Contain ("processor.example"));
			store.Save ("test", settings with
				{
				Password = "replacement"
				});
			Assert.That (store.Load ("test").Password, Is.EqualTo ("replacement"));
			Assert.That (Directory.GetFiles (folder, "*.tmp"), Is.Empty);
			}
		finally { if (Directory.Exists (folder)) { foreach (var file in Directory.GetFiles (folder)) File.Delete (file); Directory.Delete (folder); } }
		}

	[Test]
	public void TamperedProfileCannotRedirectCredentialsToAnotherHost ()
		{
		if (!OperatingSystem.IsWindows ())
			Assert.Ignore ("Windows account encryption test.");
		if (!OperatingSystem.IsWindows ())
			return;
		var folder = Path.Combine (Path.GetTempPath (), "CrestronHomeDevTools-tests", Guid.NewGuid ().ToString ("N"));
		try
			{
			var store = new ProfileStore (folder);
			store.Save ("test", new ConsoleSettings { Host = "processor.example", Password = "sentinel" });
			var bytes = File.ReadAllBytes (store.GetPath ("test"));
			bytes[^1] ^= 1;
			File.WriteAllBytes (store.GetPath ("test"), bytes);
			Assert.Throws<CryptographicException> (() => { if (OperatingSystem.IsWindows ()) store.Load ("test"); });
			}
		finally { if (Directory.Exists (folder)) { foreach (var file in Directory.GetFiles (folder)) File.Delete (file); Directory.Delete (folder); } }
		}

	[TestCase ("../outside")]
	[TestCase ("C:/outside")]
	[TestCase ("a/b")]
	[TestCase ("")]
	public void ProfileNamesCannotEscapeStorageDirectory (string name)
		 => Assert.Throws<ArgumentException> (() => new ProfileStore ().GetPath (name));
	}