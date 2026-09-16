// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DriverNameChallengeTests
	{
	private string _directory = null!;
	private FakeConnection _connection = null!;
	private bool _held;
	private readonly List<DriverNameChallengeObservation> _observations = [];
	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (TestContext.CurrentContext.WorkDirectory, "binding-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		_connection = new ();
		_held = true;
		_observations.Clear ();
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_directory, recursive: true);
	private Task<DriverNameChallengeResult> Run (Func<DriverNameChallengeObservation, CancellationToken, Task>? action = null, CancellationToken token = default) =>
		DriverNameChallenge.RunAsync (new (_connection), new (17, "Gateway", "1.0.000.0000", "Existing"), _directory,
			() => { if (!_held) throw new IOException ("Reservation lost"); }, async (observation, ct) =>
			{
			_observations.Add (observation);
			Assert.That (_connection.Name, Is.EqualTo (observation.ExpectedName));
			if (action != null) await action (observation, ct);
			}, TimeSpan.FromMilliseconds (200), token);

	[Test]
	public async Task SuccessObservesOriginalChallengeAndRestoredWithExactlyTwoWrites ()
		{
		var result = await Run ();
		Assert.That (result.NameRestored && result.ChallengeObserved, Is.True);
		Assert.That (_connection.Names, Is.EqualTo (new[] { result.ChallengeName, "Original" }));
		Assert.That (_observations.Select (o => o.Phase), Is.EqualTo (new[] { DriverNameChallengePhase.Before, DriverNameChallengePhase.Challenge, DriverNameChallengePhase.Restored }));
		Assert.That (Directory.GetFiles (_directory, "*-intent.json"), Has.Length.EqualTo (1));
		Assert.That (_connection.Name, Is.EqualTo ("Original"));
		}

	[TestCase ("version")]
	[TestCase ("model")]
	[TestCase ("unloaded")]
	[TestCase ("unsupported")]
	[TestCase ("duplicate")]
	public void IneligibleOrAmbiguousTargetNeverRenames (string failure)
		{
		_connection.Failure = failure;
		Assert.ThrowsAsync<InvalidDataException> (async () => await Run ());
		Assert.That (_connection.Names, Is.Empty);
		}

	[Test]
	public void FailedUiAssertionRestoresNameAndPreservesOriginalFailure ()
		{
		var expected = new InvalidOperationException ("UI mismatch");
		var actual = Assert.ThrowsAsync<InvalidOperationException> (async () => await Run ((o, _) =>
			{ if (o.Phase == DriverNameChallengePhase.Challenge) throw expected; return Task.CompletedTask; }));
		Assert.That (actual, Is.SameAs (expected));
		Assert.That (_connection.Name, Is.EqualTo ("Original"));
		Assert.That (_observations.Last ().Phase, Is.EqualTo (DriverNameChallengePhase.Restored));
		}

	[Test]
	public void CancelledUiAssertionUsesIndependentRestorationToken ()
		{
		using var cancel = new CancellationTokenSource ();
		Assert.ThrowsAsync<OperationCanceledException> (async () => await Run ((o, token) =>
			{
			if (o.Phase == DriverNameChallengePhase.Challenge) { cancel.Cancel (); token.ThrowIfCancellationRequested (); }
			if (o.Phase == DriverNameChallengePhase.Restored) Assert.That (token.IsCancellationRequested, Is.False);
			return Task.CompletedTask;
			}, cancel.Token));
		Assert.That (_connection.Name, Is.EqualTo ("Original"));
		}

	[Test]
	public void LostReplyWithObservedChangedNameRestoresWithoutReplayingChallenge ()
		{
		_connection.Failure = "lost-after-change";
		Assert.ThrowsAsync<IOException> (async () => await Run ());
		Assert.That (_connection.Names, Has.Count.EqualTo (2));
		Assert.That (_connection.Name, Is.EqualTo ("Original"));
		}

	[Test]
	public void UnobservedLostReplyRemainsUncertainAndIsNeverReplayed ()
		{
		_connection.Failure = "lost-before-change";
		Assert.ThrowsAsync<AggregateException> (async () => await Run ());
		Assert.That (_connection.Names, Has.Count.EqualTo (1));
		Assert.That (_observations, Has.Count.EqualTo (1));
		using var result = JsonDocument.Parse (File.ReadAllText (Directory.GetFiles (_directory, "*-result.json").Single ()));
		Assert.That (result.RootElement.GetProperty ("NameRestored").GetBoolean (), Is.False);
		}

	[Test]
	public void ExternalRenameIsNotOverwritten ()
		{
		Assert.ThrowsAsync<InvalidDataException> (async () => await Run ((o, _) =>
			{ if (o.Phase == DriverNameChallengePhase.Challenge) _connection.Name = "External name"; return Task.CompletedTask; }));
		Assert.That (_connection.Name, Is.EqualTo ("External name"));
		Assert.That (_connection.Names, Has.Count.EqualTo (1));
		}

	[Test]
	public void LostReservationPreventsAnyFurtherWrite ()
		{
		Assert.ThrowsAsync<AggregateException> (async () => await Run ((o, _) =>
			{ if (o.Phase == DriverNameChallengePhase.Challenge) _held = false; return Task.CompletedTask; }));
		Assert.That (_connection.Names, Has.Count.EqualTo (1));
		}

	private sealed class FakeConnection : IConfigurationConnection
		{
		internal string Name = "Original";
		internal string? Failure;
		internal List<string> Names = [];
		private DeviceInfo Device => new () { Id = 17, Name = Name, Model = Failure == "model" ? "Other" : "Gateway", ParentDeviceId = -6, LocationId = 12,
			Commands = Failure == "unsupported" ? [] : ["deviceName:setName"], PropertyValues = new ()
				{
				["cp.driverInformation:version"] = JsonSerializer.SerializeToElement (Failure == "version" ? "2.0.0.0" : "1.0.000.0000"),
				["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement (Failure == "unloaded" ? "Unloaded" : "Loaded")
				} };
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default)
			{
			object value = Device;
			if (path == "v2/Devices")
				{
				var devices = new Dictionary<string, DeviceInfo> { ["17"] = Device };
				if (Failure == "duplicate") devices["18"] = Device with { Id = 18 };
				value = devices;
				}
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (value)));
			}
		public Task<T?> ExecuteAsync<T> (int deviceId, string commandName, object? parameters = null, CancellationToken cancellationToken = default)
			{
			Assert.That (deviceId, Is.EqualTo (17));
			Assert.That (commandName, Is.EqualTo ("deviceName:setName"));
			var name = JsonSerializer.SerializeToElement (parameters).GetProperty ("name").GetString ()!;
			Names.Add (name);
			if (Failure == "lost-before-change" && Names.Count == 1) throw new IOException ("Unknown response");
			Name = name;
			if (Failure == "lost-after-change" && Names.Count == 1) throw new IOException ("Unknown response");
			return Task.FromResult (default (T));
			}
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}