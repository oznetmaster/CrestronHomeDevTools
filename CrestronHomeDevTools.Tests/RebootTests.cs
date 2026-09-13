// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class RebootTests
	{
	private static readonly ProcessorRebootTarget Target = new ("192.0.2.1", "Example Home");

	[Test]
	public async Task DeclinedConfirmationSendsNothing ()
		{
		var session = new FakeSession ();
		var result = await Run (session, (_, _) => Task.FromResult (false));
		Assert.That (result.Status, Is.EqualTo (ProcessorRebootStatus.Cancelled));
		Assert.That (session.Commands, Is.Empty);
		}

	[Test]
	public async Task ConfirmationReceivesTargetAfterPromptAndSendsExactlyOnce ()
		{
		var session = new FakeSession { Response = ["Rebooting sys", "tem.  Please wait..."] };
		var result = await Run (session, (target, _) =>
		{
			Assert.That (target, Is.EqualTo (Target));
			Assert.That (session.PromptRead, Is.True);
			Assert.That (session.Commands, Is.Empty);
			return Task.FromResult (true);
		});
		Assert.That (result.Status, Is.EqualTo (ProcessorRebootStatus.Accepted));
		Assert.That (session.Commands, Is.EqualTo (new[] { "REBOOT" }));
		}

	[TestCase ("disconnect")]
	[TestCase ("write-failure")]
	[TestCase ("echo-only")]
	[TestCase ("cancel-after-write")]
	public async Task UncertainSubmissionIsNeverRetried (string failure)
		{
		using var cancellation = new CancellationTokenSource ();
		var session = new FakeSession { Failure = failure, AfterWrite = failure == "cancel-after-write" ? cancellation.Cancel : null };
		var result = await Run (session, (_, _) => Task.FromResult (true), cancellation.Token);
		Assert.That (result.Status, Is.EqualTo (ProcessorRebootStatus.Unconfirmed));
		Assert.That (session.Commands, Has.Count.EqualTo (1));
		}

	[Test]
	public void CancellationDuringConfirmationSendsNothing ()
		{
		using var cancellation = new CancellationTokenSource ();
		var session = new FakeSession ();
		Assert.ThrowsAsync<OperationCanceledException> (async () => await Run (session, (_, _) =>
		{
			cancellation.Cancel ();
			return Task.FromResult (true);
		}, cancellation.Token));
		Assert.That (session.Commands, Is.Empty);
		}

	[Test]
	public void ConfirmationFailureSendsNothing ()
		{
		var session = new FakeSession ();
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Run (session, (_, _) => throw new InvalidOperationException ()));
		Assert.That (session.Commands, Is.Empty);
		}

	[Test]
	public void MissingPromptDoesNotAskForConfirmationOrSend ()
		{
		var session = new FakeSession { NoPrompt = true };
		Assert.ThrowsAsync<TimeoutException> (async () => await Run (session, (_, _) => throw new AssertionException ("Unexpected confirmation")));
		Assert.That (session.Commands, Is.Empty);
		}

	[Test]
	public void MissingPinFailsBeforeConnection ()
		{
		Assert.ThrowsAsync<ArgumentException> (async () => await ProcessorReboot.RequestAsync (Target, new NetworkCredential (), "", (_, _) => Task.FromResult (true), TimeSpan.FromSeconds (1)));
		}

	[TestCase ("REBOOT 192.0.2.1", true)]
	[TestCase ("yes", false)]
	[TestCase ("REBOOT 192.0.2.2", false)]
	[TestCase ("", false)]
	[TestCase (null, false)]
	public async Task ConsoleRequiresExactTargetConfirmation (string? answer, bool expected)
		{
		using var input = new StringReader (answer == null ? "" : answer + "\n");
		using var output = new StringWriter ();
		Assert.That (await RebootConfirmation.ConfirmAsync (Target, input, output, CancellationToken.None), Is.EqualTo (expected));
		Assert.That (output.ToString (), Does.Contain ("Example Home (192.0.2.1)"));
		}

	private static Task<ProcessorRebootResult> Run (FakeSession session, Func<ProcessorRebootTarget, CancellationToken, Task<bool>> confirm, CancellationToken token = default) =>
		 ProcessorReboot.RequestCoreAsync (Target, session, confirm, TimeSpan.FromMilliseconds (250), token);

	private sealed class FakeSession : IRebootSession
		{
		public List<string> Commands { get; } = [];
		public string[] Response { get; init; } = ["Rebooting system. Please wait..."];
		public string? Failure
			{
			get; init;
			}
		public Action? AfterWrite
			{
			get; init;
			}
		public bool NoPrompt
			{
			get; init;
			}
		public bool PromptRead
			{
			get; private set;
			}
		private int index;
		public bool IsConnected => Commands.Count == 0 || Failure != "disconnect";
		public string ReadAvailable ()
			{
			if (Commands.Count == 0)
				{
				PromptRead = true;
				return NoPrompt ? "" : "MC4-R Control Console\r\nMC4-R>";
				}
			if (Failure != null)
				return "REBOOT";
			return index < Response.Length ? Response[index++] : "";
			}
		public void WriteLine (string command)
			{
			Commands.Add (command);
			AfterWrite?.Invoke ();
			if (Failure == "write-failure")
				throw new IOException ();
			}
		}
	}