// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionEvidenceCompositionTests
	{
	private string _root = null!, _pin = null!;
	private SubmissionEvidenceCompositionPlan _plan = null!;
	private SubmissionEvidencePolicy _policy = null!;
	private SubmissionEvidenceDocument _first = null!, _second = null!;
	private static readonly DateTimeOffset Now = new (2026, 9, 19, 3, 0, 0, TimeSpan.Zero);
	private static readonly JsonSerializerOptions Json = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
		};
	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "compose-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		_policy = new (1, [new ("first", TimeSpan.Zero, false, new ("first-control", "android", SubmissionEvidenceOutcome.Passed, null, false)),
			new ("second", TimeSpan.Zero, false, new ("second-control", "combined", SubmissionEvidenceOutcome.Passed, null, false))]);
		var identity = new SubmissionEvidenceIdentity (new ('a', 64), new ('b', 40), Write ("policy.json", _policy), new ('c', 64));
		var file = new SubmissionEvidenceFile ("measurement.json", Write ("measurement.json", new { Synthetic = true }));
		_first = new (1, [new ("first", identity, SubmissionEvidenceOutcome.Passed, Now.AddSeconds (-5), Now, [file], "Synthetic assertion",
			new ("first-control", "android"))]);
		_second = new (1, [_first.Observations[0] with { RequirementId = "second", Execution = new ("second-control", "combined") }]);
		_plan = new (1, identity, [new ("first.json", Write ("first.json", _first)), new ("second.json", Write ("second.json", _second))]);
		Pin ();
		}
	[TearDown]
	public void TearDown () => Directory.Delete (_root, true);
	private string Write (string name, object value)
		{
		var bytes = JsonSerializer.SerializeToUtf8Bytes (value, Json);
		File.WriteAllBytes (Path.Combine (_root, name), bytes);
		return Convert.ToHexStringLower (SHA256.HashData (bytes));
		}
	private void Pin () => _pin = Write ("plan.json", _plan);
	private void ChangeFirst (Func<SubmissionObservation, SubmissionObservation> change)
		{
		_first = _first with { Observations = [change (_first.Observations[0])] };
		_plan = _plan with { Sources = [_plan.Sources[0] with { Sha256 = Write ("first.json", _first) }, _plan.Sources[1]] };
		Pin ();
		}
	private SubmissionEvidenceCompositionReport Combine () => SubmissionEvidenceComposition.CombineFiles (_root, "plan.json", _pin, "policy.json", Now);
	private string[] Arguments (string output) => ["--evidence", _root, "--plan", "plan.json", "--plan-sha256", _pin, "--policy", "policy.json", "--output", output];

	[Test]
	public void CompleteCompositionPreservesMeasurementsAndRetainsSourceDocuments ()
		{
		var before = File.ReadAllBytes (Path.Combine (_root, "first.json"));
		var result = Combine ();
		Assert.That (result.CompositionChecksPassed, Is.True);
		Assert.That (result.Observations.Observations.Count, Is.EqualTo (2));
		var first = result.Observations.Observations[0];
		Assert.That (first with { Files = _first.Observations[0].Files }, Is.EqualTo (_first.Observations[0]));
		Assert.That (first.Files.Select (file => file.RelativePath), Does.Contain ("first.json").And.Contain ("plan.json").And.Contain ("policy.json"));
		Assert.That (File.ReadAllBytes (Path.Combine (_root, "first.json")), Is.EqualTo (before));
		Assert.That (SubmissionEvidence.Evaluate (_plan.Identity, _policy.Requirements, result.Observations.Observations, _root, Now).EvidenceChecksPassed, Is.True);
		}

	[Test]
	public void MissingPhaseRemainsIncomplete ()
		{
		_plan = _plan with { Sources = [_plan.Sources[0]] }; Pin ();
		Assert.That (Combine ().Evidence.Issues.Select (issue => issue.Code), Does.Contain ("missing-observation"));
		}

	[TestCase (SubmissionEvidenceOutcome.Failed), TestCase (SubmissionEvidenceOutcome.Partial), TestCase (SubmissionEvidenceOutcome.Inconclusive), TestCase (SubmissionEvidenceOutcome.NotTested)]
	public void NonpassingOriginalOutcomeIsNeverPromoted (SubmissionEvidenceOutcome outcome)
		{
		ChangeFirst (item => item with { Outcome = outcome });
		var result = Combine ();
		Assert.That (result.CompositionChecksPassed, Is.False);
		Assert.That (result.Observations.Observations[0].Outcome, Is.EqualTo (outcome));
		}

	[TestCase (false), TestCase (true)]
	public void DuplicateScopesAreNeverChosenOrCollapsed (bool failed)
		{
		_second = new (1, [_first.Observations[0] with { Outcome = failed ? SubmissionEvidenceOutcome.Failed : SubmissionEvidenceOutcome.Passed }]);
		_plan = _plan with { Sources = [_plan.Sources[0], _plan.Sources[1] with { Sha256 = Write ("second.json", _second) }] }; Pin ();
		var result = Combine ();
		Assert.That (result.CompositionChecksPassed, Is.False);
		Assert.That (result.Observations.Observations.Count, Is.EqualTo (2));
		Assert.That (result.Evidence.Issues.Select (issue => issue.Code), Does.Contain ("duplicate-observation"));
		}

	[TestCase ("candidate"), TestCase ("scope"), TestCase ("files"), TestCase ("time")]
	public void InvalidSourceCannotBeRepairedByCompositionProvenance (string change)
		{
		ChangeFirst (item => change switch
			{
			"candidate" => item with { Identity = item.Identity with { PackageSha256 = new ('d', 64) } },
			"scope" => item with { Execution = item.Execution! with { Target = "another-control" } },
			"files" => item with { Files = [] },
			_ => item with { FinishedUtc = Now.AddSeconds (1) }
			});
		var result = Combine ();
		Assert.That (result.CompositionChecksPassed, Is.False);
		Assert.That (SubmissionEvidence.Evaluate (_plan.Identity, _policy.Requirements, result.Observations.Observations, _root, Now).EvidenceChecksPassed, Is.False);
		}

	[TestCase ("plan.json"), TestCase ("policy.json"), TestCase ("first.json")]
	public void ChangedPinnedInputsAreRejected (string file)
		{
		File.AppendAllText (Path.Combine (_root, file), " ");
		Assert.Throws<InvalidDataException> (() => Combine ());
		}

	[TestCase ("../outside.json"), TestCase ("C:/outside.json")]
	public void EscapingSourcePathsAreRejected (string path)
		{
		_plan = _plan with { Sources = [new (path, new ('a', 64))] }; Pin ();
		Assert.Throws<ArgumentException> (() => Combine ());
		}

	[Test]
	public void DuplicateSourceInventoryIsRejected ()
		{
		_plan = _plan with { Sources = [_plan.Sources[0], _plan.Sources[0]] }; Pin ();
		Assert.Throws<ArgumentException> (() => Combine ());
		}

	[Test]
	public void ChangedMeasurementRemainsInvalid ()
		{
		File.AppendAllText (Path.Combine (_root, "measurement.json"), " ");
		Assert.That (Combine ().Evidence.Issues.Select (issue => issue.Code), Does.Contain ("evidence-digest"));
		}

	[Test]
	public void CliWritesCompleteDocumentAndRefusesOverwrite ()
		{
		string output = Path.Combine (_root, "result");
		Assert.That (SubmissionEvidenceCompositionCommand.Run (Arguments (output), TextWriter.Null, TextWriter.Null), Is.Zero);
		Assert.That (File.Exists (Path.Combine (output, "observations.json")), Is.True);
		Assert.That (SubmissionEvidenceCompositionCommand.Run (Arguments (output), TextWriter.Null, TextWriter.Null), Is.EqualTo (2));
		}

	[Test]
	public void CliKeepsFailureReportWithoutProducingAConsumableDocument ()
		{
		ChangeFirst (item => item with { Outcome = SubmissionEvidenceOutcome.Failed });
		string output = Path.Combine (_root, "result");
		Assert.That (SubmissionEvidenceCompositionCommand.Run (Arguments (output), TextWriter.Null, TextWriter.Null), Is.EqualTo (1));
		Assert.That (File.Exists (Path.Combine (output, "report.json")), Is.True);
		Assert.That (File.Exists (Path.Combine (output, "observations.json")), Is.False);
		}
	}