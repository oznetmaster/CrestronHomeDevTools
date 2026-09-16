// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionExecutionTests
	{
	private string _directory = null!;
	private SubmissionObservation _observation = null!;
	private static readonly DateTimeOffset End = new (2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
	private static readonly SubmissionEvidenceIdentity Identity = new (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64));
	private static SubmissionExecutionRequirements Contract => new ("room/Main/Boost", "android", SubmissionEvidenceOutcome.Passed, 2, true);
	private static SubmissionExecutionObservation Measurements => new (Contract.Target, Contract.Method,
		new (End.AddSeconds (-50), End.AddSeconds (-48), "trace.txt", "trace.txt"),
		new (End.AddMinutes (-1), End.AddSeconds (-50), End.AddSeconds (-10), End, true, "trace.txt", "trace.txt"));

	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (TestContext.CurrentContext.WorkDirectory, "execution-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		var data = "Synthetic structural measurement fixture; not hardware evidence"u8.ToArray ();
		File.WriteAllBytes (Path.Combine (_directory, "trace.txt"), data);
		_observation = new ("scope", Identity, SubmissionEvidenceOutcome.Passed, End.AddMinutes (-1), End,
			[new ("trace.txt", Convert.ToHexString (SHA256.HashData (data)))], Execution: Measurements);
		}

	[TearDown]
	public void TearDown () => Directory.Delete (_directory, true);

	private SubmissionEvidenceReport Evaluate (SubmissionObservation? observation = null, SubmissionExecutionRequirements? contract = null) =>
		SubmissionEvidence.Evaluate (Identity, [new ("scope", TimeSpan.Zero, true, contract ?? Contract)],
			[observation ?? _observation], _directory, End);

	[Test]
	public void CompleteMeasurementsWithExactDeadlinePass () => Assert.That (Evaluate ().EvidenceChecksPassed, Is.True);

	[TestCase ("missing", "execution-missing")]
	[TestCase ("target", "execution-scope")]
	[TestCase ("method", "execution-scope")]
	[TestCase ("response", "response-missing")]
	[TestCase ("late", "response-deadline")]
	[TestCase ("reversed-response", "response-time")]
	[TestCase ("outside-response", "response-time")]
	[TestCase ("response-file", "response-evidence")]
	[TestCase ("restoration", "restoration-missing")]
	[TestCase ("unconfirmed", "restoration-unconfirmed")]
	[TestCase ("capture-after-action", "restoration-time")]
	[TestCase ("verify-before-restore", "restoration-time")]
	[TestCase ("restoration-file", "restoration-evidence")]
	public void ClaimedPassCannotBypassMeasurements (string defect, string code)
		{
		var execution = defect switch
			{
				"missing" => null,
				"target" => Measurements with { Target = "another-room/Main/Boost" },
				"method" => Measurements with { Method = "configuration" },
				"response" => Measurements with { Response = null },
				"late" => Measurements with { Response = Measurements.Response! with { ObservedUtc = End.AddSeconds (-47.999) } },
				"reversed-response" => Measurements with { Response = Measurements.Response! with { ObservedUtc = End.AddSeconds (-51) } },
				"outside-response" => Measurements with { Response = Measurements.Response! with { TriggeredUtc = End.AddHours (-1) } },
				"response-file" => Measurements with { Response = Measurements.Response! with { ResponseEvidence = "unretained.txt" } },
				"restoration" => Measurements with { Restoration = null },
				"unconfirmed" => Measurements with { Restoration = Measurements.Restoration! with { MatchesOriginal = false } },
				"capture-after-action" => Measurements with { Restoration = Measurements.Restoration! with { OriginalCapturedUtc = End.AddSeconds (-49) } },
				"verify-before-restore" => Measurements with { Restoration = Measurements.Restoration! with { VerifiedUtc = End.AddSeconds (-11) } },
				_ => Measurements with { Restoration = Measurements.Restoration! with { VerificationEvidence = "unretained.txt" } }
				};
		var report = Evaluate (_observation with { Execution = execution });
		Assert.That (report.EvidenceChecksPassed, Is.False);
		Assert.That (report.Issues.Select (issue => issue.Code), Does.Contain (code));
		}

	[Test]
	public void AbsenceRequiresNonApplicabilityAndRetainedProof ()
		{
		var contract = Contract with { Method = "absence", RequiredOutcome = SubmissionEvidenceOutcome.NotApplicable, Restore = false, ResponseLimitSeconds = null };
		var observation = _observation with { Outcome = SubmissionEvidenceOutcome.NotApplicable, Rationale = "Reviewed absence of this control.", Execution = new (contract.Target, contract.Method) };
		Assert.That (Evaluate (observation, contract).EvidenceChecksPassed, Is.True);
		Assert.That (Evaluate (observation with { Outcome = SubmissionEvidenceOutcome.Passed }, contract).Issues.Select (i => i.Code), Does.Contain ("execution-outcome"));
		Assert.That (Evaluate (observation with { Files = [] }, contract).Issues.Select (i => i.Code), Does.Contain ("execution-files"));
		}

	[TestCase ("missing-budget", "sampling-policy")]
	[TestCase ("missing", "samples-missing")]
	[TestCase ("gap", "sample-gap")]
	[TestCase ("first", "sample-coverage")]
	[TestCase ("last", "sample-coverage")]
	[TestCase ("duplicate", "sample-time")]
	[TestCase ("failed", "sample-result")]
	[TestCase ("file", "sample-result")]
	[TestCase ("null", "sample-invalid")]
	public void EnduranceCannotPassWithElapsedTimeAlone (string defect, string code)
		{
		var contract = Contract with { Method = "endurance", ResponseLimitSeconds = null, Restore = false, MaximumSampleGapSeconds = 30 };
		var samples = new List<SubmissionFunctionalSample>
			{
			new (_observation.StartedUtc, SubmissionEvidenceOutcome.Passed, "trace.txt"),
			new (End.AddSeconds (-30), SubmissionEvidenceOutcome.Passed, "trace.txt"),
			new (End, SubmissionEvidenceOutcome.Passed, "trace.txt")
			};
		var execution = new SubmissionExecutionObservation (contract.Target, contract.Method, Samples: samples);
		Assert.That (Evaluate (_observation with { Execution = execution }, contract).EvidenceChecksPassed, Is.True);
		switch (defect)
			{
			case "missing-budget": contract = contract with { MaximumSampleGapSeconds = null }; break;
			case "missing": samples.Clear (); break;
			case "gap": samples.RemoveAt (1); break;
			case "first": samples.RemoveAt (0); break;
			case "last": samples.RemoveAt (2); break;
			case "duplicate": samples.Insert (1, samples[0]); break;
			case "failed": samples[1] = samples[1] with { Outcome = SubmissionEvidenceOutcome.Failed }; break;
			case "file": samples[1] = samples[1] with { Evidence = "not-retained.txt" }; break;
			case "null": samples[1] = null!; break;
			}
		var report = Evaluate (_observation with { Execution = execution }, contract);
		Assert.That (report.EvidenceChecksPassed, Is.False);
		Assert.That (report.Issues.Select (i => i.Code), Does.Contain (code));
		}

	[Test]
	public void ReferencedMeasurementFileStillRequiresMatchingHash ()
		{
		File.WriteAllText (Path.Combine (_directory, "trace.txt"), "Changed after observation");
		Assert.That (Evaluate ().Issues.Select (i => i.Code), Does.Contain ("evidence-digest"));
		}
	}