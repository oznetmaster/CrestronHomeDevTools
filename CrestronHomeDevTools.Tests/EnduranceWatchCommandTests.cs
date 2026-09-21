// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

using MimeKit;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class EnduranceWatchCommandTests
	{
	private string _root = null!;
	private string[] _args = null!;
	private SubmissionEndurancePlan _plan = null!;
	private string _identity = null!;
	private const string Input = "{\"smtp\":{\"userName\":\"synthetic\",\"password\":\"PRIVATE-PASSWORD\"}}";
	[SetUp]
	public void SetUp ()
		{
		Directory.CreateDirectory (_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "watch-cli-" + Guid.NewGuid ().ToString ("N")));
		Directory.CreateDirectory (Path.Combine (_root, "journal"));
		var probe = new SubmissionEnduranceProbeProgram (_root, "never-executed.exe", [new ("never-executed.exe", new ('e', 64))]);
		_plan = new (new (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64)),
			new ("periodic", TimeSpan.FromMinutes (2), Execution: new ("gateway", "endurance", SubmissionEvidenceOutcome.Passed, null, false, 120)),
			"processor", "instance", "reservation", SubmissionEnduranceProcessProbe.GetProducerId (probe), TimeSpan.FromSeconds (30), TimeSpan.FromSeconds (30));
		_identity = SubmissionEndurance.PlanDigest (_plan);
		Write ("worker", new SubmissionEnduranceWorkerPlan (_plan, new ("processor.invalid", "pin"), probe));
		Write ("observer", new EnduranceObservationCommand.Settings (new ("Synthetic task", Path.Combine (_root, "state")), null));
		Write ("notifications", new SubmissionEnduranceNotificationSettings (_identity, "Synthetic monitor", "smtp.example.test", 587, "from@example.test", "to@example.test"));
		_args = ["--worker", Path.Combine (_root, "worker.json"), "--observer", Path.Combine (_root, "observer.json"),
			"--notifications", Path.Combine (_root, "notifications.json"), "--journal", Path.Combine (_root, "journal"), "--send", "true"];
		}
	private void Write (string name, object value) => File.WriteAllText (Path.Combine (_root, name + ".json"), JsonSerializer.Serialize (value));

	[TestCase ("smtp.example.test", "from@example.test", 0)]
	[TestCase ("different.example.test", "from@example.test", 3)]
	[TestCase ("smtp.example.test", "different@example.test", 3)]
	[Platform ("Win")]
	[System.Runtime.Versioning.SupportedOSPlatform ("windows")]
	public async Task SavedCredentialsAreBoundToEndpointAndSenderBeforeAnyObservation (string host, string sender, int expected)
		{
		var store = DevToolsPrivateStore.Create (Path.Combine (_root, "store"));
		store.SaveCredential ("mail", new (DevToolsCredentialPurpose.Smtp, host, "synthetic", "PRIVATE-PASSWORD", 587, sender));
		Write ("bindings", new DevToolsCredentialBindings (store.DirectoryPath, Smtp: "mail"));
		int observations = 0;
		var output = new StringWriter ();
		var error = new StringWriter ();
		int code = await EnduranceWatchCommand.RunAsync ([.. _args, "--credentials", Path.Combine (_root, "bindings.json")],
			new StringReader ("INVALID-STDIN-MUST-NOT-BE-USED"), output, error, CancellationToken.None,
			(plan, settings, credential, token) => { observations++; return Task.FromResult (Report (SubmissionEnduranceHealthState.Collecting)); },
			(s, c, d) => { Assert.That (c.Password, Is.EqualTo ("PRIVATE-PASSWORD")); return new (s, c, d, () => new Session ()); });
		Assert.That (code, Is.EqualTo (expected), error.ToString ());
		Assert.That (observations, Is.EqualTo (expected == 0 ? 1 : 0));
		Assert.That (output.ToString () + error, Does.Not.Contain ("PRIVATE-PASSWORD"));
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, true);

	[TestCase (SubmissionEnduranceHealthState.Collecting, 0, 0)]
	[TestCase (SubmissionEnduranceHealthState.AttentionRequired, 3, 1)]
	[TestCase (SubmissionEnduranceHealthState.Completed, 0, 1)]
	public async Task EachInvocationObservesFreshlyAndPreservesNotificationSuppression (SubmissionEnduranceHealthState state, int exitCode, int sends)
		{
		int observations = 0;
		var smtp = new Session ();
		var output = new StringWriter ();
		var error = new StringWriter ();
		for (int i = 0; i < 2; i++)
			{
			int result = await EnduranceWatchCommand.RunAsync (_args, new StringReader (Input), output, error, CancellationToken.None,
				(plan, settings, credential, token) =>
					{
						observations++;
						Assert.That (credential, Is.Null);
						Assert.That (SubmissionEndurance.PlanDigest (plan), Is.EqualTo (_identity));
						return Task.FromResult (Report (state));
					}, (s, c, d) => new (s, c, d, () => smtp));
			Assert.That (result, Is.EqualTo (exitCode), error.ToString ());
			}
		Assert.That (observations, Is.EqualTo (2));
		Assert.That (smtp.Sends, Is.EqualTo (sends));
		Assert.That (smtp.Connections, Is.EqualTo (sends));
		Assert.That (output.ToString (), Does.Contain ("Health").And.Contain ("Notification").And.Not.Contain ("PRIVATE-PASSWORD"));
		Assert.That (error.ToString (), Is.Empty);
		}

	[Test]
	public async Task FailedRemoteQueryStillSendsAttentionAndSeparatesCredentials ()
		{
		Write ("observer", new EnduranceObservationCommand.Settings (new ("Synthetic task", Path.Combine (_root, "state")),
			new ("windows.example.test", 22, "ssh-ed25519", "pinned")));
		var smtp = new Session ();
		var output = new StringWriter ();
		var error = new StringWriter ();
		string input = Input.Insert (1, "\"windows\":{\"userName\":\"windows-user\",\"password\":\"WINDOWS-SECRET\"},");
		int result = await EnduranceWatchCommand.RunAsync (_args, new StringReader (input), output, error, CancellationToken.None,
			(plan, settings, credential, token) =>
				{
					Assert.That (credential!.UserName, Is.EqualTo ("windows-user"));
					Assert.That (credential.Password, Is.EqualTo ("WINDOWS-SECRET"));
					return SubmissionEnduranceWindowsObserver.AssessCoreAsync (plan, _ => throw new IOException ("PRIVATE-QUERY-DETAILS"), token);
				}, (s, c, d) =>
				{
					Assert.That (c.UserName, Is.EqualTo ("synthetic"));
					Assert.That (c.Password, Is.EqualTo ("PRIVATE-PASSWORD"));
					return new (s, c, d, () => smtp);
				});
		Assert.That (result, Is.EqualTo (3));
		Assert.That (smtp.Sends, Is.EqualTo (1));
		Assert.That (output.ToString (), Does.Contain ("observer-query-failed").And.Contain ("Accepted")
			.And.Not.Contain ("WINDOWS-SECRET").And.Not.Contain ("PRIVATE-PASSWORD").And.Not.Contain ("PRIVATE-QUERY-DETAILS"));
		}

	[Test]
	public async Task UncertainDeliveryRetainsHealthAndNeverResendsOnNextInvocation ()
		{
		var smtp = new Session { FailSend = true };
		for (int i = 0; i < 2; i++)
			{
			var output = new StringWriter ();
			int result = await EnduranceWatchCommand.RunAsync (_args, new StringReader (Input), output, new StringWriter (), CancellationToken.None,
				(p, s, c, t) => Task.FromResult (Report (SubmissionEnduranceHealthState.AttentionRequired)),
				(s, c, d) => new (s, c, d, () => smtp));
			Assert.That (result, Is.EqualTo (3));
			Assert.That (output.ToString (), Does.Contain ("AttentionRequired").And.Contain ("Uncertain").And.Not.Contain ("PRIVATE-SMTP-DETAILS"));
			}
		Assert.That (smtp.Sends, Is.EqualTo (1));
		}

	[TestCase ("missing-send")]
	[TestCase ("duplicate")]
	[TestCase ("malformed")]
	[TestCase ("oversized")]
	[TestCase ("missing-smtp")]
	[TestCase ("unneeded-windows")]
	[TestCase ("missing-windows")]
	public async Task InvalidInputsNeverObserveOrSend (string scenario)
		{
		if (scenario == "missing-windows")
			Write ("observer", new EnduranceObservationCommand.Settings (new ("Synthetic task", Path.Combine (_root, "state")),
				new ("windows.example.test", 22, "ssh-ed25519", "pinned")));
		string[] args = scenario switch
			{
				"missing-send" => _args[..^2],
				"duplicate" => [.. _args, "--send", "true"],
				_ => _args
				};
		string input = scenario switch
			{
				"malformed" => "{PRIVATE-PASSWORD",
				"oversized" => new string ('x', 16385),
				"missing-smtp" => "{}",
				"unneeded-windows" => Input.Insert (1, "\"windows\":{\"userName\":\"unused\",\"password\":\"unused\"},"),
				_ => Input
				};
		int observations = 0, factories = 0;
		var output = new StringWriter ();
		var error = new StringWriter ();
		int result = await EnduranceWatchCommand.RunAsync (args, new StringReader (input), output, error, CancellationToken.None,
			(p, s, c, t) => { observations++; throw new InvalidOperationException (); },
			(s, c, d) => { factories++; throw new InvalidOperationException (); });
		Assert.That (result, Is.EqualTo (2));
		Assert.That (observations + factories, Is.Zero);
		Assert.That (output.ToString () + error, Does.Not.Contain ("PRIVATE-PASSWORD"));
		}

	[Test]
	public async Task WrongRunSubscriptionCannotSendAndRetainsTheObservedIdentity ()
		{
		Write ("notifications", new SubmissionEnduranceNotificationSettings (new ('f', 64), "Wrong run", "smtp.example.test", 587, "from@example.test", "to@example.test"));
		var smtp = new Session ();
		var output = new StringWriter ();
		int result = await EnduranceWatchCommand.RunAsync (_args, new StringReader (Input), output, new StringWriter (), CancellationToken.None,
			(p, s, c, t) => Task.FromResult (Report (SubmissionEnduranceHealthState.AttentionRequired)),
			(s, c, d) => new (s, c, d, () => smtp));
		Assert.That (result, Is.EqualTo (2));
		Assert.That (smtp.Connections, Is.Zero);
		Assert.That (output.ToString (), Does.Contain (_identity).And.Contain ("invalid-input"));
		}

	[Test]
	public async Task CancellationDoesNotObserveOrSend ()
		{
		using var cancelled = new CancellationTokenSource ();
		cancelled.Cancel ();
		int calls = 0;
		int result = await EnduranceWatchCommand.RunAsync (_args, new StringReader (Input), new StringWriter (), new StringWriter (), cancelled.Token,
			(p, s, c, t) => { calls++; throw new InvalidOperationException (); },
			(s, c, d) => { calls++; throw new InvalidOperationException (); });
		Assert.That (result, Is.EqualTo (3));
		Assert.That (calls, Is.Zero);
		}

	[Test]
	public async Task JournalFailurePreservesTheFreshHealthResult ()
		{
		int result;
		var output = new StringWriter ();
		var smtp = new Session ();
		result = await EnduranceWatchCommand.RunAsync (_args, new StringReader (Input), output, new StringWriter (), CancellationToken.None,
			(p, s, c, t) =>
				{
					Directory.Delete (Path.Combine (_root, "journal"));
					return Task.FromResult (Report (SubmissionEnduranceHealthState.AttentionRequired));
				}, (s, c, d) => new (s, c, d, () => smtp));
		Assert.That (result, Is.EqualTo (3));
		Assert.That (smtp.Sends, Is.Zero);
		Assert.That (output.ToString (), Does.Contain ("AttentionRequired").And.Contain ("inspection-required"));
		}

	private SubmissionEnduranceHealthReport Report (SubmissionEnduranceHealthState state) =>
		new (state, state == SubmissionEnduranceHealthState.AttentionRequired ? ["sample-too-old"] : [], DateTimeOffset.UtcNow, null, _identity);
	[TestCase (false)]
	[TestCase (true)]
	[Platform ("Win")]
	[System.Runtime.Versioning.SupportedOSPlatform ("windows")]
	public async Task StandaloneObserverUsesSavedWindowsCredentialsAndChecksHostTrust (bool wrongTrust)
		{
		var store = DevToolsPrivateStore.Create (Path.Combine (_root, "observer-store"));
		store.SaveCredential ("monitor", new (DevToolsCredentialPurpose.Windows, "windows.example.test", "synthetic", "PRIVATE-PASSWORD", 22,
			SshFingerprint: wrongTrust ? "changed" : "pinned"));
		Write ("bindings", new DevToolsCredentialBindings (store.DirectoryPath, Windows: "monitor"));
		Write ("observer", new EnduranceObservationCommand.Settings (new ("Synthetic task", Path.Combine (_root, "state")),
			new ("windows.example.test", 22, "ssh-ed25519", "pinned")));
		int observations = 0;
		using var output = new StringWriter ();
		using var error = new StringWriter ();
		int result = await EnduranceObservationCommand.RunAsync ([.. _args[..4], "--credentials", Path.Combine (_root, "bindings.json")],
			new StringReader ("invalid-stdin"), output, error, CancellationToken.None, (p, s, c, t) =>
				{
					observations++;
					Assert.That (c!.Password, Is.EqualTo ("PRIVATE-PASSWORD"));
					return Task.FromResult (Report (SubmissionEnduranceHealthState.Collecting));
				});
		Assert.That ((result, observations), Is.EqualTo (wrongTrust ? (2, 0) : (0, 1)));
		Assert.That (output.ToString () + error, Does.Not.Contain ("PRIVATE-PASSWORD"));
		}
	private sealed class Session : ISubmissionSmtpSession
		{
		internal int Connections, Sends;
		internal bool FailSend;
		public Task ConnectAsync (string host, int port, NetworkCredential credential, CancellationToken token)
			{
			Connections++;
			return Task.CompletedTask;
			}
		public Task<string> SendAsync (MimeMessage message, CancellationToken token)
			{
			Sends++;
			return FailSend ? Task.FromException<string> (new IOException ("PRIVATE-SMTP-DETAILS")) : Task.FromResult ("synthetic acceptance");
			}
		public void Dispose ()
			{
			}
		}
	}