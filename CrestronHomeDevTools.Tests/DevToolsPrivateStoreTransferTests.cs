// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[Platform ("Win")]
[SupportedOSPlatform ("windows")]
public sealed class DevToolsPrivateStoreTransferTests
	{
	private string _root = null!;
	private DevToolsPrivateStore _source = null!, _destination = null!;
	[SetUp]
	public void Setup ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "transfer-" + Guid.NewGuid ().ToString ("N"));
		_source = DevToolsPrivateStore.Create (Path.Combine (_root, "source"));
		_destination = DevToolsPrivateStore.Create (Path.Combine (_root, "target's store"));
		_source.SaveCredential ("mail", new (DevToolsCredentialPurpose.Smtp, "smtp.example.test", "synthetic", "PRIVATE-TRANSFER-PASSWORD", 587, "from@example.test"));
		_source.SaveSignature ("signature", [1, 2, 3], ".jpg");
		}
	[TearDown]
	public void Cleanup () => Directory.Delete (_root, true);
	[Test]
	public async Task SelectedEntryIsReencryptedAndOthersAreNotProvisioned ()
		{
		byte[] bytes = _source.ExportSelectedEntry ("mail", "renamed");
		try
			{
			await _destination.ReceiveAsync ("renamed", new MemoryStream (bytes));
			}
		finally { CryptographicOperations.ZeroMemory (bytes); }
		Assert.That (_destination.ListNames (), Is.EqualTo (new[] { "renamed" }));
		Assert.That (_destination.LoadCredential ("renamed", DevToolsCredentialPurpose.Smtp, "smtp.example.test").Password, Is.EqualTo ("PRIVATE-TRANSFER-PASSWORD"));
		Assert.That (Encoding.UTF8.GetString (File.ReadAllBytes (Path.Combine (_destination.DirectoryPath, "renamed.private"))), Does.Not.Contain ("PRIVATE-TRANSFER-PASSWORD"));
		Assert.That (File.ReadAllBytes (Path.Combine (_destination.DirectoryPath, "renamed.private")), Is.Not.EqualTo (File.ReadAllBytes (Path.Combine (_source.DirectoryPath, "mail.private"))));
		}
	[Test]
	public async Task SignatureTransferPreservesBytesButDoesNotSignAnything ()
		{
		byte[] bytes = _source.ExportSelectedEntry ("signature", "signature");
		try
			{
			await _destination.ReceiveAsync ("signature", new MemoryStream (bytes));
			}
		finally { CryptographicOperations.ZeroMemory (bytes); }
		Assert.That (_destination.LoadSignature ("signature").Image, Is.EqualTo (new byte[] { 1, 2, 3 }));
		Assert.That (_destination.ListNames (), Is.EqualTo (new[] { "signature" }));
		}
	[Test]
	public async Task DestinationNameAndExplicitReplacementAreRequired ()
		{
		byte[] bytes = _source.ExportSelectedEntry ("mail", "mail");
		try
			{
			Assert.ThrowsAsync<InvalidDataException> (() => _destination.ReceiveAsync ("other", new MemoryStream (bytes)));
			await _destination.ReceiveAsync ("mail", new MemoryStream (bytes));
			Assert.ThrowsAsync<IOException> (() => _destination.ReceiveAsync ("mail", new MemoryStream (bytes)));
			await _destination.ReceiveAsync ("mail", new MemoryStream (bytes), replace: true);
			}
		finally { CryptographicOperations.ZeroMemory (bytes); }
		}
	[Test]
	public async Task WindowsReceiverUsesOnlyStdinAndDoesNotEchoSecrets ()
		{
		string console = Path.Combine (AppContext.BaseDirectory, "CrestronHomeDevTools.Console.exe");
		var target = new DevToolsPrivateStoreDestination ("target.example.test", 22, "synthetic-pin", console, _destination.DirectoryPath, "mail");
		string command = DevToolsPrivateStore.TransferCommand (target);
		Assert.That (command, Does.Not.Contain ("PRIVATE-TRANSFER-PASSWORD"));
		var start = new ProcessStartInfo (Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
			{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
			};
		foreach (string arg in command["powershell.exe ".Length..].Split (' '))
			start.ArgumentList.Add (arg);
		byte[] bytes = _source.ExportSelectedEntry ("mail", "mail");
		using var process = Process.Start (start)!;
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (45));
		try
			{
			Task<string> output = process.StandardOutput.ReadToEndAsync (deadline.Token);
			Task<string> error = process.StandardError.ReadToEndAsync (deadline.Token);
			await process.StandardInput.BaseStream.WriteAsync (bytes, deadline.Token);
			process.StandardInput.Close ();
			await process.WaitForExitAsync (deadline.Token);
			Assert.That (process.ExitCode, Is.Zero, await error);
			Assert.That ((await output).Trim (), Is.EqualTo ("private-entry-imported"));
			Assert.That (await error, Does.Not.Contain ("PRIVATE-TRANSFER-PASSWORD"));
			Assert.That (_destination.LoadCredential ("mail", DevToolsCredentialPurpose.Smtp, "smtp.example.test").Password, Is.EqualTo ("PRIVATE-TRANSFER-PASSWORD"));
			}
		finally
			{
			CryptographicOperations.ZeroMemory (bytes);
			if (!process.HasExited)
				{
				process.Kill (true);
				await process.WaitForExitAsync ();
				}
			}
		}
	}