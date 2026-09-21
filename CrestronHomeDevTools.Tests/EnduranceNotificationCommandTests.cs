// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Net;
using System.Text.Json;

using MimeKit;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class EnduranceNotificationCommandTests
	{
	private string _root = null!;
	private string[] _args = null!;
	private static readonly string RunId = new ('a', 64);
	private const string CredentialJson = "{\"userName\":\"synthetic\",\"password\":\"PRIVATE-PASSWORD\"}";
	[SetUp]
	public void SetUp ()
		{
		Directory.CreateDirectory (_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "notify-cli-" + Guid.NewGuid ().ToString ("N")));
		Directory.CreateDirectory (Path.Combine (_root, "journal"));
		File.WriteAllText (Path.Combine (_root, "settings.json"), JsonSerializer.Serialize (
			new SubmissionEnduranceNotificationSettings (RunId, "Synthetic monitor", "smtp.example.test", 587, "from@example.test", "to@example.test")));
		File.WriteAllText (Path.Combine (_root, "health.json"), JsonSerializer.Serialize (
			new SubmissionEnduranceHealthReport (SubmissionEnduranceHealthState.AttentionRequired, ["worker-unreachable"], DateTimeOffset.UtcNow, null, RunId)));
		_args = ["--settings", Path.Combine (_root, "settings.json"), "--health", Path.Combine (_root, "health.json"), "--journal", Path.Combine (_root, "journal"), "--send", "true"];
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, true);
	[TestCase (false)]
	[TestCase (true)]
	[Platform ("Win")]
	[System.Runtime.Versioning.SupportedOSPlatform ("windows")]
	public async Task StoredSmtpCredentialsPreserveDestinationBindingAndDuplicateSuppression (bool wrongSender)
		{
		var store = DevToolsPrivateStore.Create (Path.Combine (_root, "store"));
		store.SaveCredential ("mail", new (DevToolsCredentialPurpose.Smtp, "smtp.example.test", "synthetic", "PRIVATE-PASSWORD", 587,
			wrongSender ? "other@example.test" : "from@example.test"));
		string bindings = Path.Combine (_root, "bindings.json");
		File.WriteAllText (bindings, JsonSerializer.Serialize (new DevToolsCredentialBindings (store.DirectoryPath, Smtp: "mail")));
		var session = new Session ();
		using var output = new StringWriter ();
		using var error = new StringWriter ();
		for (int i = 0; i < 2; i++)
			{
			int result = await EnduranceNotificationCommand.RunAsync ([.. _args, "--credentials", bindings], new StringReader ("invalid-stdin"), output, error,
				CancellationToken.None, (s, c, d) =>
					{
						Assert.That (c.Password, Is.EqualTo ("PRIVATE-PASSWORD"));
						return new (s, c, d, () => session);
					});
			Assert.That (result, Is.EqualTo (wrongSender ? 3 : 0));
			}
		Assert.That (session.Sends, Is.EqualTo (wrongSender ? 0 : 1));
		Assert.That (output.ToString () + error, Does.Not.Contain ("PRIVATE-PASSWORD"));
		}
	[Test]
	public async Task CommandUsesStdinCredentialsAndRetainsDuplicateSuppression ()
		{
		var session = new Session ();
		var output = new StringWriter ();
		var error = new StringWriter ();
		SubmissionEnduranceNotifier Create (SubmissionEnduranceNotificationSettings settings, NetworkCredential credential, string directory)
			{
			Assert.That (credential.Password, Is.EqualTo ("PRIVATE-PASSWORD"));
			return new (settings, credential, directory, () => session);
			}
		Assert.That (await EnduranceNotificationCommand.RunAsync (_args, new StringReader (CredentialJson), output, error, CancellationToken.None, Create), Is.Zero);
		Assert.That (await EnduranceNotificationCommand.RunAsync (_args, new StringReader (CredentialJson), output, error, CancellationToken.None, Create), Is.Zero);
		Assert.That (session.Sends, Is.EqualTo (1));
		Assert.That (output.ToString (), Does.Contain ("Accepted").And.Contain ("AlreadyAccepted").And.Not.Contain ("PRIVATE-PASSWORD"));
		Assert.That (error.ToString (), Is.Empty);
		}
	[TestCase ("missing-send")]
	[TestCase ("duplicate")]
	[TestCase ("bad-json")]
	[TestCase ("oversized-credentials")]
	public async Task InvalidCommandInputsDoNotContactSmtp (string scenario)
		{
		var args = scenario switch
			{
				"missing-send" => _args[..^2],
				"duplicate" => [.. _args, "--send", "true"],
				_ => _args
				};
		string input = scenario switch
			{
				"bad-json" => "{PRIVATE-PASSWORD",
				"oversized-credentials" => new string ('x', 8193),
				_ => CredentialJson
				};
		var output = new StringWriter ();
		var error = new StringWriter ();
		int creates = 0;
		int result = await EnduranceNotificationCommand.RunAsync (args, new StringReader (input), output, error, CancellationToken.None,
			(s, c, d) => { creates++; return new (s, c, d, () => new Session ()); });
		Assert.That (result, Is.EqualTo (2));
		Assert.That (creates, Is.Zero);
		Assert.That (error.ToString (), Does.Not.Contain ("PRIVATE-PASSWORD"));
		}
	[Test]
	public async Task ActualQuietCliNeedsNoProcessorProfileAndNeverConnectsToSmtp ()
		{
		File.WriteAllText (Path.Combine (_root, "health.json"), JsonSerializer.Serialize (
			new SubmissionEnduranceHealthReport (SubmissionEnduranceHealthState.Collecting, [], DateTimeOffset.UtcNow, null, RunId)));
		var start = new ProcessStartInfo ("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _root };
		start.ArgumentList.Add (Path.Combine (AppContext.BaseDirectory, "CrestronHomeDevTools.Console.dll"));
		start.ArgumentList.Add ("endurance-notify");
		foreach (string argument in _args)
			start.ArgumentList.Add (argument);
		using var process = Process.Start (start)!;
		await process.StandardInput.WriteAsync (CredentialJson);
		process.StandardInput.Close ();
		var output = process.StandardOutput.ReadToEndAsync ();
		var error = process.StandardError.ReadToEndAsync ();
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (20));
		try
			{
			await process.WaitForExitAsync (deadline.Token);
			}
		finally { if (!process.HasExited) { process.Kill (true); await process.WaitForExitAsync (); } }
		Assert.That (process.ExitCode, Is.Zero, await error);
		Assert.That (await output, Does.Contain ("Quiet").And.Not.Contain ("PRIVATE-PASSWORD"));
		}
	private sealed class Session : ISubmissionSmtpSession
		{
		internal int Sends;
		public Task ConnectAsync (string host, int port, NetworkCredential credential, CancellationToken token) => Task.CompletedTask;
		public Task<string> SendAsync (MimeMessage message, CancellationToken token)
			{
			Sends++;
			return Task.FromResult ("synthetic acceptance");
			}
		public void Dispose ()
			{
			}
		}
	}