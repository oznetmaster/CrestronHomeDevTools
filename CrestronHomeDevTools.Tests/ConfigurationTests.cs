// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ConfigurationTests
	{
	private static DriverUpdateEligibility Eligible (params int[] ids) => new ()
		{
		InstalledDriverVersion = "1.0",
		AvailableDriverVersion = "1.1",
		IsSupportsSwapDriver = true,
		IsSwapDriverRequiresReboot = false,
		EligibleDeviceIds = ids
		};

	[Test]
	public async Task Update_RechecksAndAppliesOnlyReviewedDriver ()
		{
		var connection = new FakeConnection (Eligible (17, 18), "operation-1");
		await using var client = new ConfigurationClient (connection);
		var operation = await client.BeginDriverUpdateAsync (new ("driver-1", Eligible (18, 17)));
		Assert.That (operation, Is.EqualTo ("operation-1"));
		Assert.That (connection.Calls.Select (x => x.Command), Is.EqualTo (new[]
		{ "cp.platformDriverController:getDevicesEligibleForDriverUpdate", "cp.platformDriverController:beginSwapDriverForAllEligibleDevices" }));
		Assert.That (connection.Calls.Last ().Parameters.GetProperty ("driverId").GetString (), Is.EqualTo ("driver-1"));
		Assert.That (connection.Calls.All (x => x.Id == -6), Is.True);
		}

	[TestCase ("reboot")]
	[TestCase ("unknownReboot")]
	[TestCase ("unsupported")]
	[TestCase ("unknownSupport")]
	[TestCase ("noDevices")]
	[TestCase ("missingDevices")]
	[TestCase ("missingVersion")]
	public void Update_RejectsUnconfirmedPlanBeforeAnyRequest (string scenario)
		{
		var eligibility = scenario switch
			{
				"reboot" => Eligible (17) with { IsSwapDriverRequiresReboot = true },
				"unknownReboot" => Eligible (17) with { IsSwapDriverRequiresReboot = null },
				"unsupported" => Eligible (17) with { IsSupportsSwapDriver = false },
				"unknownSupport" => Eligible (17) with { IsSupportsSwapDriver = null },
				"noDevices" => Eligible (),
				"missingVersion" => Eligible (17) with { AvailableDriverVersion = null },
				_ => Eligible (17) with { EligibleDeviceIds = null }
				};
		var connection = new FakeConnection ();
		var client = new ConfigurationClient (connection);
		Assert.ThrowsAsync<InvalidOperationException> (async () => await client.BeginDriverUpdateAsync (new ("driver", eligibility)));
		Assert.That (connection.Calls, Is.Empty);
		}

	[TestCase ("extraDevice")]
	[TestCase ("removedDevice")]
	[TestCase ("installedVersion")]
	[TestCase ("availableVersion")]
	[TestCase ("reboot")]
	public void Update_RejectsChangedEligibilityWithoutSubmitting (string change)
		{
		var current = change switch
			{
				"extraDevice" => Eligible (17, 18),
				"removedDevice" => Eligible (18),
				"installedVersion" => Eligible (17) with { InstalledDriverVersion = "0.9" },
				"availableVersion" => Eligible (17) with { AvailableDriverVersion = "1.2" },
				_ => Eligible (17) with { IsSwapDriverRequiresReboot = true }
				};
		var connection = new FakeConnection (current);
		var client = new ConfigurationClient (connection);
		Assert.ThrowsAsync<InvalidOperationException> (async () => await client.BeginDriverUpdateAsync (new ("driver", Eligible (17))));
		Assert.That (connection.Calls.Count, Is.EqualTo (1));
		}

	[Test]
	public void Plan_MissingEligibilityIsNotPermissionToUpdate ()
		{
		var client = new ConfigurationClient (new FakeConnection ((object?)null));
		Assert.ThrowsAsync<ProcessorApiException> (async () => await client.PlanDriverUpdateAsync ("driver"));
		}

	[Test]
	public async Task Reload_RequestsOnlyReferenceDevice ()
		{
		var connection = new FakeConnection (new DeviceInfo
			{
			Id = 17,
			PropertyValues = new ()
				{
				["cp.driverConfiguration:supportsUnloadReloadDriver"] = JsonSerializer.SerializeToElement (true),
				["cp.driverConfiguration:swapDriverRequiresReboot"] = JsonSerializer.SerializeToElement (false)
				}
			}, "reload-op");
		var client = new ConfigurationClient (connection);
		Assert.That (await client.BeginReloadDriverAsync (17), Is.EqualTo ("reload-op"));
		Assert.That (connection.Calls.Single ().Parameters.GetProperty ("reloadReferenceDeviceOnly").GetBoolean (), Is.True);
		Assert.That (connection.Calls.Single ().Parameters.GetProperty ("deviceId").GetInt32 (), Is.EqualTo (17));
		}

	[Test]
	public void Reload_MissingCapabilitiesDoesNotSubmit ()
		{
		var connection = new FakeConnection (new DeviceInfo { Id = 17 });
		Assert.ThrowsAsync<InvalidOperationException> (async () => await new ConfigurationClient (connection).BeginReloadDriverAsync (17));
		Assert.That (connection.Calls, Is.Empty);
		}

	[TestCase ("")]
	[TestCase (null)]
	public void MissingOperationIdDoesNotReportSuccessfulSubmission (string? operationId)
		{
		Assert.ThrowsAsync<ProcessorApiException> (async () => await new ConfigurationClient (new FakeConnection (operationId)).BeginLocalDriverRefreshAsync ());
		}

	private static DeviceInfo[] ReloadTree () =>
		[
		new () { Id = 17, PropertyValues = new ()
			{
			["cp.driverConfiguration:supportsUnloadReloadDriver"] = JsonSerializer.SerializeToElement (true),
			["cp.driverConfiguration:swapDriverRequiresReboot"] = JsonSerializer.SerializeToElement (false)
			} },
		new () { Id = 18, ParentDeviceId = 17 },
		new () { Id = 19, ParentDeviceId = 18 },
		new () { Id = 20 }
		];

	[Test]
	public async Task ReloadTree_IncludesChildrenEvenWhenAffectedQueryListsOnlyParent ()
		{
		var connection = new FakeConnection (ReloadTree ().ToDictionary (device => device.Id.ToString ()), new[] { 17 }, "tree-op");
		Assert.That (await new ConfigurationClient (connection).BeginReloadDriverTreeAsync (17, [19, 17, 18]), Is.EqualTo ("tree-op"));
		Assert.That (connection.Calls.Last ().Parameters.GetProperty ("reloadReferenceDeviceOnly").GetBoolean (), Is.False);
		Assert.That (connection.Calls.Last ().Parameters.GetProperty ("deviceId").GetInt32 (), Is.EqualTo (17));
		}

	[TestCase (new[] { 17, 18 })]
	[TestCase (new[] { 17, 18, 19, 20 })]
	public void ReloadTree_RejectsChangedTreeBeforeSubmitting (int[] reviewed)
		{
		var connection = new FakeConnection (ReloadTree ().ToDictionary (device => device.Id.ToString ()));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await new ConfigurationClient (connection).BeginReloadDriverTreeAsync (17, reviewed));
		Assert.That (connection.Calls, Is.Empty);
		}

	[TestCase ("unsupported")]
	[TestCase ("reboot")]
	[TestCase ("unknown")]
	public void ReloadTree_RequiresExplicitRebootFreeCapability (string condition)
		{
		var devices = ReloadTree ();
		if (condition == "unknown") devices[0].PropertyValues.Clear ();
		else devices[0].PropertyValues[condition == "reboot"
			? "cp.driverConfiguration:swapDriverRequiresReboot" : "cp.driverConfiguration:supportsUnloadReloadDriver"]
			= JsonSerializer.SerializeToElement (condition == "reboot");
		var connection = new FakeConnection (devices.ToDictionary (device => device.Id.ToString ()));
		Assert.ThrowsAsync<InvalidOperationException> (async () => await new ConfigurationClient (connection).BeginReloadDriverTreeAsync (17, [17, 18, 19]));
		Assert.That (connection.Calls, Is.Empty);
		}

	[TestCase (new[] { 17, 20 })]
	[TestCase (new[] { 18 })]
	[TestCase (new[] { 17, 17 })]
	public void ReloadTree_RejectsUnexpectedDependencyScope (int[] affected)
		{
		var connection = new FakeConnection (ReloadTree ().ToDictionary (device => device.Id.ToString ()), affected);
		Assert.ThrowsAsync<InvalidOperationException> (async () => await new ConfigurationClient (connection).BeginReloadDriverTreeAsync (17, [17, 18, 19]));
		Assert.That (connection.Calls.Select (call => call.Command), Does.Not.Contain ("cp.platformDriverController:beginReloadDrivers"));
		}

	[TestCase (new[] { 18, 19 })]
	[TestCase (new[] { 17, 17, 18, 19 })]
	[TestCase (new[] { 17, 0 })]
	public void ReloadTree_RejectsInvalidReviewedIds (int[] reviewed)
		{
		var connection = new FakeConnection ();
		Assert.ThrowsAsync<ArgumentException> (async () => await new ConfigurationClient (connection).BeginReloadDriverTreeAsync (17, reviewed));
		Assert.That (connection.Calls, Is.Empty);
		}

	[Test]
	public async Task Command_UsesNamedParametersAndPreservesOptionalResponseFields ()
		{
		var connection = new FakeConnection ((object)new[] { new DriverInfo { Id = "driver", Model = "Example Tests" } });
		var drivers = await new ConfigurationClient (connection).GetDriversAsync ("Example Tests");
		Assert.That (drivers.Single ().Version, Is.Null);
		Assert.That (connection.Calls.Single ().Parameters.GetProperty ("substringFilterTextTokens").EnumerateArray ().Select (x => x.GetString ()), Is.EqualTo (new[] { "Example", "Tests" }));
		}

	[TestCase (null)]
	[TestCase ("")]
	[TestCase ("   ")]
	public async Task CatalogueWithoutSearch_UsesAdvertisedCategories (string? search)
		{
		var categories = new[] { new { Id = "category.utility" }, new { Id = "category.weather" } };
		var connection = new FakeConnection (categories, new[] { new DriverInfo { Id = "driver" } });
		var drivers = await new ConfigurationClient (connection).GetDriversAsync (search);
		Assert.That (drivers.Single ().Id, Is.EqualTo ("driver"));
		Assert.That (connection.Calls.Select (call => call.Command), Is.EqualTo (new[]
			{ "cp.platformDriverController:getDriverMetadataFilterOptions", "cp.platformDriverController:getDrivers" }));
		Assert.That (connection.Calls.Last ().Parameters.GetProperty ("filterIds").EnumerateArray ().Select (id => id.GetString ()), Is.EqualTo (new[] { "category.utility", "category.weather" }));
		}

	[Test]
	public async Task CatalogueLongSearch_IntersectsEveryBatchByIdentity ()
		{
		var wanted = new DriverInfo { Id = "wanted", Model = "Example Long Driver Model With Seven Words" };
		var connection = new FakeConnection (
			new[] { wanted, new DriverInfo { Id = "first-only" }, new DriverInfo { Id = "first-two" } },
			new[] { new DriverInfo { Id = "wanted" }, new DriverInfo { Id = "first-two" } },
			new[] { new DriverInfo { Id = "wanted" }, new DriverInfo { Id = "last-only" } });
		var drivers = await new ConfigurationClient (connection).GetDriversAsync (wanted.Model);
		Assert.That (drivers.Select (driver => driver.Id), Is.EqualTo (new[] { "wanted" }));
		Assert.That (drivers.Single ().Model, Is.EqualTo (wanted.Model));
		Assert.That (connection.Calls.Select (call => call.Parameters.GetProperty ("substringFilterTextTokens").GetArrayLength ()), Is.EqualTo (new[] { 3, 3, 1 }));
		Assert.That (connection.Calls.SelectMany (call => call.Parameters.GetProperty ("substringFilterTextTokens").EnumerateArray ().Select (token => token.GetString ())), Is.EqualTo (wanted.Model.Split (' ')));
		}

	[Test]
	public async Task CatalogueLongSearch_StopsWhenNoCandidateCanMatch ()
		{
		var connection = new FakeConnection ((object)Array.Empty<DriverInfo> ());
		Assert.That (await new ConfigurationClient (connection).GetDriversAsync ("WeatherLink Live Weather Station"), Is.Empty);
		Assert.That (connection.Calls.Count, Is.EqualTo (1));
		}

	[Test]
	public void CatalogueLongSearch_MissingLaterResponseDoesNotReturnPartialMatches ()
		{
		var connection = new FakeConnection (new[] { new DriverInfo { Id = "candidate" } }, null);
		Assert.ThrowsAsync<ProcessorApiException> (async () => await new ConfigurationClient (connection).GetDriversAsync ("WeatherLink Live Weather Station"));
		Assert.That (connection.Calls.Count, Is.EqualTo (2));
		}

	[Test]
	public async Task CatalogueWithoutCategories_DoesNotSendInvalidEmptyRequest ()
		{
		var connection = new FakeConnection ((object)Array.Empty<object> ());
		Assert.That (await new ConfigurationClient (connection).GetDriversAsync (), Is.Empty);
		Assert.That (connection.Calls.Count, Is.EqualTo (1));
		}

	[Test]
	public void CatalogueMissingCategories_ReportsProtocolFailure ()
		{
		var connection = new FakeConnection ((object?)null);
		Assert.ThrowsAsync<ProcessorApiException> (async () => await new ConfigurationClient (connection).GetDriversAsync ());
		Assert.That (connection.Calls.Count, Is.EqualTo (1));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task RemovalChecksScopeAndTargetsOnlyReviewedInstance (bool sharedScope)
		{
		var device = new DeviceInfo
			{
			Id = 17,
			Model = "Example Tests",
			Commands = ["cp.deviceConfiguration:setLocation"],
			PropertyValues = new ()
				{
				["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("1.1"),
				["cp.driverConfiguration:supportsUnloadReloadDriver"] = JsonSerializer.SerializeToElement (true),
				["cp.driverConfiguration:swapDriverRequiresReboot"] = JsonSerializer.SerializeToElement (false)
				}
			};
		var connection = new FakeConnection (device, sharedScope ? new[] { 17, 18 } : new[] { 17 }, null, new Dictionary<string, DeviceInfo> ());
		var client = new ConfigurationClient (connection);
		if (sharedScope)
			{
			Assert.ThrowsAsync<InvalidOperationException> (async () => await client.RemoveDriverInstanceAsync (17, "Example Tests", "1.1", TimeSpan.FromSeconds (1)));
			Assert.That (connection.Calls.Count, Is.EqualTo (1));
			return;
			}
		await client.RemoveDriverInstanceAsync (17, "Example Tests", "1.1", TimeSpan.FromSeconds (1));
		Assert.That (connection.Calls.Last ().Id, Is.EqualTo (17));
		Assert.That (connection.Calls.Last ().Command, Is.EqualTo ("cp.deviceConfiguration:setLocation"));
		Assert.That (connection.Calls.Last ().Parameters.GetProperty ("locationId").ValueKind, Is.EqualTo (JsonValueKind.Null));
		}

	[Test]
	public void RemovalRefusesChangedModelBeforeSubmitting ()
		{
		var connection = new FakeConnection (new DeviceInfo { Id = 17, Model = "Actual Driver" });
		Assert.ThrowsAsync<InvalidOperationException> (async () => await new ConfigurationClient (connection).RemoveDriverInstanceAsync (17, "Example Tests", "1.1", TimeSpan.FromSeconds (1)));
		Assert.That (connection.Calls, Is.Empty);
		}

	[Test]
	public async Task VersionVerificationChecksEveryReviewedInstance ()
		{
		DeviceInfo State (int id, string version) => new ()
			{
			Id = id,
			PropertyValues = new ()
				{
				["cp.driverInformation:version"] = JsonSerializer.SerializeToElement (version),
				["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement ("Loaded")
				}
			};
		var connection = new FakeConnection (State (17, "1.0"), State (18, "1.1"), State (17, "1.1"), State (18, "1.1"));
		var states = await new ConfigurationClient (connection).WaitForDriverVersionAsync ([17, 18], "1.1", TimeSpan.FromSeconds (5));
		Assert.That (states.Select (state => state.DeviceId), Is.EqualTo (new[] { 17, 18 }));
		Assert.That (states.All (state => state.Version == "1.1" && state.LoadingStatus == "Loaded"), Is.True);
		}

	[Test]
	public void RequestedVersionLoadFailureStopsVerificationImmediately ()
		{
		var connection = new FakeConnection (VersionState (17, "1.003.0009.0000", "FailedToLoad"));
		var error = Assert.ThrowsAsync<InvalidOperationException> (async () =>
			await new ConfigurationClient (connection).WaitForDriverVersionAsync ([17], "1.3.9.0", TimeSpan.FromSeconds (5)));
		Assert.That (error!.Message, Does.Contain ("17").And.Contain ("FailedToLoad").And.Contain ("1.3.9.0"));
		Assert.That (connection.Calls, Is.Empty, "Observing a load failure must not issue another command.");
		}

	[Test]
	public async Task PreviousVersionLoadFailureDoesNotRejectIncomingVersion ()
		{
		var connection = new FakeConnection (VersionState (17, "1.0", "FailedToLoad"), VersionState (17, "1.1", "Loaded"));
		var states = await new ConfigurationClient (connection).WaitForDriverVersionAsync ([17], "1.1", TimeSpan.FromSeconds (5));
		Assert.That (states.Single ().LoadingStatus, Is.EqualTo ("Loaded"));
		}

	private static DeviceInfo VersionState (int id, string version, string loadingStatus) => new ()
		{
		Id = id,
		PropertyValues = new ()
			{
			["cp.driverInformation:version"] = JsonSerializer.SerializeToElement (version),
			["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement (loadingStatus)
			}
		};

	[Test]
	public void MissingDriverStateCannotVerifyAnUpdate ()
		{
		var connection = new FakeConnection (new DeviceInfo { Id = 17 });
		Assert.CatchAsync<OperationCanceledException> (async () => await new ConfigurationClient (connection).WaitForDriverVersionAsync ([17], "1.1", TimeSpan.FromMilliseconds (30)));
		}

	[Test]
	public void EmptyUpdateScopeCannotPassVerification ()
		{
		Assert.ThrowsAsync<ArgumentException> (async () => await new ConfigurationClient (new FakeConnection ()).WaitForDriverVersionAsync ([], "1.1", TimeSpan.FromSeconds (1)));
		}

	[Test]
	public async Task BorrowedConnectionIsNotDisposedWithClient ()
		{
		var connection = new FakeConnection ();
		await new ConfigurationClient (connection).DisposeAsync ();
		Assert.That (connection.Disposed, Is.False);
		await new ConfigurationClient (connection, true).DisposeAsync ();
		Assert.That (connection.Disposed, Is.True);
		}

	private sealed class FakeConnection (params object?[] responses) : IConfigurationConnection
		{
		private readonly Queue<object?> _responses = new (responses);
		public List<(int Id, string Command, JsonElement Parameters)> Calls { get; } = [];
		public bool Disposed
			{
			get; private set;
			}
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default) => Next<T> ();
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken cancellationToken = default)
			{
			Calls.Add ((id, command, JsonSerializer.SerializeToElement (parameters)));
			return Next<T> ();
			}
		private Task<T?> Next<T> () => Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (_responses.Dequeue ())));
		public Task<OperationResult> WaitForOperationAsync (string id, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync ()
			{
			Disposed = true;
			return ValueTask.CompletedTask;
			}
		}
	}

[TestFixture]
public sealed class ConnectionTests
	{
	[Test]
	public async Task Request_UnwrapsResponseAndDoesNotConfuseFalseWithMissing ()
		{
		var handler = new StubHandler (HttpStatusCode.OK, "{\"Result\":false,\"Error\":null}");
		await using var connection = Create (handler);
		Assert.That (await connection.ExecuteAsync<bool?> (-6, "readStatus"), Is.False);
		Assert.That (handler.Path, Is.EqualTo ("https://processor.example/cws/api/v2/Devices/-6/Command"));
		using var request = JsonDocument.Parse (handler.Body!);
		Assert.That (request.RootElement.GetProperty ("Parameters").EnumerateObject (), Is.Empty);
		}

	[TestCase (HttpStatusCode.InternalServerError)]
	[TestCase (HttpStatusCode.Unauthorized)]
	[TestCase (HttpStatusCode.Found)]
	[TestCase (HttpStatusCode.UnprocessableEntity)]
	public void Failure_DoesNotRetryWriteOrExposeResponseBody (HttpStatusCode status)
		{
		var handler = new StubHandler (status, "private-password-sentinel");
		var connection = Create (handler);
		var error = Assert.ThrowsAsync<ProcessorApiException> (async () => await connection.ExecuteAsync<string> (17, "change"));
		Assert.That (handler.Count, Is.EqualTo (1));
		Assert.That (error!.Message, Does.StartWith ("Configuration command failed with HTTP"));
		Assert.That (error.ToString (), Does.Not.Contain ("private-password-sentinel"));
		}

	[Test]
	public void ApiError_IsNotSuccessfulNullResultAndDoesNotExposePrivateText ()
		{
		var connection = Create (new StubHandler (HttpStatusCode.OK, "{\"Result\":null,\"Error\":{\"DebugText\":\"private-password-sentinel\"}}"));
		var error = Assert.ThrowsAsync<ProcessorApiException> (async () => await connection.ExecuteAsync<string> (17, "change"));
		Assert.That (error!.ToString (), Does.Not.Contain ("private-password-sentinel"));
		}

	[TestCase ("https://elsewhere.example/steal")]
	[TestCase ("//elsewhere.example/steal")]
	[TestCase ("v2/../../elsewhere")]
	[TestCase ("v2/%2e%2e/elsewhere")]
	public void CallerCannotRedirectAuthenticatedRequestOutsideApi (string path)
		{
		var handler = new StubHandler (HttpStatusCode.OK, "{}");
		Assert.ThrowsAsync<ArgumentException> (async () => await Create (handler).GetAsync<string> (path));
		Assert.That (handler.Count, Is.Zero);
		}

	[Test]
	public void CertificatePin_MustMatchEvenWhenSystemTrustSucceeds ()
		{
		using var key = RSA.Create (2048);
		var request = new CertificateRequest ("CN=processor.example", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		using var certificate = request.CreateSelfSigned (DateTimeOffset.UtcNow.AddMinutes (-1), DateTimeOffset.UtcNow.AddDays (1));
		var correct = certificate.GetCertHashString (HashAlgorithmName.SHA256);
		Assert.Multiple (() =>
		{
			Assert.That (ProcessorConnection.ValidateCertificate (certificate, SslPolicyErrors.RemoteCertificateChainErrors, correct), Is.True);
			Assert.That (ProcessorConnection.ValidateCertificate (certificate, SslPolicyErrors.None, new string ('0', 64)), Is.False);
			Assert.That (ProcessorConnection.ValidateCertificate (certificate, SslPolicyErrors.RemoteCertificateChainErrors, null), Is.False);
		});
		}

	private static ProcessorConnection Create (StubHandler handler) => new (new HttpClient (handler) { BaseAddress = new Uri ("https://processor.example/cws/api/") });

	private sealed class StubHandler (HttpStatusCode status, string responseBody) : HttpMessageHandler
		{
		public int Count
			{
			get; private set;
			}
		public string? Path
			{
			get; private set;
			}
		public string? Body
			{
			get; private set;
			}
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken cancellationToken)
			{
			Count++;
			Path = request.RequestUri!.ToString ();
			Body = request.Content == null ? null : await request.Content.ReadAsStringAsync (cancellationToken);
			return new HttpResponseMessage (status) { Content = new StringContent (responseBody, Encoding.UTF8, "application/json") };
			}
		}
	}

[TestFixture]
public sealed class OperationTests
	{
	private static void Event (OperationTracker tracker, object status, string id = "op")
		 => tracker.Accept (JsonSerializer.SerializeToElement (new
			 {
			 EventType = "cp.types:operationStatusChanged",
			 OperationId = id,
			 LatestStatus = status
			 }));

	[TestCase ("Succeeded", true)]
	[TestCase ("Failed", false)]
	[TestCase ("Ended", false)]
	[TestCase (3, true)]
	[TestCase (2, false)]
	public async Task TerminalEventBeforeWaitIsRetained (object status, bool success)
		{
		var tracker = new OperationTracker ();
		Event (tracker, status);
		Assert.That ((await tracker.WaitAsync ("op", TimeSpan.FromSeconds (1), default)).Succeeded, Is.EqualTo (success));
		}

	[Test]
	public async Task EndedDoesNotOverwriteFailed ()
		{
		var tracker = new OperationTracker ();
		Event (tracker, "Failed");
		Event (tracker, "Ended");
		Assert.That ((await tracker.WaitAsync ("op", TimeSpan.FromSeconds (1), default)).Status, Is.EqualTo ("Failed"));
		}

	[Test]
	public async Task ProgressDoesNotCompleteWaitAndUnrelatedOperationIsIgnored ()
		{
		var tracker = new OperationTracker ();
		var pending = tracker.WaitAsync ("op", TimeSpan.FromSeconds (1), default);
		Event (tracker, "Progress");
		Event (tracker, "Succeeded", "other");
		Assert.That (pending.IsCompleted, Is.False);
		Event (tracker, "Succeeded");
		Assert.That ((await pending).Succeeded, Is.True);
		}

	[Test]
	public async Task CancelledWaitCanBeRetriedWithoutLosingEvent ()
		{
		var tracker = new OperationTracker ();
		using var cancellation = new CancellationTokenSource ();
		var pending = tracker.WaitAsync ("op", TimeSpan.FromSeconds (1), cancellation.Token);
		cancellation.Cancel ();
		Assert.CatchAsync<OperationCanceledException> (async () => await pending);
		Event (tracker, "Succeeded");
		Assert.That ((await tracker.WaitAsync ("op", TimeSpan.FromSeconds (1), default)).Succeeded, Is.True);
		}

	[Test]
	public void ConnectionLossFailsPendingAndFutureWaits ()
		{
		var tracker = new OperationTracker ();
		var pending = tracker.WaitAsync ("op", TimeSpan.FromSeconds (1), default);
		tracker.Fail (new IOException ("disconnected"));
		Assert.ThrowsAsync<IOException> (async () => await pending);
		Assert.ThrowsAsync<IOException> (async () => await tracker.WaitAsync ("other", TimeSpan.FromSeconds (1), default));
		}

	[Test]
	public void TimeoutDoesNotPretendOperationFailedOnProcessor ()
		{
		var tracker = new OperationTracker ();
		Assert.ThrowsAsync<TimeoutException> (async () => await tracker.WaitAsync ("op", TimeSpan.FromMilliseconds (20), default));
		}
	}
