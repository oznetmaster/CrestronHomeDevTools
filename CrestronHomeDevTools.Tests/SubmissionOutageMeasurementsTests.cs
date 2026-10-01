// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionOutageMeasurementsTests
	{
	private static readonly DateTimeOffset Start = new (2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
	private string _root = null!;
	private SubmissionEvidenceFile _proof = null!;
	private SubmissionOutageMeasurementPlan _plan = null!;
	private SubmissionOutageMeasurementRecord _record = null!;
	private SubmissionOutageCapture At (double first, double? last = null) => new (Start.AddSeconds (first), Start.AddSeconds (last ?? first), _proof);
	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "outage-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		File.WriteAllText (Path.Combine (_root, "synthetic.txt"), "Synthetic fixture; not hardware evidence.");
		_proof = new ("synthetic.txt", Convert.ToHexStringLower (SHA256.HashData (File.ReadAllBytes (Path.Combine (_root, "synthetic.txt")))));
		var identity = new SubmissionEvidenceIdentity (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64));
		_plan = new (identity, "system.network", ["processor", "device"], ["control", "feedback"], TimeSpan.FromSeconds (60), TimeSpan.FromSeconds (60), SubmissionOutageRecoveryClock.NetworkRestored);
		_record = new (1, identity, [new ("processor", At (10, 11), At (80, 82)), new ("device", At (12, 13), At (78, 81))], null,
			 [new ("control", SubmissionEvidenceOutcome.Passed, At (100, 102)), new ("feedback", SubmissionEvidenceOutcome.Passed, At (110, 112))], At (0, 1), At (150, 151), true);
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, true);
	private SubmissionOutageMeasurementReport Assess () => SubmissionOutageMeasurements.Assess (_plan, _record, _root, Start.AddSeconds (200));
	[Test]
	public void UsesCommonGuaranteedDowntimeAndWorstCaseRecovery ()
		{
		var report = Assess ();
		Assert.Multiple (() =>
		{
			Assert.That (report.MeasurementChecksPassed, Is.True);
			Assert.That (report.GuaranteedInterruptionSeconds, Is.EqualTo (65));
			Assert.That (report.MinimumRecoverySeconds, Is.EqualTo (28));
			Assert.That (report.MaximumRecoverySeconds, Is.EqualTo (32));
		});
		}
	[TestCase (139, 140, SubmissionEvidenceOutcome.Passed)]
	[TestCase (141, 143, SubmissionEvidenceOutcome.Partial)]
	[TestCase (143, 144, SubmissionEvidenceOutcome.Failed)]
	public void RecoveryDeadlineUsesBoundsNotMidpoint (double first, double last, SubmissionEvidenceOutcome expected)
		{
		_record = _record with
			{
			Functions = [_record.Functions[0], _record.Functions[1] with { Observation = At (first, last) }]
			};
		Assert.That (Assess ().Outcome, Is.EqualTo (expected));
		}
	[Test]
	public void SequentialSixtySecondOutagesDoNotProveCommonOutage ()
		{
		_record = _record with
			{
			Interruptions = [new ("processor", At (10), At (70)), new ("device", At (75), At (135))],
			Functions = [new ("control", SubmissionEvidenceOutcome.Passed, At (140)), new ("feedback", SubmissionEvidenceOutcome.Passed, At (141))]
			};
		var result = Assess ();
		Assert.That (result.GuaranteedInterruptionSeconds, Is.Zero);
		Assert.That (result.Issues, Does.Contain ("minimum-interruption-unproven"));
		}
	[Test]
	public void PowerClockUsesProgramLoadRatherThanNetworkRestoration ()
		{
		_plan = _plan with
			{
			RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded,
			ProgramComponent = "processor"
			};
		_record = _record with
			{
			ProgramLoaded = At (95, 96)
			};
		var result = Assess ();
		Assert.That (result.MeasurementChecksPassed, Is.True);
		Assert.That (result.MaximumRecoverySeconds, Is.EqualTo (17));
		}
	[Test]
	public void MissingProgramLoadCannotPass ()
		{
		_plan = _plan with
			{
			RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded,
			ProgramComponent = "processor"
			};
		Assert.That (Assess ().Issues, Does.Contain ("program-load-time-not-observed"));
		}
	[TestCase (140, SubmissionEvidenceOutcome.Passed)]
	[TestCase (140.001, SubmissionEvidenceOutcome.Partial)]
	[TestCase (149, SubmissionEvidenceOutcome.Partial)]
	public void ProgramStartOnlyProvesRecoveryInsideConservativeDeadline (double recovered, SubmissionEvidenceOutcome expected)
		{
		_plan = _plan with { RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded, ProgramComponent = "processor" };
		_record = _record with { SchemaVersion = 2, ProgramLoadIsLowerBound = true, ProgramLoaded = At (80, 82),
			Interruptions = [new ("processor", At (10, 11), At (75, 79)), new ("device", At (12, 13), At (75, 79))],
			Functions = [new ("control", SubmissionEvidenceOutcome.Passed, At (100)), new ("feedback", SubmissionEvidenceOutcome.Passed, At (recovered))] };
		var result = Assess ();
		Assert.That (result.Outcome, Is.EqualTo (expected));
		Assert.That (result.MinimumRecoverySeconds, Is.Null, "No load-completion upper bound was observed.");
		Assert.That (result.MaximumRecoverySeconds, Is.EqualTo (recovered - 80).Within (0.000000001));
		Assert.That (result.Issues, Does.Not.Contain ("recovery-deadline-exceeded"));
		if (expected == SubmissionEvidenceOutcome.Partial)
			Assert.That (result.Issues, Does.Contain ("program-load-completion-not-observed"));
		var inputs = Inputs ();
		var imported = Import (inputs.Plan, inputs.Record);
		Assert.That (imported.Measurements.Outcome, Is.EqualTo (expected));
		Assert.That (SubmissionEvidence.Evaluate (_plan.Identity, [inputs.Rule], imported.Observations.Observations,
			_root, Start.AddSeconds (200)).EvidenceChecksPassed, Is.EqualTo (expected == SubmissionEvidenceOutcome.Passed));
		}
	[TestCase ("old-schema")]
	[TestCase ("missing-start")]
	[TestCase ("network")]
	public void LowerBoundMarkerCannotBeMisinterpreted (string variant)
		{
		_plan = _plan with { RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded, ProgramComponent = "processor" };
		_record = _record with { SchemaVersion = 2, ProgramLoadIsLowerBound = true, ProgramLoaded = At (95, 96) };
		if (variant == "old-schema") _record = _record with { SchemaVersion = 1 };
		if (variant == "missing-start") _record = _record with { ProgramLoaded = null };
		if (variant == "network") _plan = _plan with { RecoveryClock = SubmissionOutageRecoveryClock.NetworkRestored, ProgramComponent = null };
		Assert.Throws<InvalidDataException> (() => Assess ());
		}
	[Test]
	public void ConservativeClockDoesNotHideFunctionalFailure ()
		{
		_plan = _plan with { RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded, ProgramComponent = "processor" };
		_record = _record with { SchemaVersion = 2, ProgramLoadIsLowerBound = true, ProgramLoaded = At (95, 96),
			Functions = [_record.Functions[0] with { Outcome = SubmissionEvidenceOutcome.Failed }, _record.Functions[1]] };
		Assert.That (Assess ().Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Failed));
		}
	[Test]
	public void ProgramLoadBeforeProvenPowerRestorationCannotPass ()
		{
		_plan = _plan with
			{
			RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded,
			ProgramComponent = "processor"
			};
		_record = _record with
			{
			ProgramLoaded = At (79, 83)
			};
		Assert.That (Assess ().Issues, Does.Contain ("program-load-not-after-power-restoration"));
		}
	[Test]
	public void LoadFromBeforeOutageIsRejected ()
		{
		_plan = _plan with
			{
			RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded,
			ProgramComponent = "processor"
			};
		_record = _record with
			{
			ProgramLoaded = At (5)
			};
		Assert.Throws<InvalidDataException> (() => Assess ());
		}
	[Test]
	public void FunctionalObservationBeforeLastComponentRestoresCannotPass ()
		{
		_plan = _plan with
			{
			RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded,
			ProgramComponent = "processor"
			};
		_record = _record with
			{
			ProgramLoaded = At (83),
			Interruptions = [_record.Interruptions[0], new ("device", At (12), At (115))]
			};
		Assert.That (Assess ().Issues, Does.Contain ("functional-evidence-not-after-restoration"));
		}
	[TestCase (SubmissionEvidenceOutcome.Failed, SubmissionEvidenceOutcome.Failed)]
	[TestCase (SubmissionEvidenceOutcome.Partial, SubmissionEvidenceOutcome.Partial)]
	[TestCase (SubmissionEvidenceOutcome.NotTested, SubmissionEvidenceOutcome.Partial)]
	[TestCase (SubmissionEvidenceOutcome.Inconclusive, SubmissionEvidenceOutcome.Partial)]
	public void FunctionalOutcomesArePreserved (SubmissionEvidenceOutcome input, SubmissionEvidenceOutcome expected)
		{
		_record = _record with
			{
			Functions = [_record.Functions[0] with { Outcome = input }, _record.Functions[1]]
			};
		Assert.That (Assess ().Outcome, Is.EqualTo (expected));
		}
	[TestCase (SubmissionEvidenceOutcome.NotApplicable)]
	[TestCase (SubmissionEvidenceOutcome.ReviewedPriorPass)]
	[TestCase ((SubmissionEvidenceOutcome)99)]
	public void CannotSubstituteAbsenceOrOldPassForFreshFunction (SubmissionEvidenceOutcome outcome)
		{
		_record = _record with
			{
			Functions = [_record.Functions[0] with { Outcome = outcome }, _record.Functions[1]]
			};
		Assert.Throws<InvalidDataException> (() => Assess ());
		}
	[Test]
	public void IncompleteScopeAndFailedRestorationAreDistinct ()
		{
		_record = _record with
			{
			Interruptions = [_record.Interruptions[0]],
			Functions = []
			};
		Assert.That (Assess ().Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Partial));
		Assert.That (Assess ().Issues, Is.EquivalentTo (new[] { "component-not-observed:device", "function-not-observed:control", "function-not-observed:feedback" }));
		_record = _record with
			{
			MatchesOriginal = false
			};
		Assert.That (Assess ().Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Failed));
		}
	[TestCase ("component")]
	[TestCase ("function")]
	[TestCase ("foreign-component")]
	[TestCase ("foreign-function")]
	[TestCase ("identity")]
	[TestCase ("schema")]
	[TestCase ("time")]
	[TestCase ("future")]
	[TestCase ("digest")]
	[TestCase ("path")]
	public void InvalidOrUnboundMeasurementsAreRejected (string variant)
		{
		_record = variant switch
			{
				"component" => _record with { Interruptions = [_record.Interruptions[0], _record.Interruptions[0]] },
				"function" => _record with { Functions = [_record.Functions[0], _record.Functions[0]] },
				"foreign-component" => _record with { Interruptions = [new ("router", At (10), At (80))] },
				"foreign-function" => _record with { Functions = [new ("ping", SubmissionEvidenceOutcome.Passed, At (110))] },
				"identity" => _record with { Identity = _record.Identity with { SourceCommit = new ('e', 40) } },
				"schema" => _record with { SchemaVersion = 2 },
				"time" => _record with { OriginalState = At (2, 1) },
				"future" => _record with { VerifiedState = At (201) },
				"digest" => _record with { VerifiedState = At (150) with { Evidence = _proof with { Sha256 = new ('f', 64) } } },
				"path" => _record with { OriginalState = At (0) with { Evidence = _proof with { RelativePath = "../outside.txt" } } },
				_ => throw new InvalidOperationException ()
				};
		Assert.Throws<InvalidDataException> (() => Assess ());
		}
	[Test]
	public void EvidenceMutationAndCancellationAreRejected ()
		{
		File.AppendAllText (Path.Combine (_root, "synthetic.txt"), "changed");
		Assert.Throws<InvalidDataException> (() => Assess ());
		Assert.Throws<OperationCanceledException> (() => SubmissionOutageMeasurements.Assess (_plan, _record, _root, Start.AddSeconds (200), new CancellationToken (true)));
		}
	[TestCase ("empty")]
	[TestCase ("duplicate")]
	[TestCase ("clock")]
	[TestCase ("processor")]
	[TestCase ("duration")]
	public void PlanMustDefineReviewedScopeAndClock (string variant)
		{
		_plan = variant switch
			{
				"empty" => _plan with { RequiredFunctions = [] },
				"duplicate" => _plan with { RequiredComponents = ["device", "device"] },
				"clock" => _plan with { RecoveryClock = (SubmissionOutageRecoveryClock)99 },
				"processor" => _plan with { RecoveryClock = SubmissionOutageRecoveryClock.ProgramLoaded },
				"duration" => _plan with { MinimumInterruption = TimeSpan.Zero },
				_ => throw new InvalidOperationException ()
				};
		Assert.Throws<InvalidDataException> (() => Assess ());
		}

	private static readonly JsonSerializerOptions Json = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
		};
	private SubmissionEvidenceFile Save (string path, object value)
		{
		var bytes = JsonSerializer.SerializeToUtf8Bytes (value, Json);
		File.WriteAllBytes (Path.Combine (_root, path), bytes);
		return new (path, Convert.ToHexStringLower (SHA256.HashData (bytes)));
		}
	private (SubmissionEvidenceFile Plan, SubmissionEvidenceFile Record, SubmissionRequirement Rule) Inputs (SubmissionRequirement? rule = null)
		{
		rule ??= new (_plan.RequirementId, TimeSpan.FromSeconds (60), false, new ("test/system", "outage", SubmissionEvidenceOutcome.Passed, 60, true));
		var policy = Save ("policy.json", new SubmissionEvidencePolicy (1, [rule]));
		_plan = _plan with
			{
			Identity = _plan.Identity with
				{
				PolicySha256 = policy.Sha256
				}
			};
		_record = _record with
			{
			Identity = _plan.Identity
			};
		return (Save ("plan.json", _plan), Save ("record.json", _record), rule);
		}
	private SubmissionOutageEvidenceResult Import (SubmissionEvidenceFile plan, SubmissionEvidenceFile record) =>
		 SubmissionOutageEvidence.ImportFiles (_root, plan.RelativePath, plan.Sha256, record.RelativePath, record.Sha256, "policy.json", Start.AddSeconds (200));
	[Test]
	public void ImportedPassSatisfiesNormalEvidenceGateAndRetainsInputs ()
		{
		var inputs = Inputs ();
		var result = Import (inputs.Plan, inputs.Record);
		var observation = result.Observations.Observations.Single ();
		Assert.That (SubmissionEvidence.Evaluate (_plan.Identity, [inputs.Rule], result.Observations.Observations, _root, Start.AddSeconds (200)).EvidenceChecksPassed, Is.True);
		Assert.That (observation.Files.Select (f => f.RelativePath), Is.EquivalentTo (new[] { "plan.json", "record.json", "policy.json", "synthetic.txt" }));
		Assert.That (observation.Execution!.Response!.TriggeredUtc, Is.EqualTo (Start.AddSeconds (80)));
		Assert.That (observation.Execution.Response.ObservedUtc, Is.EqualTo (Start.AddSeconds (112)));
		Assert.That (observation.StartedUtc, Is.EqualTo (Start));
		Assert.That (observation.FinishedUtc, Is.EqualTo (Start.AddSeconds (151)));
		}
	[Test]
	public void MissingComponentProducesPartialObservationWhichGateRejects ()
		{
		_record = _record with
			{
			Interruptions = [_record.Interruptions[0]]
			};
		var inputs = Inputs ();
		var result = Import (inputs.Plan, inputs.Record);
		Assert.That (result.Observations.Observations.Single ().Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Partial));
		Assert.That (SubmissionEvidence.Evaluate (_plan.Identity, [inputs.Rule], result.Observations.Observations, _root, Start.AddSeconds (200)).EvidenceChecksPassed, Is.False);
		}
	[TestCase ("plan")]
	[TestCase ("record")]
	[TestCase ("policy")]
	public void ModifiedPinnedInputsCannotImport (string name)
		{
		var inputs = Inputs ();
		File.AppendAllText (Path.Combine (_root, name + ".json"), " ");
		Assert.Throws<InvalidDataException> (() => Import (inputs.Plan, inputs.Record));
		}
	[TestCase ("duplicate")]
	[TestCase ("unknown")]
	[TestCase ("missing")]
	public void AmbiguousOrIncompleteJsonCannotImportEvenWithNewDigest (string variant)
		{
		var inputs = Inputs ();
		var path = Path.Combine (_root, "record.json");
		var json = File.ReadAllText (path);
		json = variant switch
			{
				"duplicate" => json.Insert (1, "\"matchesOriginal\":false,"),
				"unknown" => json.Insert (1, "\"unreviewedWaiver\":true,"),
				"missing" => json.Replace ("\"schemaVersion\":1,", "", StringComparison.Ordinal),
				_ => throw new InvalidOperationException ()
				};
		File.WriteAllText (path, json);
		var repinned = new SubmissionEvidenceFile ("record.json", Convert.ToHexStringLower (SHA256.HashData (File.ReadAllBytes (path))));
		Assert.Throws<JsonException> (() => Import (inputs.Plan, repinned));
		}
	[TestCase ("duration")]
	[TestCase ("response")]
	[TestCase ("method")]
	[TestCase ("restoration")]
	[TestCase ("id")]
	public void PlanCannotWeakenPolicy (string variant)
		{
		var rule = new SubmissionRequirement (variant == "id" ? "another" : _plan.RequirementId,
			 TimeSpan.FromSeconds (variant == "duration" ? 61 : 60), false,
			 new ("test/system", variant == "method" ? "combined" : "outage", SubmissionEvidenceOutcome.Passed, variant == "response" ? 30 : 60, variant != "restoration"));
		var inputs = Inputs (rule);
		Assert.Throws<InvalidDataException> (() => Import (inputs.Plan, inputs.Record));
		}
	[TestCase (true, 0)]
	[TestCase (false, 1)]
	public void PublicCommandRetainsPassingAndIncompleteResultsWithoutOverwrite (bool complete, int expected)
		{
		if (!complete)
			_record = _record with
				{
				Interruptions = [_record.Interruptions[0]]
				};
		var inputs = Inputs ();
		var directory = Path.Combine (_root, "output");
		string[] args = ["--evidence",_root,"--plan","plan.json","--plan-sha256",inputs.Plan.Sha256,
				"--record","record.json","--record-sha256",inputs.Record.Sha256,"--policy","policy.json","--output",directory];
		using var output = new StringWriter ();
		using var error = new StringWriter ();
		Assert.That (SubmissionOutageEvidenceCommand.Run (args, output, error), Is.EqualTo (expected), error.ToString ());
		Assert.That (File.Exists (Path.Combine (directory, "report.json")), Is.True);
		var retained = JsonSerializer.Deserialize<SubmissionEvidenceDocument> (File.ReadAllText (Path.Combine (directory, "observations.json")), Json)!;
		Assert.That (retained.Observations.Single ().Outcome, Is.EqualTo (complete ? SubmissionEvidenceOutcome.Passed : SubmissionEvidenceOutcome.Partial));
		var before = File.ReadAllBytes (Path.Combine (directory, "observations.json"));
		Assert.That (SubmissionOutageEvidenceCommand.Run (args, output, error), Is.EqualTo (2));
		Assert.That (File.ReadAllBytes (Path.Combine (directory, "observations.json")), Is.EqualTo (before));
		using var summary = JsonDocument.Parse (output.ToString ());
		Assert.That (summary.RootElement.GetProperty ("submissionReady").GetBoolean (), Is.False);
		}
	}
