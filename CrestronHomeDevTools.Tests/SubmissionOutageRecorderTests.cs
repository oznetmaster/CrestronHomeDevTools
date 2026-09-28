// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionOutageRecorderTests
	{
	private string _root = null!;
	private Clock _clock = null!;
	private Hardware _hardware = null!;
	private SubmissionOutageMeasurementPlan _plan = null!;
	private CancellationTokenSource _cancel = null!;
	private int _holds;

	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "outage-recorder-" + Guid.NewGuid ().ToString ("N"));
		_clock = new ();
		_cancel = new ();
		_hardware = new (_clock, _cancel);
		_plan = new (new (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64)),
			 "system.network", ["processor", "device"], ["control", "feedback"],
			 TimeSpan.FromSeconds (60), TimeSpan.FromSeconds (60), SubmissionOutageRecoveryClock.NetworkRestored);
		_holds = 0;
		}
	[TearDown]
	public void TearDown ()
		{
		_cancel.Dispose ();
		if (Directory.Exists (_root))
			Directory.Delete (_root, true);
		}
	private Task<SubmissionOutageRecordingResult> Run () => SubmissionOutageRecorder.RecordCoreAsync (_plan,
		 _hardware, _root, TimeSpan.FromMinutes (10), TimeSpan.FromMinutes (1), _clock, (duration, ct) =>
		 {
			 ct.ThrowIfCancellationRequested ();
			 _holds++;
			 Assert.That (_hardware.Interrupted, Is.EquivalentTo (_plan.RequiredComponents));
			 _clock.Advance (duration);
			 return Task.CompletedTask;
		 }, _cancel.Token);

	[Test]
	public async Task RecordsAllStagesAndAssessesCapturedBounds ()
		{
		var result = await Run ();
		Assert.Multiple (() =>
		{
			Assert.That (result.Passed, Is.True);
			Assert.That (result.Measurements!.GuaranteedInterruptionSeconds, Is.GreaterThanOrEqualTo (60));
			Assert.That (_holds, Is.EqualTo (1));
			Assert.That (_hardware.Interrupted, Is.Empty);
			Assert.That (_hardware.RestoreOriginalCalls, Is.EqualTo (1));
			Assert.That (_hardware.CheckedFunctions, Is.EqualTo (_plan.RequiredFunctions));
			Assert.That (File.Exists (Path.Combine (_root, result.RecordRelativePath!)), Is.True);
			Assert.That (File.Exists (Path.Combine (_root, "recording-result.json")), Is.True);
		});
		}

	[TestCase ("interrupt")]
	[TestCase ("cancel")]
	[TestCase ("capture-hash")]
	[TestCase ("journal")]
	public async Task RestoresAnAttemptedCommandEvenWhenItThrowsOrRecordingFails (string failure)
		{
		_hardware.Failure = failure;
		var result = await Run ();
		Assert.Multiple (() =>
		{
			Assert.That (result.Passed, Is.False);
			Assert.That (result.Issues, Is.Not.Empty);
			Assert.That (result.RecordRelativePath, Is.Null);
			Assert.That (_hardware.Interrupted, Is.Empty);
			Assert.That (_hardware.Restored, Does.Contain ("processor"));
			Assert.That (_hardware.RestoreOriginalCalls, Is.EqualTo (1));
			Assert.That (_hardware.CheckedFunctions, Is.Empty);
			Assert.That (File.Exists (Path.Combine (_root, "measurements.json")), Is.False);
			Assert.That (_holds, Is.Zero);
		});
		}

	[Test]
	public async Task OneFailedRestorationDoesNotPreventOtherRestorationAttempts ()
		{
		_hardware.Failure = "connectivity";
		var result = await Run ();
		Assert.Multiple (() =>
		{
			Assert.That (result.Passed, Is.False);
			Assert.That (_hardware.Restored, Is.EqualTo (new[] { "device", "processor" }));
			Assert.That (_hardware.RestoreOriginalCalls, Is.EqualTo (1));
			Assert.That (result.Issues, Does.Contain ("connectivity-restoration:IOException"));
			Assert.That (_hardware.CheckedFunctions, Is.Empty);
		});
		}

	[TestCase ("function")]
	[TestCase ("original")]
	public async Task FunctionAndFinalRestorationErrorsCannotProducePassingMeasurements (string failure)
		{
		_hardware.Failure = failure;
		var result = await Run ();
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RecordRelativePath, Is.Null);
		Assert.That (_hardware.RestoreOriginalCalls, Is.EqualTo (1));
		Assert.That (_hardware.Interrupted, Is.Empty);
		}

	[TestCase ("preflight")]
	[TestCase ("baseline")]
	public async Task FailureBeforeFirstCommandDoesNotOperateEquipment (string failure)
		{
		_hardware.Failure = failure;
		var result = await Run ();
		Assert.That (result.Passed, Is.False);
		Assert.That (_hardware.Commands, Is.Empty);
		Assert.That (_hardware.RestoreOriginalCalls, Is.Zero);
		}

	[Test]
	public async Task MissingProgramMarkerRemainsPartial ()
		{
		_plan = _plan with
			{
			RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded,
			ProgramComponent = "processor"
			};
		_hardware.Failure = "no-load-marker";
		var result = await Run ();
		Assert.That (result.Passed, Is.False);
		Assert.That (result.Measurements!.Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Partial));
		Assert.That (result.Measurements.Issues, Does.Contain ("program-load-time-not-observed"));
		Assert.That (result.RecordRelativePath, Is.Not.Null, "A valid partial measurement must be retained.");
		}

	[Test]
	public async Task UsesProgramMarkerAndRetainsFailedStateComparison ()
		{
		_plan = _plan with
			{
			RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded,
			ProgramComponent = "processor"
			};
		_hardware.Failure = "state-mismatch";
		var result = await Run ();
		Assert.That (result.Passed, Is.False);
		Assert.That (result.Measurements!.Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Failed));
		Assert.That (result.Measurements.Issues, Does.Contain ("original-state-not-restored"));
		Assert.That (result.RecordRelativePath, Is.Not.Null);
		}

	[Test]
	public async Task ExistingAttemptCannotBeReplayed ()
		{
		await Run ();
		int commands = _hardware.Commands.Count;
		await Assert.ThrowsAsync<IOException> (async () => await Run ());
		Assert.That (_hardware.Commands, Has.Count.EqualTo (commands));
		}

	[Test]
	public async Task UnboundScopeIsRejectedBeforePreflight ()
		{
		_plan = _plan with
			{
			RequiredComponents = ["processor", "device", "network"]
			};
		await Assert.ThrowsAsync<InvalidDataException> (async () => await Run ());
		Assert.That (_hardware.PreflightCalls, Is.Zero);
		Assert.That (Directory.Exists (_root), Is.False);
		}

	[Test]
	public async Task InvalidPlanIsRejectedBeforePreflight ()
		{
		_plan = _plan with
			{
			RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded
			};
		await Assert.ThrowsAsync<InvalidDataException> (async () => await Run ());
		Assert.That (_hardware.PreflightCalls, Is.Zero);
		}

	[Test]
	public async Task ErrorDetailsAreNotCopiedIntoEvidence ()
		{
		_hardware.Failure = "interrupt";
		await Run ();
		string all = string.Join ("\n", Directory.GetFiles (_root).Select (File.ReadAllText));
		Assert.That (all, Does.Not.Contain ("synthetic-secret"));
		}

	[TestCase ("failed-function", SubmissionEvidenceOutcome.Failed)]
	[TestCase ("late-function", SubmissionEvidenceOutcome.Failed)]
	[TestCase ("early-restoration", SubmissionEvidenceOutcome.Partial)]
	public async Task RealMeasurementLimitsStillApplyAfterSuccessfulCallbacks (string failure, SubmissionEvidenceOutcome outcome)
		{
		_hardware.Failure = failure;
		var result = await Run ();
		Assert.That (result.Passed, Is.False);
		Assert.That (result.Measurements!.Outcome, Is.EqualTo (outcome));
		}

	[Test]
	public async Task RecordedMeasurementsImportThroughTheNormalEvidenceGate ()
		{
		var rule = new SubmissionRequirement (_plan.RequirementId, TimeSpan.FromSeconds (60), false,
			 new ("test/system", "outage", SubmissionEvidenceOutcome.Passed, 60, true));
		var json = new JsonSerializerOptions
			{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			Converters = { new JsonStringEnumConverter () }
			};
		byte[] policy = JsonSerializer.SerializeToUtf8Bytes (new SubmissionEvidencePolicy (1, [rule]), json);
		_plan = _plan with
			{
			Identity = _plan.Identity with
				{
				PolicySha256 = Convert.ToHexStringLower (SHA256.HashData (policy))
				}
			};
		var recorded = await Run ();
		Assert.That (recorded.Passed, Is.True);
		File.WriteAllBytes (Path.Combine (_root, "policy.json"), policy);
		string Hash (string path) => Convert.ToHexStringLower (SHA256.HashData (File.ReadAllBytes (Path.Combine (_root, path))));
		var result = SubmissionOutageEvidence.ImportFiles (_root, "plan.json", Hash ("plan.json"),
			 recorded.RecordRelativePath!, Hash (recorded.RecordRelativePath!), "policy.json", _clock.GetUtcNow ());
		Assert.That (SubmissionEvidence.Evaluate (_plan.Identity, [rule], result.Observations.Observations,
			 _root, _clock.GetUtcNow ()).EvidenceChecksPassed, Is.True);
		}

	private sealed class Clock : TimeProvider
		{
		private DateTimeOffset _now = new (2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow () => _now;
		public void Advance (TimeSpan interval) => _now += interval;
		}

	private sealed class Hardware (Clock clock, CancellationTokenSource caller) : ISubmissionOutageHardware
		{
		public IReadOnlyList<string> Components => ["processor", "device"];
		public IReadOnlyList<string> Functions => ["control", "feedback"];
		public string Failure = "";
		public readonly List<string> Commands = [];
		public readonly List<string> Interrupted = [];
		public readonly List<string> Restored = [];
		public readonly List<string> CheckedFunctions = [];
		public int RestoreOriginalCalls, PreflightCalls;
		private string _root = null!;
		private int _sequence;
		private SubmissionOutageCapture? _lastInterruption;
		public Task PreflightAsync (SubmissionOutageRecordingContext context, CancellationToken token)
			{
			PreflightCalls++;
			_root = context.EvidenceDirectory;
			if (Failure == "preflight")
				throw new IOException ("synthetic-secret");
			return Task.CompletedTask;
			}
		private SubmissionOutageCapture Capture ()
			{
			DateTimeOffset first = clock.GetUtcNow ();
			clock.Advance (TimeSpan.FromSeconds (1));
			return CaptureAt (first, clock.GetUtcNow ());
			}
		private SubmissionOutageCapture CaptureAt (DateTimeOffset first, DateTimeOffset last)
			{
			string name = "synthetic-" + _sequence++ + ".json";
			byte[] bytes = JsonSerializer.SerializeToUtf8Bytes (new
				{
				Synthetic = true,
				Earliest = first,
				Latest = last
				});
			File.WriteAllBytes (Path.Combine (_root, name), bytes);
			return new (first, last, new (name, Convert.ToHexStringLower (SHA256.HashData (bytes))));
			}
		public Task<SubmissionOutageCapture> CaptureOriginalAsync (CancellationToken token)
			{
			if (Failure == "baseline")
				throw new IOException ("synthetic-secret");
			return Task.FromResult (Capture ());
			}
		public Task<SubmissionOutageCapture> InterruptAsync (string component, CancellationToken token)
			{
			Commands.Add (component);
			Interrupted.Add (component);
			if (Failure == "interrupt")
				throw new IOException ("synthetic-secret");
			if (Failure == "cancel")
				{
				caller.Cancel ();
				token.ThrowIfCancellationRequested ();
				}
			var capture = Capture ();
			if (Failure == "capture-hash")
				capture = capture with
					{
					Evidence = capture.Evidence with
						{
						Sha256 = new ('0', 64)
						}
					};
			if (Failure == "journal")
				File.WriteAllText (Path.Combine (_root, "002-interrupted.json"), "synthetic collision");
			_lastInterruption = capture;
			return Task.FromResult (capture);
			}
		public Task<SubmissionOutageCapture> RestoreConnectivityAsync (string component, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Restored.Add (component);
			if (Failure == "connectivity" && component == "device")
				throw new IOException ("synthetic-secret");
			Interrupted.Remove (component);
			if (Failure == "early-restoration")
				{
				var early = _lastInterruption!.LatestUtc.AddSeconds (20);
				return Task.FromResult (CaptureAt (early, early));
				}
			return Task.FromResult (Capture ());
			}
		public Task<SubmissionOutageCapture?> ObserveProgramLoadedAsync (string component, CancellationToken token) =>
			 Task.FromResult (Failure == "no-load-marker" ? null : Capture ());
		public Task<SubmissionOutageFunction> VerifyFunctionAsync (string function, CancellationToken token)
			{
			CheckedFunctions.Add (function);
			if (Failure == "function")
				throw new IOException ("synthetic-secret");
			if (Failure == "late-function")
				clock.Advance (TimeSpan.FromSeconds (90));
			return Task.FromResult (new SubmissionOutageFunction (function,
				 Failure == "failed-function" ? SubmissionEvidenceOutcome.Failed : SubmissionEvidenceOutcome.Passed, Capture ()));
			}
		public Task<SubmissionOutageRestoredState> RestoreOriginalAsync (SubmissionOutageCapture original, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			RestoreOriginalCalls++;
			if (Failure == "original")
				throw new IOException ("synthetic-secret");
			return Task.FromResult (new SubmissionOutageRestoredState (Capture (), Failure != "state-mismatch"));
			}
		}
	}