// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionEvidenceTests
	{
	private string _directory = null!;
	private SubmissionEvidenceFile _file = null!;
	private static readonly DateTimeOffset Now = new (2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
	private static SubmissionEvidenceIdentity Identity => new (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64));
	private SubmissionObservation Passing => new ("ui.navigation", Identity, SubmissionEvidenceOutcome.Passed, Now.AddMinutes (-1), Now, [_file]);

	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (TestContext.CurrentContext.WorkDirectory, "submission-evidence-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		var bytes = "Retained observation fixture"u8.ToArray ();
		File.WriteAllBytes (Path.Combine (_directory, "result.txt"), bytes);
		_file = new ("result.txt", Convert.ToHexString (SHA256.HashData (bytes)));
		}

	[TearDown]
	public void TearDown () => Directory.Delete (_directory, true);

	private SubmissionEvidenceReport Evaluate (params SubmissionObservation[] observations) =>
		SubmissionEvidence.Evaluate (Identity, [new ("ui.navigation", TimeSpan.Zero)], observations, _directory, Now);

	[Test]
	public void CompleteBoundEvidencePasses () => Assert.That (Evaluate (Passing).EvidenceChecksPassed, Is.True);

	[Test]
	public void EmptyRunDoesNotPass () => Assert.That (Evaluate ().Issues.Select (issue => issue.Code), Does.Contain ("missing-observation"));

	[TestCase (SubmissionEvidenceOutcome.NotTested)]
	[TestCase (SubmissionEvidenceOutcome.Failed)]
	[TestCase (SubmissionEvidenceOutcome.Partial)]
	[TestCase (SubmissionEvidenceOutcome.Inconclusive)]
	[TestCase ((SubmissionEvidenceOutcome)99)]
	public void NonpassingOutcomeCannotCompleteRequirement (SubmissionEvidenceOutcome outcome) =>
		Assert.That (Evaluate (Passing with
			{
			Outcome = outcome
			}).Issues.Select (issue => issue.Code), Does.Contain ("not-passed"));

	[TestCase ("package")]
	[TestCase ("commit")]
	[TestCase ("policy")]
	[TestCase ("template")]
	public void EvidenceFromAnotherCandidateOrPolicyIsRejected (string changed)
		{
		var identity = changed switch
			{
				"package" => Identity with { PackageSha256 = new ('e', 64) },
				"commit" => Identity with { SourceCommit = new ('e', 40) },
				"policy" => Identity with { PolicySha256 = new ('e', 64) },
				_ => Identity with { TemplateSha256 = new ('e', 64) }
				};
		Assert.That (Evaluate (Passing with
			{
			Identity = identity
			}).Issues.Select (issue => issue.Code), Does.Contain ("identity-mismatch"));
		}

	[Test]
	public void CannotChoosePassOverFailureForSameRequirement () =>
		Assert.That (Evaluate (Passing, Passing with
			{
			Outcome = SubmissionEvidenceOutcome.Failed
			}).Issues.Select (issue => issue.Code),
			Is.SupersetOf (new[] { "duplicate-observation", "not-passed" }));

	[Test]
	public void UnknownRequirementDoesNotFillMissingRequirement () =>
		Assert.That (Evaluate (Passing with
			{
			RequirementId = "ui.other"
			}).Issues.Select (issue => issue.Code),
			Is.SupersetOf (new[] { "missing-observation", "unknown-requirement" }));

	[Test]
	public void EnduranceNeedsRequiredElapsedTime ()
		{
		var report = SubmissionEvidence.Evaluate (Identity, [new ("ui.navigation", TimeSpan.FromHours (24))], [Passing], _directory, Now);
		Assert.That (report.Issues.Select (issue => issue.Code), Does.Contain ("insufficient-duration"));
		Assert.That (SubmissionEvidence.Evaluate (Identity, [new ("ui.navigation", TimeSpan.FromHours (24))],
			[Passing with { StartedUtc = Now.AddHours (-24) }], _directory, Now).EvidenceChecksPassed, Is.True);
		}

	[TestCase ("missing")]
	[TestCase ("reversed")]
	[TestCase ("future")]
	public void InvalidTimestampsAreRejected (string kind)
		{
		var observation = kind switch
			{
				"missing" => Passing with { StartedUtc = default },
				"reversed" => Passing with { StartedUtc = Now, FinishedUtc = Now.AddSeconds (-1) },
				_ => Passing with { FinishedUtc = Now.AddSeconds (1) }
				};
		Assert.That (Evaluate (observation).Issues.Select (issue => issue.Code), Does.Contain ("invalid-time"));
		}

	[Test]
	public void NonApplicabilityNeedsPolicyPermissionAndReason ()
		{
		var observation = Passing with
			{
			Outcome = SubmissionEvidenceOutcome.NotApplicable,
			Files = [],
			Rationale = "This driver has no navigation pad."
			};
		Assert.That (Evaluate (observation).EvidenceChecksPassed, Is.False);
		Assert.That (SubmissionEvidence.Evaluate (Identity, [new ("ui.navigation", TimeSpan.Zero, true)], [observation], _directory, Now).EvidenceChecksPassed, Is.True);
		Assert.That (SubmissionEvidence.Evaluate (Identity, [new ("ui.navigation", TimeSpan.Zero, true)], [observation with { Rationale = "" }], _directory, Now).EvidenceChecksPassed, Is.False);
		}

	[Test]
	public void ChangedEvidenceIsRejected ()
		{
		File.WriteAllText (Path.Combine (_directory, _file.RelativePath), "Changed after capture");
		Assert.That (Evaluate (Passing).Issues.Select (issue => issue.Code), Does.Contain ("evidence-digest"));
		}

	[Test]
	public void MissingEvidenceIsRejected ()
		{
		File.Delete (Path.Combine (_directory, _file.RelativePath));
		Assert.That (Evaluate (Passing).EvidenceChecksPassed, Is.False);
		}

	[Test]
	public void ClaimedPassWithoutRetainedProofIsRejected () =>
		Assert.That (Evaluate (Passing with
			{
			Files = []
			}).Issues.Select (issue => issue.Code), Does.Contain ("missing-evidence-file"));

	[TestCase ("../outside.txt")]
	[TestCase ("sub/../../outside.txt")]
	[TestCase ("/outside.txt")]
	[TestCase ("C:\\outside.txt")]
	[TestCase ("result.txt:stream")]
	[TestCase ("sub\\..\\result.txt")]
	[TestCase (".. /outside.txt")]
	[TestCase (".../outside.txt")]
	public void EvidenceCannotEscapeItsDirectory (string path) =>
		Assert.That (Evaluate (Passing with
			{
			Files = [_file with { RelativePath = path }]
			}).Issues.Select (issue => issue.Code), Does.Contain ("invalid-evidence-file"));

	[Test]
	public void EmptyOrDuplicatePolicyIsRejected ()
		{
		Assert.Throws<ArgumentException> (() => SubmissionEvidence.Evaluate (Identity, [], [Passing], _directory, Now));
		Assert.Throws<ArgumentException> (() => SubmissionEvidence.Evaluate (Identity,
			[new ("same", TimeSpan.Zero), new ("same", TimeSpan.Zero)], [], _directory, Now));
		}

	[Test]
	public void CancellationStopsEvaluation ()
		{
		using var cancellation = new CancellationTokenSource ();
		cancellation.Cancel ();
		Assert.Throws<OperationCanceledException> (() => SubmissionEvidence.Evaluate (Identity,
			[new ("ui.navigation", TimeSpan.Zero)], [Passing], _directory, Now, cancellation.Token));
		}
	}