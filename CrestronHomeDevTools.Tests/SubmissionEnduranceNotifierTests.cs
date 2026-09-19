// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using MimeKit;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionEnduranceNotifierTests
	{
	private string _root = null!;
	private readonly SubmissionEnduranceNotificationSettings _settings = new (new ('a', 64), "Synthetic candidate", "smtp.example.test", 587,
		"sender@example.test", "operator@example.test");
	[SetUp]
	public void SetUp () => Directory.CreateDirectory (_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "notify-" + Guid.NewGuid ().ToString ("N")));
	[TearDown]
	public void TearDown () => Directory.Delete (_root, true);
	private SubmissionEnduranceNotifier Notifier (Session session, SubmissionEnduranceNotificationSettings? settings = null) =>
		new (settings ?? _settings, new NetworkCredential ("synthetic-user", "PRIVATE-PASSWORD"), _root, () => session);
	private SubmissionEnduranceHealthReport Report (SubmissionEnduranceHealthState state = SubmissionEnduranceHealthState.AttentionRequired) =>
		new (state, state == SubmissionEnduranceHealthState.AttentionRequired ? ["worker-unreachable"] : [], DateTimeOffset.UtcNow, null, _settings.RunId);

	[Test]
	public async Task HealthyObservationsSendNothingAndIncidentIsDeduplicatedAcrossRestart ()
		{
		var first = new Session ();
		Assert.That ((await Notifier (first).NotifyAsync (Report (SubmissionEnduranceHealthState.Collecting))).State, Is.EqualTo ("Quiet"));
		Assert.That (first.Connects, Is.Zero);
		var sent = await Notifier (first).NotifyAsync (Report ());
		Assert.That (sent.State, Is.EqualTo ("Accepted"));
		var second = new Session ();
		var repeated = await Notifier (second).NotifyAsync (Report () with { Reasons = ["task-disabled"] });
		Assert.That (repeated.State, Is.EqualTo ("AlreadyAccepted"));
		Assert.That (repeated.MessageId, Is.EqualTo (sent.MessageId));
		Assert.That (second.Connects, Is.Zero);
		await Notifier (second).NotifyAsync (Report (SubmissionEnduranceHealthState.Collecting));
		var next = await Notifier (second).NotifyAsync (Report ());
		Assert.That (second.Sends, Is.EqualTo (1));
		Assert.That (next.MessageId, Is.Not.EqualTo (sent.MessageId));
		}

	[Test]
	public async Task CompletionIsSentOnceEvenAfterAnInterveningAttentionEpisode ()
		{
		var session = new Session ();
		var completion = await Notifier (session).NotifyAsync (Report (SubmissionEnduranceHealthState.Completed));
		await Notifier (session).NotifyAsync (Report ());
		await Notifier (session).NotifyAsync (Report (SubmissionEnduranceHealthState.Collecting));
		var again = await Notifier (session).NotifyAsync (Report (SubmissionEnduranceHealthState.Completed));
		Assert.That (session.Sends, Is.EqualTo (2));
		Assert.That (again.MessageId, Is.EqualTo (completion.MessageId));
		}

	[Test]
	public async Task ExplicitDestinationReceivesOnlySafeOperationalTextAndNoAttachments ()
		{
		var session = new Session { DisposeFails = true };
		var result = await Notifier (session).NotifyAsync (Report ());
		Assert.That (result.State, Is.EqualTo ("Accepted"));
		using var message = MimeMessage.Load (new MemoryStream (session.Bytes!));
		Assert.That (message.To.Mailboxes.Single ().Address, Is.EqualTo (_settings.Recipient));
		Assert.That (message.From.Mailboxes.Single ().Address, Is.EqualTo (_settings.Sender));
		Assert.That (message.Cc.Concat (message.Bcc), Is.Empty);
		Assert.That (message.Attachments, Is.Empty);
		Assert.That (message.TextBody, Does.Contain ("worker-unreachable").And.Not.Contain ("PRIVATE-PASSWORD"));
		Assert.That (message.Subject, Is.EqualTo ("Endurance monitoring needs attention"));
		Assert.That (message.MessageId, Is.EqualTo (result.MessageId));
		Assert.That (session.Disposed, Is.True);
		}

	[TestCase (false, "NotSent")]
	[TestCase (true, "Uncertain")]
	public async Task ConnectionAndSendFailuresAreRetainedAndNeverAutomaticallyRetried (bool duringSend, string state)
		{
		var broken = new Session { Failure = duringSend ? "send" : "connect" };
		var result = await Notifier (broken).NotifyAsync (Report ());
		Assert.That (result.State, Is.EqualTo (state));
		Assert.That (result.RequiresInspection, Is.True);
		var replay = new Session ();
		Assert.That ((await Notifier (replay).NotifyAsync (Report (SubmissionEnduranceHealthState.Collecting))).RequiresInspection, Is.True);
		Assert.That ((await Notifier (replay).NotifyAsync (Report ())).RequiresInspection, Is.True);
		Assert.That (replay.Connects, Is.Zero);
		foreach (string file in Directory.EnumerateFiles (_root, "*.json")) Assert.That (File.ReadAllText (file), Does.Not.Contain ("PRIVATE-PASSWORD"));
		}

	[TestCase (true)]
	[TestCase (false)]
	public async Task OnlyExplicitMatchingReconciliationPermitsResolution (bool accepted)
		{
		var first = await Notifier (new Session { Failure = "send" }).NotifyAsync (Report ());
		var session = new Session ();
		var notifier = Notifier (session);
		Assert.Throws<InvalidOperationException> (() => notifier.Reconcile ("wrong", accepted, "operator receipt"));
		notifier.Reconcile (first.MessageId!, accepted, "operator-checked-message-42");
		var next = await notifier.NotifyAsync (Report ());
		Assert.That (next.State, Is.EqualTo (accepted ? "AlreadyAccepted" : "Accepted"));
		Assert.That (session.Sends, Is.EqualTo (accepted ? 0 : 1));
		Assert.That (Directory.EnumerateFiles (_root, "review-*.json").Count (), Is.EqualTo (1));
		}

	[Test]
	public async Task ChangedRunOrDestinationAndDeletedCheckpointCannotRepeatAnAcceptedSend ()
		{
		await Notifier (new Session ()).NotifyAsync (Report ());
		var replay = new Session ();
		Assert.ThrowsAsync<InvalidDataException> (() => Notifier (replay, _settings with { Recipient = "someoneelse@example.test" }).NotifyAsync (Report ()));
		Assert.ThrowsAsync<ArgumentException> (() => Notifier (replay).NotifyAsync (Report () with { PlanSha256 = new ('b', 64) }));
		File.Delete (Path.Combine (_root, "notification.json"));
		Assert.ThrowsAsync<InvalidDataException> (() => Notifier (replay).NotifyAsync (Report ()));
		Assert.That (replay.Connects, Is.Zero);
		}

	[Test]
	public void LostAcceptanceWriteRequiresInspectionInsteadOfResending ()
		{
		var session = new Session { AfterSend = () =>
			{
			File.Delete (Path.Combine (_root, "notification.json"));
			Directory.CreateDirectory (Path.Combine (_root, "notification.json"));
			} };
		var error = Assert.CatchAsync<Exception> (() => Notifier (session).NotifyAsync (Report ()));
		Assert.That (error, Is.InstanceOf<IOException> ().Or.InstanceOf<UnauthorizedAccessException> ());
		Assert.That (session.Sends, Is.EqualTo (1));
		var replay = new Session ();
		Assert.ThrowsAsync<InvalidDataException> (() => Notifier (replay).NotifyAsync (Report ()));
		Assert.That (replay.Connects, Is.Zero);
		}

	[Test]
	public void ExclusiveObserverLockPreventsConcurrentSend ()
		{
		using var held = File.Open (Path.Combine (_root, "notification.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		var session = new Session ();
		Assert.ThrowsAsync<IOException> (() => Notifier (session).NotifyAsync (Report ()));
		Assert.That (session.Connects, Is.Zero);
		}

	[Test]
	public void MalformedOrStaleReportsAreRejectedBeforeConnecting ()
		{
		var session = new Session ();
		foreach (var report in new[] { Report () with { EvaluatedUtc = DateTimeOffset.UtcNow.AddMinutes (-6) },
			Report () with { EvaluatedUtc = DateTimeOffset.UtcNow.AddMinutes (1) }, Report () with { Reasons = ["raw\r\nPRIVATE-PASSWORD"] },
			Report (SubmissionEnduranceHealthState.Collecting) with { Reasons = ["task-disabled"] }, Report () with { Reasons = [] } })
			Assert.ThrowsAsync<ArgumentException> (() => Notifier (session).NotifyAsync (report));
		Assert.That (session.Connects, Is.Zero);
		}

	[Test]
	public async Task CompletionCanBeDisabledWithoutSuppressingAttention ()
		{
		var session = new Session ();
		var notifier = Notifier (session, _settings with { NotifyCompletion = false });
		Assert.That ((await notifier.NotifyAsync (Report (SubmissionEnduranceHealthState.Completed))).State, Is.EqualTo ("Quiet"));
		await notifier.NotifyAsync (Report ());
		Assert.That (session.Sends, Is.EqualTo (1));
		}

	private sealed class Session : ISubmissionSmtpSession
		{
		internal int Connects, Sends;
		internal bool DisposeFails, Disposed;
		internal string? Failure;
		internal byte[]? Bytes;
		internal Action? AfterSend;
		public Task ConnectAsync (string host, int port, NetworkCredential credential, CancellationToken token)
			{
			Connects++;
			if (Failure == "connect") throw new IOException ("PRIVATE-PASSWORD");
			return Task.CompletedTask;
			}
		public Task<string> SendAsync (MimeMessage message, CancellationToken token)
			{
			Sends++;
			if (Failure == "send") throw new IOException ("PRIVATE-PASSWORD");
			using var output = new MemoryStream ();
			message.WriteTo (output);
			Bytes = output.ToArray ();
			AfterSend?.Invoke ();
			return Task.FromResult ("Synthetic accepted");
			}
		public void Dispose () { Disposed = true; if (DisposeFails) throw new IOException ("Synthetic close failure"); }
		}
	}