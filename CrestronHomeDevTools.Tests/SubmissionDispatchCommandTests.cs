// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class SubmissionDispatchCommandTests
	{
	private string _root = null!, _path = null!;
	private SubmissionDispatchSettings _settings = null!;
	private static readonly JsonSerializerOptions Json = new () { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
	private const string Credentials = "{\"uploadUserName\":\"uploader\",\"uploadPassword\":\"private-upload\",\"smtpUserName\":\"mailbox\",\"smtpPassword\":\"private-smtp\"}";
	private static string Hash (byte[] bytes) => Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant ();
	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "dispatch-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		string Dir (string name) => Directory.CreateDirectory (Path.Combine (_root, name)).FullName;
		string package = Path.Combine (_root, "test.pkg"), form = Path.Combine (_root, "form.pdf");
		File.WriteAllText (package, "synthetic package");
		File.WriteAllText (form, "synthetic form");
		var plan = new SubmissionDeliveryPlan (new ('a', 64), new ('b', 64), new ('c', 64), Hash (File.ReadAllBytes (package)), Hash (File.ReadAllBytes (form)),
			"test.pkg", "form.pdf", "sender@example.test", "recipient@example.test");
		var revalidation = new SubmissionDeliveryRevalidationSettings ("unused-python", new ('d', 64), "unused-dotnet", new ('e', 64),
			Dir ("tools"), [], Dir ("validator"), [], "unused-preparation", new ('f', 64), Dir ("prepared"), Dir ("revalidation"), new ('a', 64), TimeSpan.FromSeconds (5));
		_settings = new (1, plan, Dir ("journal"), package, form, revalidation, Dir ("upload"), new ('b', 64), new ('c', 64), 30, Dir ("mail"), "smtp.example.test", 587, 30);
		_path = Path.Combine (_root, "settings.json");
		Save (_settings);
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);
	private void Save (SubmissionDispatchSettings settings) => File.WriteAllText (_path, JsonSerializer.Serialize (settings, Json));
	private string[] Args () => ["--settings", _path, "--settings-sha256", Hash (File.ReadAllBytes (_path)), "--execute-approved"];
	private SubmissionDeliveryReceipt Submitted () => new (1, SubmissionDelivery.PlanDigest (_settings.Plan), SubmissionDeliveryState.Submitted, "test", DateTimeOffset.UtcNow);
	[TestCase ("valid")]
	[TestCase ("wrong-smtp-host")]
	[TestCase ("wrong-sender")]
	[TestCase ("wrong-uploader")]
	[TestCase ("wrong-purpose")]
	[TestCase ("changed-settings")]
	[TestCase ("missing-approval")]
	[Platform ("Win")]
	[System.Runtime.Versioning.SupportedOSPlatform ("windows")]
	public async Task SavedEntriesRespectEndpointsSettingsAndExplicitExecution (string scenario)
		{
		var store = DevToolsPrivateStore.Create (Path.Combine (_root, "private-store"));
		store.SaveCredential ("mail", new (DevToolsCredentialPurpose.Smtp,
			scenario == "wrong-smtp-host" ? "other.example.test" : _settings.SmtpHost,
			"mailbox", "private-smtp", _settings.SmtpPort,
			scenario == "wrong-sender" ? "other@example.test" : _settings.Plan.Sender));
		store.SaveCredential ("upload", new (scenario == "wrong-purpose" ? DevToolsCredentialPurpose.Windows : DevToolsCredentialPurpose.Uploader,
			scenario == "wrong-uploader" ? "other.example.test" : "uploader.crestron.com", "uploader", "private-upload"));
		string bindings = Path.Combine (_root, "bindings.json");
		File.WriteAllText (bindings, JsonSerializer.Serialize (new DevToolsCredentialBindings (store.DirectoryPath, Smtp: "mail", Uploader: "upload")));
		string[] args = [.. Args (), "--credentials", bindings];
		if (scenario == "changed-settings")
			File.AppendAllText (_path, " ");
		if (scenario == "missing-approval")
			args = args.Where (a => a != "--execute-approved").ToArray ();
		using var output = new StringWriter ();
		using var error = new StringWriter ();
		var transport = new Transport ();
		int checks = 0;
		int result = await SubmissionDispatchCommand.RunAsync (args, new RefuseInput (), output, error,
			execute: (settings, credentials, token) =>
				{
					Assert.That (credentials.SmtpPassword, Is.EqualTo ("private-smtp"));
					Assert.That (credentials.UploadPassword, Is.EqualTo ("private-upload"));
					Assert.That (credentials.ToString (), Does.Not.Contain ("private-smtp"));
					return SubmissionDispatchCommand.DispatchAsync (settings, transport, (_, _) =>
						{
							checks++;
							return Task.FromResult (new SubmissionDeliveryAuthorization (SubmissionDelivery.PlanDigest (settings.Plan), DateTimeOffset.UtcNow.AddMinutes (1)));
						}, token);
				});
		Assert.That ((result, transport.Uploads, transport.Sends, checks),
			Is.EqualTo (scenario == "valid" ? (0, 1, 1, 2) : (2, 0, 0, 0)));
		Assert.That (output.ToString () + error, Does.Not.Contain ("private-smtp").And.Not.Contain ("private-upload"));
		}
	private sealed class RefuseInput : TextReader
		{
		public override ValueTask<int> ReadAsync (Memory<char> buffer, CancellationToken cancellationToken = default)
			=> throw new AssertionException ("Saved credentials must not read standard input.");
		}
	[TestCase ("")]
	[TestCase ("\uFEFF")]
	public async Task PinnedSettingsAndStdinCredentialsReachDispatchWithoutBeingPrinted (string prefix)
		{
		using var output = new StringWriter ();
		using var error = new StringWriter ();
		int calls = 0;
		int result = await SubmissionDispatchCommand.RunAsync (Args (), new StringReader (prefix + Credentials), output, error, execute: (settings, credentials, _) =>
			{
				calls++;
				Assert.That (settings.Plan, Is.EqualTo (_settings.Plan));
				Assert.That (credentials.SmtpPassword, Is.EqualTo ("private-smtp"));
				Assert.That (credentials.UploadPassword, Is.EqualTo ("private-upload"));
				return Task.FromResult (Submitted ());
			});
		Assert.That ((result, calls), Is.EqualTo ((0, 1)));
		Assert.That (error.ToString (), Is.Empty);
		Assert.That (output.ToString (), Does.Contain ("Submitted").And.Not.Contain ("private").And.Not.Contain ("example.test"));
		}
	[TestCase ("hash")]
	[TestCase ("missing-flag")]
	[TestCase ("unknown-setting")]
	[TestCase ("duplicate-setting")]
	[TestCase ("missing-setting")]
	[TestCase ("relative-package")]
	[TestCase ("nested-receipts")]
	[TestCase ("unencrypted-port")]
	[TestCase ("oversized-settings")]
	public async Task InvalidSettingsNeverDispatch (string failure)
		{
		string[] args = Args ();
		if (failure == "hash")
			args[3] = new ('0', 64);
		else if (failure == "missing-flag")
			args = args[..^1];
		else
			{
			if (failure == "unknown-setting")
				File.WriteAllText (_path, File.ReadAllText (_path).Insert (1, "\"unreviewed\":true,"));
			if (failure == "duplicate-setting")
				File.WriteAllText (_path, File.ReadAllText (_path).Insert (1, "\"schemaVersion\":1,"));
			if (failure == "missing-setting")
				File.WriteAllText (_path, File.ReadAllText (_path).Replace ("\"schemaVersion\":1,", ""));
			if (failure == "relative-package")
				Save (_settings with
					{
					PackagePath = "test.pkg"
					});
			if (failure == "nested-receipts")
				Save (_settings with
					{
					MailReceiptDirectory = Directory.CreateDirectory (Path.Combine (_settings.JournalDirectory, "mail")).FullName
					});
			if (failure == "unencrypted-port")
				Save (_settings with
					{
					SmtpPort = 25
					});
			if (failure == "oversized-settings")
				File.WriteAllText (_path, new string (' ', 1024 * 1024 + 1));
			args = Args ();
			}
		int calls = 0;
		using var output = new StringWriter ();
		using var error = new StringWriter ();
		int result = await SubmissionDispatchCommand.RunAsync (args, new StringReader (Credentials), output, error,
			execute: (_, _, _) => { calls++; return Task.FromResult (Submitted ()); });
		Assert.That ((result, calls), Is.EqualTo ((2, 0)));
		Assert.That (output.ToString (), Is.Empty);
		Assert.That (error.ToString (), Does.Not.Contain (_root).And.Not.Contain ("private-smtp"));
		}
	[TestCase ("")]
	[TestCase ("{\"smtpPassword\":\"private-smtp\"}")]
	[TestCase ("oversized")]
	[TestCase ("duplicate")]
	public async Task InvalidCredentialsNeverDispatch (string input)
		{
		if (input == "oversized")
			input = new string ('x', 32769);
		if (input == "duplicate")
			input = Credentials.Insert (1, "\"smtpPassword\":\"other-password\",");
		int calls = 0;
		int result = await SubmissionDispatchCommand.RunAsync (Args (), new StringReader (input), TextWriter.Null, TextWriter.Null,
			execute: (_, _, _) => { calls++; return Task.FromResult (Submitted ()); });
		Assert.That ((result, calls), Is.EqualTo ((2, 0)));
		}
	[Test]
	public async Task ProviderFailureNeverLeaksTheExceptionMessage ()
		{
		using var output = new StringWriter ();
		using var error = new StringWriter ();
		int result = await SubmissionDispatchCommand.RunAsync (Args (), new StringReader (Credentials), output, error,
			execute: (_, _, _) => throw new IOException ("private-smtp and private-download-url"));
		Assert.That (result, Is.EqualTo (2));
		Assert.That (output.ToString (), Is.Empty);
		Assert.That (error.ToString (), Does.Contain ("IOException").And.Not.Contain ("private-smtp").And.Not.Contain ("private-download-url"));
		}
	[Test]
	public async Task DispatchRevalidatesBothStepsAndCompletedReplayDoesNothing ()
		{
		var steps = new List<SubmissionDeliveryStep> ();
		var transport = new Transport ();
		Task<SubmissionDeliveryAuthorization> Check (SubmissionDeliveryStep step, CancellationToken _)
			{
			steps.Add (step);
			return Task.FromResult (new SubmissionDeliveryAuthorization (SubmissionDelivery.PlanDigest (_settings.Plan), DateTimeOffset.UtcNow.AddMinutes (1)));
			}
		await SubmissionDispatchCommand.DispatchAsync (_settings, transport, Check, default);
		await SubmissionDispatchCommand.DispatchAsync (_settings, transport, (_, _) => throw new Exception ("No second approval or delivery"), default);
		Assert.That (steps, Is.EqualTo (new[] { SubmissionDeliveryStep.Upload, SubmissionDeliveryStep.Send }));
		Assert.That ((transport.Uploads, transport.Sends), Is.EqualTo ((1, 1)));
		}
	[TestCase (SubmissionDeliveryStep.Upload, 0)]
	[TestCase (SubmissionDeliveryStep.Send, 1)]
	public void RefusedRevalidationStopsThatExternalStep (SubmissionDeliveryStep denied, int uploads)
		{
		var transport = new Transport ();
		Assert.ThrowsAsync<InvalidOperationException> (() => SubmissionDispatchCommand.DispatchAsync (_settings, transport,
			(step, _) => step == denied ? throw new InvalidOperationException ("Approval refused") :
				Task.FromResult (new SubmissionDeliveryAuthorization (SubmissionDelivery.PlanDigest (_settings.Plan), DateTimeOffset.UtcNow.AddMinutes (1))), default));
		Assert.That ((transport.Uploads, transport.Sends), Is.EqualTo ((uploads, 0)));
		}
	private sealed class Transport : ISubmissionDeliveryTransport
		{
		public int Uploads, Sends;
		public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken token)
			{
			Uploads++;
			return Task.FromResult (new SubmissionUploadReceipt ("https://example.test/package", "synthetic-upload"));
			}
		public Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload, Stream form, string messageId, CancellationToken token)
			{
			Sends++;
			return Task.FromResult (new SubmissionMailReceipt ("synthetic-mail"));
			}
		}
	}
