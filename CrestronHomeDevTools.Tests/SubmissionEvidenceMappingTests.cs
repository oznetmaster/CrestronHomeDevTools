// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionEvidenceMappingTests
	{
	private string _root = null!;
	private string _pin = null!;
	private SubmissionEvidenceMappingPlan _plan = null!;
	private SubmissionEvidenceDocument _document = null!;
	private SubmissionEvidencePolicy _destination = null!;
	private static readonly DateTimeOffset Now = new (2026, 9, 19, 3, 0, 0, TimeSpan.Zero);
	private static readonly JsonSerializerOptions Json = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
		};

	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "mapping-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		var source = new SubmissionEvidencePolicy (1, [new ("probe", TimeSpan.FromMinutes (1), false,
			new ("gateway", "endurance", SubmissionEvidenceOutcome.Passed, null, false, 60))]);
		_destination = new (1, [new ("period", TimeSpan.FromMinutes (1), false,
			new ("$endurance", "endurance", SubmissionEvidenceOutcome.Passed, null, false, 60)),
			new ("final-functions", TimeSpan.Zero, false, new ("$final", "combined", SubmissionEvidenceOutcome.Passed, null, true))]);
		var identity = new SubmissionEvidenceIdentity (new ('a', 64), new ('b', 40), Write ("source-policy.json", source), new ('c', 64));
		var target = identity with { PolicySha256 = Write ("destination-policy.json", _destination) };
		var sample = new SubmissionEvidenceFile ("sample.json", Write ("sample.json", new { Synthetic = true }));
		_document = new (1, [new ("probe", identity, SubmissionEvidenceOutcome.Passed, Now.AddMinutes (-1), Now,
			[sample], "Synthetic monitoring fixture", new ("gateway", "endurance", Samples:
			[new (Now.AddMinutes (-1), SubmissionEvidenceOutcome.Passed, sample.RelativePath), new (Now, SubmissionEvidenceOutcome.Passed, sample.RelativePath)]))]);
		_plan = new (1, identity, target, Write ("source-observations.json", _document),
			[new ("probe", "period", "gateway", "$endurance", "Synthetic scope equivalence reviewed; final checks are separate.")]);
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
	private void Pin () => _pin = Write ("mapping.json", _plan);
	private void ChangeObservation (Func<SubmissionObservation, SubmissionObservation> change)
		{
		_document = _document with { Observations = [change (_document.Observations[0])] };
		_plan = _plan with { SourceObservationsSha256 = Write ("source-observations.json", _document) };
		Pin ();
		}
	private void ChangeDestination (Func<SubmissionRequirement, SubmissionRequirement> change)
		{
		_destination = _destination with { Requirements = [change (_destination.Requirements[0]), _destination.Requirements[1]] };
		_plan = _plan with { DestinationIdentity = _plan.DestinationIdentity with { PolicySha256 = Write ("destination-policy.json", _destination) } };
		Pin ();
		}
	private SubmissionEvidenceMappingReport Map () => SubmissionEvidenceMapping.MapFiles (_root, "mapping.json", _pin,
		"source-policy.json", "source-observations.json", "destination-policy.json", Now);

	[Test]
	public void ImportRetainsOriginalFactsDocumentsAndExplicitMissingCoverage ()
		{
		var before = Directory.GetFiles (_root).ToDictionary (path => Path.GetFileName (path), File.ReadAllBytes);
		var report = Map ();
		Assert.That (report.MappingChecksPassed, Is.True);
		Assert.That (report.UnmappedRequirementIds, Is.EqualTo (new[] { "final-functions" }));
		var imported = report.Observations!.Observations.Single ();
		var original = _document.Observations.Single ();
		Assert.That (imported.Identity, Is.EqualTo (_plan.DestinationIdentity));
		Assert.That (imported.StartedUtc, Is.EqualTo (original.StartedUtc));
		Assert.That (imported.FinishedUtc, Is.EqualTo (original.FinishedUtc));
		Assert.That (imported.Outcome, Is.EqualTo (original.Outcome));
		Assert.That (imported.Execution!.Samples, Is.EqualTo (original.Execution!.Samples));
		Assert.That (imported.Execution.Restoration, Is.Null);
		Assert.That (imported.Files.Select (file => file.RelativePath), Is.EquivalentTo (new[]
			{ "sample.json", "source-policy.json", "source-observations.json", "destination-policy.json", "mapping.json" }));
		Assert.That (SubmissionEvidence.Evaluate (_plan.DestinationIdentity, _destination.Requirements,
			report.Observations.Observations, _root, Now).Issues.Select (item => item.Code), Does.Contain ("missing-observation"));
		foreach (var pair in before)
			Assert.That (File.ReadAllBytes (Path.Combine (_root, pair.Key!)), Is.EqualTo (pair.Value));
		}

	[TestCase (SubmissionEvidenceOutcome.Partial)]
	[TestCase (SubmissionEvidenceOutcome.Failed)]
	[TestCase (SubmissionEvidenceOutcome.NotTested)]
	[TestCase (SubmissionEvidenceOutcome.Inconclusive)]
	public void DoesNotUpgradeNonpassingSource (SubmissionEvidenceOutcome outcome)
		{
		ChangeObservation (item => item with { Outcome = outcome });
		var report = Map ();
		Assert.That (report.MappingChecksPassed, Is.False);
		Assert.That (report.Observations, Is.Null);
		}

	[TestCase ("duration", "insufficient-duration")]
	[TestCase ("restoration", "restoration-missing")]
	[TestCase ("gap", "sample-gap")]
	[TestCase ("cadence", "sampling-policy")]
	[TestCase ("response", "response-missing")]
	public void StrongerDestinationObligationsCannotBeInvented (string change, string issue)
		{
		ChangeDestination (item => change switch
			{
				"duration" => item with { MinimumDuration = TimeSpan.FromHours (24) },
				"restoration" => item with { Execution = item.Execution! with { Restore = true } },
				"gap" => item with { Execution = item.Execution! with { MaximumSampleGapSeconds = 30 } },
				"cadence" => item with { Execution = item.Execution! with { MaximumSampleGapSeconds = null } },
				_ => item with { Execution = item.Execution! with { ResponseLimitSeconds = 10 } }
			});
		var report = Map ();
		Assert.That (report.Observations, Is.Null);
		Assert.That (report.Destination!.Issues.Select (item => item.Code), Does.Contain (issue));
		}

	[TestCase ("package")]
	[TestCase ("source")]
	[TestCase ("template")]
	public void CannotMapAcrossCandidateIdentities (string field)
		{
		_plan = _plan with { DestinationIdentity = field switch
			{
				"package" => _plan.DestinationIdentity with { PackageSha256 = new ('f', 64) },
				"source" => _plan.DestinationIdentity with { SourceCommit = new ('f', 40) },
				_ => _plan.DestinationIdentity with { TemplateSha256 = new ('f', 64) }
			} };
		Pin ();
		Assert.Throws<ArgumentException> (() => Map ());
		}

	[TestCase ("mapping.json")]
	[TestCase ("source-policy.json")]
	[TestCase ("source-observations.json")]
	[TestCase ("destination-policy.json")]
	public void ChangedInputCannotBeImported (string file)
		{
		File.AppendAllText (Path.Combine (_root, file), " ");
		Assert.Throws<InvalidDataException> (() => Map ());
		}

	[Test]
	public void OriginalEnduranceExportIsReadWithoutRewritingItsBytes ()
		{
		var bytes = JsonSerializer.SerializeToUtf8Bytes (_document.Observations[0], new JsonSerializerOptions
			{ Converters = { new JsonStringEnumConverter (allowIntegerValues: false) } });
		File.WriteAllBytes (Path.Combine (_root, "source-observations.json"), bytes);
		_plan = _plan with { SourceFormat = "endurance-export", SourceObservationsSha256 = Convert.ToHexStringLower (SHA256.HashData (bytes)) };
		Pin ();
		Assert.That (Map ().MappingChecksPassed, Is.True);
		Assert.That (File.ReadAllBytes (Path.Combine (_root, "source-observations.json")), Is.EqualTo (bytes));
		}

	[Test]
	public void ChangedRawSampleCannotBeImported ()
		{
		File.AppendAllText (Path.Combine (_root, "sample.json"), " ");
		var report = Map ();
		Assert.That (report.Observations, Is.Null);
		Assert.That (report.Source.Issues.Select (item => item.Code), Does.Contain ("evidence-digest"));
		}

	[Test]
	public void CannotChoosePassOverFailure ()
		{
		_document = _document with { Observations = [_document.Observations[0], _document.Observations[0] with { Outcome = SubmissionEvidenceOutcome.Failed }] };
		_plan = _plan with { SourceObservationsSha256 = Write ("source-observations.json", _document) };
		Pin ();
		Assert.That (Map ().Observations, Is.Null);
		}

	[TestCase ("rationale")]
	[TestCase ("source-target")]
	[TestCase ("destination-target")]
	[TestCase ("requirement")]
	[TestCase ("duplicate")]
	public void AmbiguousOrUnreviewedBindingsRejected (string change)
		{
		var row = _plan.Requirements[0];
		_plan = _plan with { Requirements = change == "duplicate" ? [row, row] : [change switch
			{
				"rationale" => row with { Rationale = "" },
				"source-target" => row with { SourceTarget = "other" },
				"destination-target" => row with { DestinationTarget = "other" },
				_ => row with { DestinationRequirementId = "unknown" }
			}] };
		Pin ();
		Assert.Throws<ArgumentException> (() => Map ());
		}

	[Test]
	public void CannotChangeObservationMethod ()
		{
		ChangeDestination (item => item with { Execution = item.Execution! with { Method = "outage" } });
		Assert.Throws<ArgumentException> (() => Map ());
		}

	[Test]
	public void DuplicateJsonRejectedEvenWithMatchingDigest ()
		{
		var path = Path.Combine (_root, "mapping.json");
		var text = File.ReadAllText (path).Replace ("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal);
		File.WriteAllText (path, text);
		_pin = Convert.ToHexStringLower (SHA256.HashData (File.ReadAllBytes (path)));
		Assert.Throws<JsonException> (() => Map ());
		}

	[Test]
	public void CliWritesOnlyMappedSubsetAndRefusesOverwrite ()
		{
		var output = Path.Combine (_root, "mapped");
		string[] args = ["--evidence", _root, "--mapping", "mapping.json", "--mapping-sha256", _pin,
			"--source-policy", "source-policy.json", "--source-observations", "source-observations.json",
			"--destination-policy", "destination-policy.json", "--output", output];
		Assert.That (SubmissionEvidenceMappingCommand.Run (args, TextWriter.Null, TextWriter.Null), Is.Zero);
		Assert.That (File.Exists (Path.Combine (output, "observations.json")), Is.True);
		Assert.That (SubmissionEvidenceMappingCommand.Run (args, TextWriter.Null, TextWriter.Null), Is.EqualTo (2));
		}

	[Test]
	public void CliFailureRetainsReportWithoutObservations ()
		{
		ChangeDestination (item => item with { MinimumDuration = TimeSpan.FromHours (24) });
		var output = Path.Combine (_root, "failed");
		string[] args = ["--evidence", _root, "--mapping", "mapping.json", "--mapping-sha256", _pin,
			"--source-policy", "source-policy.json", "--source-observations", "source-observations.json",
			"--destination-policy", "destination-policy.json", "--output", output];
		Assert.That (SubmissionEvidenceMappingCommand.Run (args, TextWriter.Null, TextWriter.Null), Is.EqualTo (1));
		Assert.That (File.Exists (Path.Combine (output, "report.json")), Is.True);
		Assert.That (File.Exists (Path.Combine (output, "observations.json")), Is.False);
		}
	}