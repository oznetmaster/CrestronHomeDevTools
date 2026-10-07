// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionRehearsalTransportTests
	{
	private string _root = null!;
	private string _destination = null!;
	private string _journal = null!;
	private string _package = null!;
	private string _form = null!;
	private SubmissionDeliveryPlan _plan = null!;
	private readonly List<SubmissionDeliveryStep> _authorizations = [];
	private static string Hash (byte[] value) => Convert.ToHexStringLower (SHA256.HashData (value));

	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "rehearsal-" + Guid.NewGuid ().ToString ("N"));
		_destination = Path.Combine (_root, "mock-provider");
		_journal = Path.Combine (_root, "journal");
		Directory.CreateDirectory (_destination);
		Directory.CreateDirectory (_journal);
		_package = Path.Combine (_root, "Example_Platform_Test_IP.pkg");
		_form = Path.Combine (_root, "self-test.pdf");
		File.WriteAllText (_package, "Synthetic package, not driver acceptance evidence");
		File.WriteAllText (_form, "Synthetic PDF, not a real signature");
		_plan = new (new ('a', 64), new ('b', 64), new ('c', 64), Hash (File.ReadAllBytes (_package)), Hash (File.ReadAllBytes (_form)),
			Path.GetFileName (_package), Path.GetFileName (_form), "developer@example.test", "rehearsal@example.test") { Environment = SubmissionDeliveryEnvironment.Rehearsal };
		_authorizations.Clear ();
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);
	private Task<SubmissionDeliveryAuthorization> Authorize (SubmissionDeliveryStep step, CancellationToken token)
		{
		token.ThrowIfCancellationRequested ();
		_authorizations.Add (step);
		return Task.FromResult (new SubmissionDeliveryAuthorization (SubmissionDelivery.PlanDigest (_plan), DateTimeOffset.UtcNow.AddMinutes (1)));
		}
	private Task<SubmissionDeliveryReceipt> Execute (Func<SubmissionDeliveryStep, CancellationToken, Task<SubmissionDeliveryAuthorization>>? authorize = null) =>
		SubmissionDelivery.ExecuteAuthorizedAsync (_journal, _plan, _package, _form,
			new SubmissionRehearsalTransport (_destination, _plan), authorize ?? Authorize);

	[Test]
	public async Task SharedCoordinatorRetainsExactFilesAndExplicitRehearsalReceipts ()
		{
		var receipt = await Execute ();
		Assert.That (receipt.State, Is.EqualTo (SubmissionDeliveryState.Submitted));
		Assert.That (receipt.Environment, Is.EqualTo (SubmissionDeliveryEnvironment.Rehearsal));
		Assert.That (_authorizations, Is.EqualTo (new[] { SubmissionDeliveryStep.Upload, SubmissionDeliveryStep.Send }));
		Assert.That (new Uri (receipt.Upload!.DownloadUrl).Host, Is.EqualTo ("rehearsal.invalid"));
		Assert.That (receipt.Mail!.ProviderReceipt, Does.StartWith ("rehearsal-local-mail:"));
		Assert.That (File.ReadAllBytes (Path.Combine (_destination, "package.pkg")), Is.EqualTo (File.ReadAllBytes (_package)));
		Assert.That (File.ReadAllBytes (Path.Combine (_destination, "signed-form.pdf")), Is.EqualTo (File.ReadAllBytes (_form)));
		foreach (string name in new[] { "rehearsal-destination.json", "upload-receipt.json", "mail-receipt.json" })
			{
			using var document = JsonDocument.Parse (File.ReadAllBytes (Path.Combine (_destination, name)));
			Assert.That (document.RootElement.GetProperty ("mode").GetString (), Is.EqualTo ("Rehearsal"));
			Assert.That (document.RootElement.GetProperty ("externalDeliveryAttempted").GetBoolean (), Is.False);
			}
		}

	[Test]
	public async Task CompletedJournalReplaysWithoutTouchingProviderOrSourceFiles ()
		{
		var first = await Execute ();
		var retained = Directory.GetFiles (_destination).ToDictionary (path => Path.GetFileName (path)!, path => Hash (File.ReadAllBytes (path)));
		File.Delete (_package);
		File.Delete (_form);
		var again = await Execute ();
		Assert.That (again, Is.EqualTo (first));
		Assert.That (_authorizations.Count, Is.EqualTo (2));
		foreach (var file in retained)
			Assert.That (Hash (File.ReadAllBytes (Path.Combine (_destination, file.Key!))), Is.EqualTo (file.Value));
		}

	[Test]
	public async Task RefusedSendKeepsUploadAndResumesOnlyTheUnperformedStep ()
		{
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await Execute ((step, token) =>
			step == SubmissionDeliveryStep.Send ? throw new InvalidOperationException ("Synthetic authorization refusal") : Authorize (step, token)));
		Assert.That (SubmissionDelivery.Read (_journal, _plan, SubmissionDeliveryEnvironment.Rehearsal)!.State, Is.EqualTo (SubmissionDeliveryState.Uploaded));
		Assert.That (File.Exists (Path.Combine (_destination, "mail-receipt.json")), Is.False);
		Assert.That ((await Execute ()).State, Is.EqualTo (SubmissionDeliveryState.Submitted));
		Assert.That (_authorizations, Is.EqualTo (new[] { SubmissionDeliveryStep.Upload, SubmissionDeliveryStep.Send }));
		}

	[Test]
	public async Task ChangedLocalUploadStopsSendAndDoesNotReplayAnUnknownOperation ()
		{
		await Assert.ThrowsAsync<InvalidDataException> (async () => await Execute ((step, token) =>
			{
			if (step == SubmissionDeliveryStep.Send)
				File.AppendAllText (Path.Combine (_destination, "package.pkg"), " changed");
			return Authorize (step, token);
			}));
		Assert.That (SubmissionDelivery.Read (_journal, _plan, SubmissionDeliveryEnvironment.Rehearsal)!.State, Is.EqualTo (SubmissionDeliveryState.OutcomeUnknown));
		Assert.That (File.Exists (Path.Combine (_destination, "mail-receipt.json")), Is.False);
		await Assert.ThrowsAsync<InvalidOperationException> (async () => await Execute ());
		}

	[Test]
	public void DestinationCannotBeReboundToAnotherDriverOrEnvironment ()
		{
		_ = new SubmissionRehearsalTransport (_destination, _plan);
		Assert.Throws<InvalidDataException> (() => new SubmissionRehearsalTransport (_destination, _plan with { PackageSha256 = new ('d', 64) }));
		string marker = Path.Combine (_destination, "rehearsal-destination.json");
		File.WriteAllText (marker, File.ReadAllText (marker).Replace ("Rehearsal", "Production", StringComparison.Ordinal));
		Assert.Throws<InvalidDataException> (() => new SubmissionRehearsalTransport (_destination, _plan));
		}

	[Test]
	public async Task DirectTransportRejectsWrongBytesAndAnUnrelatedUpload ()
		{
		var transport = new SubmissionRehearsalTransport (_destination, _plan);
		using var wrong = new MemoryStream ([1, 2, 3]);
		await Assert.ThrowsAsync<InvalidDataException> (async () => await transport.UploadAsync (wrong, _plan.PackageFileName, default));
		Assert.That (File.Exists (Path.Combine (_destination, "package.pkg")), Is.False);
		using var form = File.OpenRead (_form);
		await Assert.ThrowsAsync<InvalidDataException> (async () => await transport.SendAsync (_plan,
			new ("https://example.test/upload", "unrelated"), form, "wrong", default));
		Assert.That (File.Exists (Path.Combine (_destination, "mail-receipt.json")), Is.False);
		}

	[Test]
	public async Task CompletedLocalMockCannotBePromotedToEmailDelivery ()
		{
		await Execute ();
		var email = _plan with { SendRehearsalEmail = true };
		Assert.Throws<InvalidDataException> (() => SubmissionDelivery.Read (_journal, email, SubmissionDeliveryEnvironment.Rehearsal));
		Assert.Throws<InvalidDataException> (() => new SubmissionRehearsalTransport (_destination, email));
		}

	[Test]
	public void ExistingUnboundFilesCannotBeAdoptedAsARehearsalDestination ()
		{
		File.WriteAllText (Path.Combine (_destination, "existing.txt"), "Preserve this file");
		Assert.Throws<InvalidDataException> (() => new SubmissionRehearsalTransport (_destination, _plan));
		Assert.That (File.ReadAllText (Path.Combine (_destination, "existing.txt")), Is.EqualTo ("Preserve this file"));
		}

	private sealed class UnexpectedProductionTransport : ISubmissionDeliveryTransport
		{
		public int Calls { get; private set; }
		public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken token)
			{ Calls++; throw new InvalidOperationException ("Production transport must not be entered."); }
		public Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt upload, Stream form, string messageId, CancellationToken token)
			{ Calls++; throw new InvalidOperationException ("Production transport must not be entered."); }
		}

	[Test]
	public async Task CompletedRehearsalCannotBeAcceptedAsProductionDelivery ()
		{
		await Execute ();
		var production = new UnexpectedProductionTransport ();
		await Assert.ThrowsAsync<InvalidDataException> (async () => await SubmissionDelivery.ExecuteAuthorizedAsync (
			_journal, _plan, _package, _form, production, Authorize));
		Assert.That (production.Calls, Is.Zero);
		Assert.Throws<InvalidDataException> (() => SubmissionDelivery.Read (_journal, _plan));
		Assert.That (SubmissionDelivery.Read (_journal, _plan, SubmissionDeliveryEnvironment.Rehearsal)!.State,
			Is.EqualTo (SubmissionDeliveryState.Submitted));
		}

	[Test]
	public async Task ProductionJournalCannotBeAdoptedByRehearsal ()
		{
		var production = new UnexpectedProductionTransport ();
		var productionPlan = _plan with { Environment = SubmissionDeliveryEnvironment.Production };
		await Assert.ThrowsAsync<OperationCanceledException> (async () => await SubmissionDelivery.ExecuteAuthorizedAsync (
			_journal, productionPlan, _package, _form, production, (_, _) => throw new OperationCanceledException ()));
		Assert.That (SubmissionDelivery.Read (_journal, productionPlan)!.Environment, Is.EqualTo (SubmissionDeliveryEnvironment.Production));
		await Assert.ThrowsAsync<InvalidDataException> (async () => await Execute ());
		Assert.That (production.Calls, Is.Zero);
		Assert.That (File.Exists (Path.Combine (_destination, "package.pkg")), Is.False);
		}
	}