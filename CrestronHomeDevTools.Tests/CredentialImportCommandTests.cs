// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture, Platform ("Win")]
[SupportedOSPlatform ("windows")]
public sealed class CredentialImportCommandTests
	{
	[TestCase (false)]
	[TestCase (true)]
	public async Task ActualCliImportsRedirectedUtf8WithOrWithoutWindowsPowerShellPreamble (bool emitPreamble)
		{
		string path = Path.Combine (TestContext.CurrentContext.WorkDirectory, "stdin-import-" + Guid.NewGuid ().ToString ("N"));
		var store = DevToolsPrivateStore.Create (path);
		var start = new ProcessStartInfo ("dotnet")
			{
			UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
			RedirectStandardOutput = true, RedirectStandardError = true,
			StandardInputEncoding = new UTF8Encoding (emitPreamble)
			};
		foreach (string argument in new[] { Path.Combine (AppContext.BaseDirectory, "CrestronHomeDevTools.Console.dll"), "credentials", "import", "--name", "monitor", "--store", path })
			start.ArgumentList.Add (argument);
		using var process = Process.Start (start)!;
		await process.StandardInput.WriteAsync ("{\"Purpose\":\"Windows\",\"Host\":\"example.invalid\",\"UserName\":\"synthetic\",\"Password\":\"SYNTHETIC-IMPORT-SECRET-\u00e9\",\"Port\":22}");
		process.StandardInput.Close ();
		var output = process.StandardOutput.ReadToEndAsync ();
		var error = process.StandardError.ReadToEndAsync ();
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (20));
		try { await process.WaitForExitAsync (deadline.Token); }
		finally { if (!process.HasExited) { process.Kill (true); await process.WaitForExitAsync (); } }
		Assert.That (process.ExitCode, Is.Zero, await error);
		Assert.That (await output + await error, Does.Not.Contain ("SYNTHETIC-IMPORT-SECRET"));
		Assert.That (store.LoadCredential ("monitor", DevToolsCredentialPurpose.Windows, "example.invalid").Password, Is.EqualTo ("SYNTHETIC-IMPORT-SECRET-\u00e9"));
		Assert.That (Encoding.UTF8.GetString (File.ReadAllBytes (Path.Combine (path, "monitor.private"))), Does.Not.Contain ("SYNTHETIC-IMPORT-SECRET"));
		store.Remove ("monitor");
		}
	}
