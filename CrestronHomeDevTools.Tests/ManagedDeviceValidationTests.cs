// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ManagedDeviceValidationTests
	{
	private string _root = null!;
	private string Journal => Path.Combine (_root, "validation");
	private static ManagedDeviceTestTarget Target (string alias) => new (alias, new (17, "Platform", "1.0.0.1", alias, "CI " + alias, "Child", 3));
	[SetUp]
	public void SetUp () => Directory.CreateDirectory (_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "validation-" + Guid.NewGuid ().ToString ("N")));
	[TearDown]
	public void TearDown () => Directory.Delete (_root, true);

	[Test]
	public async Task TestsReceiveCreatedIdsBeforeReverseOrderCleanup ()
		{
		var fake = new Scenario (Journal);
		var result = await Run (fake);
		Assert.That (result.Passed && result.RestorationConfirmed && result.CleanupConfirmed, Is.True);
		Assert.That (fake.Events, Is.EqualTo (new[] { "create:first", "create:second", "tests:101,102", "remove:second", "remove:first" }));
		Assert.That (result.Bindings.Select (binding => binding.DeviceId), Is.EqualTo (new[] { 101, 102 }));
		Assert.That (fake.OwnershipChecks, Is.EqualTo (10));
		Assert.That (File.Exists (Path.Combine (Journal, "removed-first.json")), Is.True);
		}

	[Test]
	public async Task FailedTestWithConfirmedRestorationCleansUpButStillFails ()
		{
		var fake = new Scenario (Journal) { TestPassed = false };
		var result = await Run (fake);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed && result.CleanupConfirmed, Is.True);
		Assert.That (fake.Removals, Is.EqualTo (2));
		}

	[TestCase (true)]
	[TestCase (false)]
	public async Task UnconfirmedRestorationRetainsChildrenRegardlessOfTestOutcome (bool passed)
		{
		var fake = new Scenario (Journal) { TestPassed = passed, Restored = false };
		var result = await Run (fake);
		Assert.That (result.Passed || result.CleanupConfirmed || result.RestorationConfirmed, Is.False);
		Assert.That (fake.Removals, Is.Zero);
		Assert.That (result.Bindings.Count, Is.EqualTo (2));
		}

	[Test]
	public void TestExceptionDoesNotGuessRestorationOrRemoveChildren ()
		{
		var fake = new Scenario (Journal) { Fault = "test" };
		Assert.ThrowsAsync<IOException> (async () => await Run (fake));
		Assert.That (fake.Removals, Is.Zero);
		Assert.That (File.Exists (Path.Combine (Journal, "stopped.json")), Is.True);
		Assert.That (File.Exists (Path.Combine (Journal, "result.json")), Is.False);
		}

	[Test]
	public void LostOwnershipPreventsCleanupEvenAfterReportedRestoration ()
		{
		var fake = new Scenario (Journal) { Fault = "ownership" };
		Assert.ThrowsAsync<IOException> (async () => await Run (fake));
		Assert.That (fake.Removals, Is.Zero);
		}

	[Test]
	public async Task RequiredConfigurationNeverStartsTestsOrClaimsCleanup ()
		{
		var fake = new Scenario (Journal) { Fault = "configuration" };
		var result = await Run (fake);
		Assert.That (result.Passed || result.CleanupConfirmed, Is.False);
		Assert.That (fake.Events, Is.EqualTo (new[] { "create:first" }));
		Assert.That (result.Bindings.Single ().DeviceId, Is.EqualTo (101));
		}

	[Test]
	public void PartialCommissioningKeepsCreatedBindingAndDoesNotRunTests ()
		{
		var fake = new Scenario (Journal) { Fault = "second" };
		Assert.ThrowsAsync<IOException> (async () => await Run (fake));
		Assert.That (File.Exists (Path.Combine (Journal, "binding-first.json")), Is.True);
		Assert.That (fake.Events, Is.EqualTo (new[] { "create:first", "create:second" }));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Run (fake));
		Assert.That (fake.Created, Is.EqualTo (2));
		}

	[Test]
	public void DuplicateReturnedDeviceIdsAreNotGivenToTests ()
		{
		var fake = new Scenario (Journal) { Fault = "duplicate" };
		Assert.ThrowsAsync<InvalidDataException> (async () => await Run (fake));
		Assert.That (fake.Events, Is.EqualTo (new[] { "create:first", "create:second" }));
		}

	[Test]
	public void CleanupFailureDoesNotRemoveAnotherChildOrClaimSuccess ()
		{
		var fake = new Scenario (Journal) { Fault = "cleanup" };
		Assert.ThrowsAsync<IOException> (async () => await Run (fake));
		Assert.That (fake.Removals, Is.EqualTo (1));
		Assert.That (File.Exists (Path.Combine (Journal, "result.json")), Is.False);
		}

	[Test]
	public void CleanupIdentityMismatchIsNotAccepted ()
		{
		var fake = new Scenario (Journal) { Fault = "cleanup-id" };
		Assert.ThrowsAsync<InvalidDataException> (async () => await Run (fake));
		Assert.That (fake.Removals, Is.EqualTo (1));
		}

	[TestCase ("../escape")]
	[TestCase ("FIRST")]
	public void UnsafeOrDuplicateAliasesFailBeforeAnyOperation (string second)
		{
		var fake = new Scenario (Journal);
		Assert.ThrowsAsync<ArgumentException> (async () => await Run (fake, [Target ("first"), Target (second)]));
		Assert.That (fake.Events, Is.Empty);
		Assert.That (Directory.Exists (Journal), Is.False);
		}

	[Test]
	public void PreCancelledRunDoesNotCommissionChildren ()
		{
		var fake = new Scenario (Journal);
		using var cancelled = new CancellationTokenSource ();
		cancelled.Cancel ();
		Assert.ThrowsAsync<OperationCanceledException> (async () => await ManagedDeviceValidation.RunCoreAsync ([Target ("first")], Journal,
			fake.Verify, fake.Tests, fake.Commission, fake.Cleanup, cancelled.Token));
		Assert.That (fake.Events, Is.Empty);
		}

	private Task<ManagedDeviceValidationResult> Run (Scenario fake, ManagedDeviceTestTarget[]? targets = null) =>
		ManagedDeviceValidation.RunCoreAsync (targets ?? [Target ("first"), Target ("second")], Journal, fake.Verify, fake.Tests, fake.Commission, fake.Cleanup);

	private sealed class Scenario (string journal)
		{
		public List<string> Events { get; } = [];
		public string? Fault { get; init; }
		public bool TestPassed { get; init; } = true;
		public bool Restored { get; init; } = true;
		public int Created { get; private set; }
		public int Removals { get; private set; }
		public int OwnershipChecks { get; private set; }
		private bool _tested;
		public Task Verify (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			OwnershipChecks++;
			if (_tested && Fault == "ownership") throw new IOException ("Owner changed.");
			return Task.CompletedTask;
			}
		public Task<ManagedDeviceResult> Commission (ManagedDeviceRequest request, string directory, CancellationToken token)
			{
			Created++;
			string alias = Path.GetFileName (directory);
			Events.Add ("create:" + alias);
			if (Created == 2 && Fault == "second") throw new IOException ("Second setup uncertain.");
			return Task.FromResult (new ManagedDeviceResult (Fault == "duplicate" ? 101 : 100 + Created, Fault == "configuration" ? "ConfigurationRequired" : "Ready"));
			}
		public Task<ManagedDeviceTestOutcome> Tests (IReadOnlyList<ManagedDeviceTestBinding> bindings, CancellationToken token)
			{
			_tested = true;
			Assert.That (File.Exists (Path.Combine (journal, "bindings.json")), Is.True);
			Assert.That (File.Exists (Path.Combine (journal, "tests-intent.json")), Is.True);
			var recorded = JsonSerializer.Deserialize<ManagedDeviceTestBinding[]> (File.ReadAllText (Path.Combine (journal, "bindings.json")))!;
			Assert.That (recorded, Is.EqualTo (bindings));
			Events.Add ("tests:" + string.Join (",", bindings.Select (binding => binding.DeviceId)));
			if (Fault == "test") throw new IOException ("Producer disappeared.");
			return Task.FromResult (new ManagedDeviceTestOutcome (TestPassed, Restored));
			}
		public Task<ManagedDeviceCleanupResult> Cleanup (string directory, CancellationToken token)
			{
			Removals++;
			string alias = Path.GetFileName (directory);
			Events.Add ("remove:" + alias);
			if (Fault == "cleanup") throw new IOException ("Removal outcome unknown.");
			return Task.FromResult (new ManagedDeviceCleanupResult (Fault == "cleanup-id" ? 999 : alias == "first" ? 101 : 102, true, true));
			}
		}
	}