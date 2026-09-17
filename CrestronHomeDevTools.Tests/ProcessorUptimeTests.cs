// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ProcessorUptimeTests
	{
	private const string Response = "The system has been running for 0 days 05:12:38.93\r\nThe system last started on: Thursday, September 17, 2026 at 11:20:20\r\n";

	[Test]
	public void UptimeWindowsRemainConsistentDespiteRoundedLocalStart ()
		{
		var sent = DateTimeOffset.Parse ("2026-09-17T15:00:00Z");
		Assert.That (ProcessorUptime.TryParse (Response, sent, sent.AddSeconds (1), out var first), Is.True);
		Assert.That (ProcessorUptime.TryParse (Response.Replace ("05:12:38.93", "05:13:38.93").Replace ("11:20:20", "11:20:19"), sent.AddMinutes (1), sent.AddMinutes (1).AddSeconds (1), out var second), Is.True);
		Assert.That (first!.LocalStartedAt, Is.Not.EqualTo (second!.LocalStartedAt));
		Assert.That (first.EarliestStartUtc, Is.EqualTo (second.EarliestStartUtc));
		Assert.That (first.LatestStartUtc, Is.EqualTo (second.LatestStartUtc));
		Assert.That (first.LocalStartedAt.Kind, Is.EqualTo (DateTimeKind.Unspecified));
		Assert.That (first.Uptime, Is.EqualTo (new TimeSpan (0, 5, 12, 38, 930)));
		}

	[Test]
	public void ResetUptimeMovesOutsideTheOriginalWindow ()
		{
		var sent = DateTimeOffset.Parse ("2026-09-17T15:00:00Z");
		ProcessorUptime.TryParse (Response, sent, sent.AddSeconds (1), out var first);
		ProcessorUptime.TryParse (Response.Replace ("05:12:38.93", "00:00:38.93"), sent.AddMinutes (1), sent.AddMinutes (1).AddSeconds (1), out var second);
		Assert.That (second!.EarliestStartUtc, Is.GreaterThan (first!.LatestStartUtc));
		}

	[Test]
	public void BackwardsObservationClockIsRejected ()
		{
		var sent = DateTimeOffset.UtcNow;
		Assert.Throws<InvalidDataException> (() => ProcessorUptime.TryParse (Response, sent, sent.AddSeconds (-1), out _));
		}
	[TestCase ("24:12:38.93")]
	[TestCase ("05:60:38.93")]
	[TestCase ("05:12:60.93")]
	[TestCase ("05:12:38.9")]
	public void MalformedDurationIsRejected (string replacement)
		=> Assert.Throws<InvalidDataException> (() => ProcessorUptime.TryParse (Response.Replace ("05:12:38.93", replacement), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, out _));

	[TestCase ("Friday, September 17, 2026")]
	[TestCase ("Thursday, September 31, 2026")]
	[TestCase ("17/09/2026")]
	public void InvalidOrAmbiguousCalendarIsRejected (string replacement)
		=> Assert.Throws<InvalidDataException> (() => ProcessorUptime.TryParse (Response.Replace ("Thursday, September 17, 2026", replacement), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, out _));

	[Test]
	public void DuplicateConsoleRepliesAreRejected ()
		=> Assert.Throws<InvalidDataException> (() => ProcessorUptime.TryParse (Response + Response, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, out _));

	[Test]
	public void PartialAndLogEmbeddedRepliesDoNotCount ()
		{
		Assert.That (ProcessorUptime.TryParse (Response.TrimEnd (), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, out _), Is.False);
		Assert.That (ProcessorUptime.TryParse ("[INFO] " + Response.Replace ("\r\n", "\r\n[INFO] "), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, out _), Is.False);
		}

	[TestCase ("CP4-R>\r\n")]
	[TestCase ("CP4-R>2026-09-17 16:44:52.149 [INFO] background log\r\n")]
	public async Task FragmentedReplyWithInterleavedLogsSendsExactlyOnce (string prompt)
		{
		var session = new Session { Prompt = prompt, Chunks = ["The system has been ", "running for 0 days 05:12:38.93\r\n", "[INFO] background event\r\n", "The system last started on: Thursday, September 17, 2026 at 11:20:20\r\n"] };
		var result = await ProcessorUptime.ReadCoreAsync (session, TimeSpan.FromSeconds (2), default);
		Assert.That (session.Commands, Is.EqualTo (new[] { "uptime" }));
		Assert.That (result.Uptime, Is.EqualTo (new TimeSpan (0, 5, 12, 38, 930)));
		}

	[Test]
	public void PreCommandRepliesAreDiscarded ()
		{
		var session = new Session { Prompt = Response + "CP4-R>\r\n", Chunks = [] };
		Assert.CatchAsync<OperationCanceledException> (() => ProcessorUptime.ReadCoreAsync (session, TimeSpan.FromMilliseconds (200), default));
		Assert.That (session.Commands, Has.Count.EqualTo (1));
		}

	[Test]
	public void MissingPromptSendsNothing ()
		{
		var session = new Session { Prompt = "[INFO] no prompt\r\n" };
		Assert.CatchAsync<OperationCanceledException> (() => ProcessorUptime.ReadCoreAsync (session, TimeSpan.FromMilliseconds (200), default));
		Assert.That (session.Commands, Is.Empty);
		}

	[TestCase ("write")]
	[TestCase ("disconnect")]
	[TestCase ("oversize")]
	public void FailedRequestIsNeverRepeated (string failure)
		{
		var session = new Session { Failure = failure, Chunks = [new string ('x', 65537)] };
		Assert.CatchAsync<Exception> (() => ProcessorUptime.ReadCoreAsync (session, TimeSpan.FromSeconds (1), default));
		Assert.That (session.Commands, Has.Count.EqualTo (1));
		}

	private sealed class Session : IUptimeSession
		{
		public string Prompt { get; init; } = "CP4-R>\r\n";
		public string[] Chunks { get; set; } = [Response];
		public string? Failure { get; init; }
		public List<string> Commands { get; } = [];
		private bool _promptRead;
		private int _index;
		public bool IsConnected => Failure != "disconnect" || Commands.Count == 0;
		public string ReadAvailable ()
			{
			if (!_promptRead) { _promptRead = true; return Prompt; }
			return Commands.Count > 0 && _index < Chunks.Length ? Chunks[_index++] : "";
			}
		public void WriteLine (string command)
			{
			Commands.Add (command);
			if (Failure == "write") throw new IOException ("Synthetic failed write.");
			}
		}
	}