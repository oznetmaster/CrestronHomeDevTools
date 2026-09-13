// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class RebootLifecycleTests
	{
	private static readonly DriverInfo Catalogue = new () { Id = "catalogue", Model = "Example", Version = "1.1" };
	private static DriverUpdateEligibility Eligibility (bool? reboot = true, params int[] ids) => new ()
		{
		InstalledDriverVersion = "1.0",
		AvailableDriverVersion = "1.1",
		IsSupportsSwapDriver = true,
		IsSwapDriverRequiresReboot = reboot,
		EligibleDeviceIds = ids.Length == 0 ? [17] : ids
		};
	private static DeviceInfo Device (string version, bool reboot = true) => new ()
		{
		Id = 17,
		Model = "Example",
		Name = "Example",
		LocationId = 12,
		Commands = ["cp.deviceConfiguration:setLocation"],
		PropertyValues = new ()
			{
			["cp.driverInformation:version"] = JsonSerializer.SerializeToElement (version),
			["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement ("Loaded"),
			["cp.driverConfiguration:supportsUnloadReloadDriver"] = JsonSerializer.SerializeToElement (!reboot),
			["cp.driverConfiguration:swapDriverRequiresReboot"] = JsonSerializer.SerializeToElement (reboot)
			}
		};
	private static Dictionary<string, DeviceInfo> Inventory (string version) => new () { ["17"] = Device (version) };
	private static Task<DriverInstanceReady> Ensure (Fake connection, DriverRebootHandler? handler = null) =>
		 DriverInstanceLifecycle.EnsureAsync (new (connection), "catalogue", "Example", 12, null, TimeSpan.FromSeconds (3), reboot: handler);

	[Test]
	public void RebootUpdateIsRefusedWithoutAuthorization ()
		{
		var original = new Fake (Catalogue, Inventory ("1.0"), Eligibility ());
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Ensure (original));
		Assert.That (original.Commands.Any (c => c.Contains ("beginSwap")), Is.False);
		}

	[Test]
	public async Task AuthorizedUpdateWaitsForConfirmedSwapBeforeReboot ()
		{
		var original = new Fake (Catalogue, Inventory ("1.0"), Eligibility (), Eligibility (), "op");
		var fresh = new Fake (Device ("1.1"), Device ("1.1"));
		var before = 0;
		var recovery = 0;
		var handler = new DriverRebootHandler ((request, _) =>
		{
			Assert.That (original.Commands.Any (c => c.Contains ("beginSwap")), Is.False);
			Assert.That (request.Mode, Is.EqualTo (DriverRebootMode.ExplicitAfterOperation));
			before++;
			return Task.CompletedTask;
		}, (request, _, _) =>
		{
			Assert.That (before, Is.EqualTo (1));
			Assert.That (original.SwapWaited, Is.True);
			recovery++;
			return Task.FromResult (new ConfigurationClient (fresh));
		});
		Assert.That ((await Ensure (original, handler)).Action, Is.EqualTo ("Updated"));
		Assert.That (recovery, Is.EqualTo (1));
		Assert.That (original.Commands.Count (c => c.Contains ("beginSwap")), Is.EqualTo (1));
		Assert.That (fresh.Commands, Is.Empty);
		}

	[Test]
	public void LostSubmissionResponseDoesNotInferRebootOrReplay ()
		{
		var original = new Fake (Catalogue, Inventory ("1.0"), Eligibility (), Eligibility (), new IOException ());
		Assert.ThrowsAsync<IOException> (async () => await Ensure (original, new ((_, _) => Task.CompletedTask, (_, _, _) => throw new AssertionException ("Unconfirmed update must not reboot"))));
		Assert.That (original.Commands.Count (c => c.Contains ("beginSwap")), Is.EqualTo (1));
		Assert.That (original.SwapWaited, Is.False);
		}

	[Test]
	public void MissingSwapCompletionDoesNotReboot ()
		{
		var original = new Fake (Catalogue, Inventory ("1.0"), Eligibility (), Eligibility (), "op") { SwapFailure = new TimeoutException () };
		Assert.ThrowsAsync<TimeoutException> (async () => await Ensure (original, new ((_, _) => Task.CompletedTask, (_, _, _) => throw new AssertionException ("Unconfirmed swap must not reboot"))));
		Assert.That (original.Commands.Count (c => c.Contains ("beginSwap")), Is.EqualTo (1));
		}

	[TestCase (false, false)]
	[TestCase (true, true)]
	public void UnconfirmedRebootOrRequiredReconfigurationStopsBeforeReboot (bool rebootRequired, bool reconfigure)
		{
		var original = new Fake (Catalogue, Inventory ("1.0"), Eligibility (), Eligibility (), "op") { RebootRequired = rebootRequired, ReconfigurationIds = reconfigure ? [17] : [] };
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Ensure (original, new ((_, _) => Task.CompletedTask, (_, _, _) => throw new AssertionException ("Unexpected reboot"))));
		Assert.That (original.Commands.Count (c => c.Contains ("beginSwap")), Is.EqualTo (1));
		}

	[Test]
	public void FailureToSaveAuthorizationPreventsUpdate ()
		{
		var original = new Fake (Catalogue, Inventory ("1.0"), Eligibility ());
		Assert.ThrowsAsync<IOException> (async () => await Ensure (original, new ((_, _) => throw new IOException (), (_, _, _) => throw new AssertionException ("Unexpected operation"))));
		Assert.That (original.Commands.Any (c => c.Contains ("beginSwap")), Is.False);
		}

	[Test]
	public void RebootUpdateCannotExpandScope ()
		{
		var original = new Fake (Catalogue, Inventory ("1.0"), Eligibility (true, 17, 18));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Ensure (original, new ((_, _) => throw new AssertionException ("Unexpected operation"), (_, _, _) => throw new AssertionException ("Unexpected operation"))));
		Assert.That (original.Commands.Any (c => c.Contains ("beginSwap")), Is.False);
		}

	[Test]
	public void ChangedRebootRequirementDuringRecheckPreventsSubmission ()
		{
		var original = new Fake (Eligibility (false));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await new ConfigurationClient (original).BeginDriverUpdateAsync (new ("catalogue", Eligibility ()), allowProcessorReboot: true));
		Assert.That (original.Commands.Any (c => c.Contains ("beginSwap")), Is.False);
		}

	[Test]
	public void UnknownRebootRequirementIsNotAuthorizedByGlobalOptIn ()
		{
		var original = new Fake ();
		Assert.ThrowsAsync<InvalidOperationException> (async () => await new ConfigurationClient (original).BeginDriverUpdateAsync (new ("catalogue", Eligibility (null)), allowProcessorReboot: true));
		Assert.That (original.Commands, Is.Empty);
		}

	[Test]
	public void WrongDriverAfterRestartFailsActivation ()
		{
		var original = new Fake (Catalogue, Inventory ("1.0"), Eligibility (), Eligibility (), "op");
		var fresh = new Fake (Device ("1.1"), Device ("1.1") with
			{
			Model = "Wrong driver"
			});
		Assert.ThrowsAsync<InvalidOperationException> (async () => await Ensure (original, new ((_, _) => Task.CompletedTask, (_, _, _) => Task.FromResult (new ConfigurationClient (fresh)))));
		}

	[Test]
	public void FailedReconnectionDoesNotRepeatUpdate ()
		{
		var original = new Fake (Catalogue, Inventory ("1.0"), Eligibility (), Eligibility (), "op");
		Assert.ThrowsAsync<TimeoutException> (async () => await Ensure (original, new ((_, _) => Task.CompletedTask, (_, _, _) => throw new TimeoutException ())));
		Assert.That (original.Commands.Count (c => c.Contains ("beginSwap")), Is.EqualTo (1));
		}

	[Test]
	public async Task ExplicitInstallPolicyRunsAfterSuccessfulCommissionOnly ()
		{
		var original = new Fake (Catalogue, new Dictionary<string, DeviceInfo> (), "Success", new
			{
			Id = 17,
			CommissioningResult = "Success"
			});
		var fresh = new Fake (Device ("1.1"), Device ("1.1"));
		var before = 0;
		var handler = new DriverRebootHandler ((_, _) => { before++; return Task.CompletedTask; }, (request, _, _) =>
		{
			Assert.That (request.DeviceId, Is.EqualTo (17));
			Assert.That (request.Mode, Is.EqualTo (DriverRebootMode.ExplicitAfterOperation));
			Assert.That (original.Commands.Count (c => c.EndsWith (":commissionDevice")), Is.EqualTo (1));
			return Task.FromResult (new ConfigurationClient (fresh));
		})
			{
			RebootAfterInstall = true
			};
		Assert.That ((await Ensure (original, handler)).Action, Is.EqualTo ("Installed"));
		Assert.That (before, Is.EqualTo (1));
		}

	[Test]
	public async Task ExplicitRemovalVerifiesDisappearanceOnFreshConnection ()
		{
		var original = new Fake (Device ("1.0"), new[] { 17 }, null);
		var fresh = new Fake (new Dictionary<string, DeviceInfo> ());
		var before = 0;
		await new ConfigurationClient (original).RemoveDriverInstanceAsync (17, "Example", "1.0", TimeSpan.FromSeconds (3), rebootHandler:
			 new ((request, _) => { before++; Assert.That (request.Operation, Is.EqualTo ("Remove")); return Task.CompletedTask; },
				  (_, _, _) => Task.FromResult (new ConfigurationClient (fresh)))
				 {
				 RebootAfterRemoval = true
				 });
		Assert.That (before, Is.EqualTo (1));
		Assert.That (original.Commands.Count (c => c.EndsWith (":setLocation")), Is.EqualTo (1));
		Assert.That (fresh.Reads, Is.EqualTo (1));
		}

	[Test]
	public void ExplicitRemovalCannotAffectOtherInstances ()
		{
		var original = new Fake (Device ("1.0"), new[] { 17, 18 });
		Assert.ThrowsAsync<InvalidOperationException> (async () => await new ConfigurationClient (original).RemoveDriverInstanceAsync (17, "Example", "1.0", TimeSpan.FromSeconds (3), rebootHandler:
			 new ((_, _) => throw new AssertionException ("Unexpected operation"), (_, _, _) => throw new AssertionException ("Unexpected operation"))
				 {
				 RebootAfterRemoval = true
				 }));
		Assert.That (original.Commands.Any (c => c.EndsWith (":setLocation")), Is.False);
		}

	[Test]
	public async Task RecoveryCannotReusePreRebootService ()
		{
		var original = new Fake ();
		var fresh = new Fake (new Dictionary<string, DeviceInfo> ());
		var connects = 0;
		var pending = ProcessorRestartRecovery.WaitAsync (new (original), _ => { connects++; return Task.FromResult (new ConfigurationClient (fresh)); }, TimeSpan.FromSeconds (3));
		Assert.That (connects, Is.Zero);
		original.Disconnected.SetResult ();
		await pending;
		Assert.That (connects, Is.EqualTo (1));
		Assert.That (fresh.Reads, Is.EqualTo (1));
		}

	[Test]
	public void MissingRestartTimesOutWithoutConnectingOrRebooting ()
		{
		var original = new Fake ();
		Assert.ThrowsAsync<TimeoutException> (async () => await ProcessorRestartRecovery.WaitAsync (new (original), _ => throw new AssertionException ("Unexpected operation"), TimeSpan.FromMilliseconds (50)));
		Assert.That (original.Commands, Is.Empty);
		}

	[Test]
	public async Task RecoveryRetriesOnlyReadOnlyConnectionsAndDisposesFailures ()
		{
		var original = new Fake ();
		original.Disconnected.SetResult ();
		var unavailable = new Fake (new IOException ());
		var available = new Fake (new Dictionary<string, DeviceInfo> ());
		var connects = 0;
		await ProcessorRestartRecovery.WaitAsync (new (original), _ => Task.FromResult (new ConfigurationClient (++connects == 1 ? unavailable : available, ownsConnection: true)), TimeSpan.FromSeconds (4));
		Assert.That (connects, Is.EqualTo (2));
		Assert.That (unavailable.Disposed, Is.True);
		Assert.That (available.Disposed, Is.False);
		Assert.That (unavailable.Commands.Concat (available.Commands), Is.Empty);
		}

	[TestCase (HttpStatusCode.InternalServerError)]
	[TestCase (HttpStatusCode.BadGateway)]
	[TestCase (HttpStatusCode.ServiceUnavailable)]
	[TestCase (HttpStatusCode.GatewayTimeout)]
	public async Task RestartRetriesTransientInventoryErrors (HttpStatusCode status)
		{
		var original = new Fake ();
		original.Disconnected.SetResult ();
		var unavailable = new Fake (new ProcessorApiException ("Starting", status));
		var available = new Fake (new Dictionary<string, DeviceInfo> ());
		var connects = 0;
		await ProcessorRestartRecovery.WaitAsync (new (original), _ => Task.FromResult (new ConfigurationClient (++connects == 1 ? unavailable : available, ownsConnection: true)), TimeSpan.FromSeconds (4));
		Assert.That (connects, Is.EqualTo (2));
		Assert.That (unavailable.Disposed, Is.True);
		Assert.That (unavailable.Commands.Concat (available.Commands), Is.Empty);
		}

	[TestCase (HttpStatusCode.Unauthorized)]
	[TestCase (HttpStatusCode.Forbidden)]
	[TestCase (HttpStatusCode.UnprocessableEntity)]
	public void RestartDoesNotRetryPermissionOrRequestErrors (HttpStatusCode status)
		{
		Assert.That (ProcessorRestartRecovery.IsReadinessFailure (new ProcessorApiException ("Rejected", status)), Is.False);
		}

	[Test]
	public void AuthenticationFailureStopsRecovery ()
		{
		var original = new Fake ();
		original.Disconnected.SetResult ();
		var connects = 0;
		Assert.ThrowsAsync<ProcessorApiException> (async () => await ProcessorRestartRecovery.WaitAsync (new (original), _ => { connects++; throw new ProcessorApiException ("Authentication failed"); }, TimeSpan.FromSeconds (1)));
		Assert.That (connects, Is.EqualTo (1));
		}

	[TestCase ("development", "development", false, true)]
	[TestCase ("DEVELOPMENT", "development", false, true)]
	[TestCase (null, null, true, false)]
	public void RebootCliAuthorization (string? target, string? confirmation, bool interactive, bool expected) =>
		 Assert.That (RebootConfirmation.ValidateCliAuthorization (target, confirmation, interactive), Is.EqualTo (expected));

	[TestCase (null, null)]
	[TestCase ("development", null)]
	[TestCase (null, "development")]
	[TestCase ("development", "another-home")]
	[TestCase ("development", "yes")]
	public void RebootCliRejectsMissingOrDifferentTarget (string? target, string? confirmation) =>
		 Assert.Throws<ArgumentException> (() => RebootConfirmation.ValidateCliAuthorization (target, confirmation, false));

	private sealed class Fake (params object?[] responses) : IConfigurationConnection
		{
		private readonly Queue<object?> _responses = new (responses);
		public List<string> Commands { get; } = [];
		public bool SwapWaited
			{
			get; private set;
			}
		public bool RebootRequired { get; init; } = true;
		public int[] ReconfigurationIds { get; init; } = [];
		public Exception? SwapFailure
			{
			get; init;
			}
		public Task<DriverSwapResult> WaitForDriverSwapAsync (string operationId, string driverId, TimeSpan timeout, CancellationToken token = default)
			{
			SwapWaited = true;
			if (SwapFailure != null)
				throw SwapFailure;
			return Task.FromResult (new DriverSwapResult (operationId, driverId, RebootRequired, ReconfigurationIds));
			}
		public int Reads
			{
			get; private set;
			}
		public bool Disposed
			{
			get; private set;
			}
		public TaskCompletionSource Disconnected { get; } = new (TaskCreationOptions.RunContinuationsAsynchronously);
		public Task WaitForDisconnectAsync (CancellationToken token = default) => Disconnected.Task.WaitAsync (token);
		private Task<T?> Next<T> ()
			{
			var item = _responses.Dequeue ();
			if (item is Exception exception)
				throw exception;
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (item)));
			}
		public Task<T?> GetAsync<T> (string path, CancellationToken token = default)
			{
			Reads++;
			return Next<T> ();
			}
		public Task<T?> ExecuteAsync<T> (int deviceId, string command, object? parameters = null, CancellationToken token = default)
			{
			Commands.Add (command);
			return Next<T> ();
			}
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken token = default) => throw new AssertionException ("Reboot path must recover instead of waiting for an old operation stream.");
		public ValueTask DisposeAsync ()
			{
			Disposed = true;
			return ValueTask.CompletedTask;
			}
		}
	}