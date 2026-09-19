// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionReviewAssessmentTests
	{
	private string _root = null!;
	private SubmissionEvidenceFile _file = null!;
	private static readonly DateTimeOffset Now = new (2026, 9, 19, 10, 0, 0, TimeSpan.Zero);
	private static SubmissionEvidenceIdentity Identity => new (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64));
	private static SubmissionRequirement[] Policy => [new ("controls", TimeSpan.Zero), new ("endurance", TimeSpan.FromHours (24))];
	private SubmissionObservation Passing => new ("controls", Identity, SubmissionEvidenceOutcome.Passed, Now.AddMinutes (-1), Now, [_file]);
	private SubmissionObservation Endurance => Passing with { RequirementId = "endurance", StartedUtc = Now.AddHours (-24) };

	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "review-assessment-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		var bytes = "Synthetic evidence only"u8.ToArray ();
		File.WriteAllBytes (Path.Combine (_root, "measurement.txt"), bytes);
		_file = new ("measurement.txt", Convert.ToHexString (SHA256.HashData (bytes)));
		}

	[TearDown]
	public void TearDown () => Directory.Delete (_root, true);

	private SubmissionReviewAssessmentReport Assess (SubmissionObservation[] observations,
		SubmissionGapDeclaration[]? gaps = null, SubmissionReviewMode mode = SubmissionReviewMode.DeclaredGaps) =>
		SubmissionReviewAssessment.Assess (Identity, Policy, observations, _root, mode, gaps ?? [], Now);

	[Test]
	public void CompleteRequiresEveryRequirementAndDoesNotCreateVendorDecision ()
		{
		var report = Assess ([Passing, Endurance], mode: SubmissionReviewMode.Complete);
		Assert.That (report.VerificationStatus, Is.EqualTo (SubmissionVerificationStatus.CompleteAgainstInterpretedRequirements));
		Assert.That (report.ReadyForReview, Is.True);
		Assert.That (report.Evidence.EvidenceChecksPassed, Is.True);
		Assert.That (report.Requirements.All (row => row.Status == SubmissionRequirementReviewStatus.VerifiedAgainstPlan), Is.True);
		Assert.That (Assess ([Passing], mode: SubmissionReviewMode.Complete).ReadyForReview, Is.False);
		}

	[TestCase (SubmissionEvidenceOutcome.Failed)]
	[TestCase (SubmissionEvidenceOutcome.Partial)]
	[TestCase (SubmissionEvidenceOutcome.Inconclusive)]
	[TestCase (SubmissionEvidenceOutcome.NotTested)]
	public void ExplainedFailurePreservesOriginalOutcomeAndValidationFailure (SubmissionEvidenceOutcome outcome)
		{
		var original = Passing with { Outcome = outcome };
		var report = Assess ([original, Endurance], [new ("controls", "Equipment cannot be made available for further testing.")]);
		Assert.That (report.VerificationStatus, Is.EqualTo (SubmissionVerificationStatus.GapsDeclared));
		Assert.That (report.ReadyForReview, Is.True);
		Assert.That (report.Evidence.EvidenceChecksPassed, Is.False);
		Assert.That (report.Evidence.Issues.Select (issue => issue.Code), Does.Contain ("not-passed"));
		Assert.That (report.Requirements[0].ObservedOutcome, Is.EqualTo (outcome));
		Assert.That (report.Requirements[0].Status, Is.EqualTo (SubmissionRequirementReviewStatus.GapDeclared));
		Assert.That (original.Outcome, Is.EqualTo (outcome));
		}

	[Test]
	public void MissingAndShortEnduranceRemainGapsInsteadOfCompletion ()
		{
		foreach (var observations in new[] { new[] { Passing }, new[] { Passing, Endurance with { StartedUtc = Now.AddHours (-12) } } })
			{
			var report = Assess (observations, [new ("endurance", "Developer declined the remaining observation period.")]);
			Assert.That (report.VerificationStatus, Is.EqualTo (SubmissionVerificationStatus.GapsDeclared));
			Assert.That (report.Evidence.EvidenceChecksPassed, Is.False);
			Assert.That (report.Requirements[1].Status, Is.EqualTo (SubmissionRequirementReviewStatus.GapDeclared));
			}
		}

	[Test]
	public void DeclaredListCannotHideAnotherMissingRequirement ()
		{
		var report = Assess ([], [new ("endurance", "Observation time unavailable.")]);
		Assert.That (report.ReadyForReview, Is.False);
		Assert.That (report.Requirements, Has.Count.EqualTo (2));
		Assert.That (report.Requirements[0].Status, Is.EqualTo (SubmissionRequirementReviewStatus.GapUndeclared));
		Assert.That (report.BlockingIssues.Select (issue => issue.RequirementId), Does.Contain ("controls"));
		}

	[Test]
	public void NoMeasurementsCanBeHonestlyDeclaredWithoutFabricatingObservations ()
		{
		var report = Assess ([], [new ("controls", "Equipment unavailable."), new ("endurance", "Equipment unavailable.")]);
		Assert.That (report.VerificationStatus, Is.EqualTo (SubmissionVerificationStatus.GapsDeclared));
		Assert.That (report.Requirements.All (row => row.ObservedOutcome == null), Is.True);
		Assert.That (report.Evidence.EvidenceChecksPassed, Is.False);
		}

	[TestCase ("unknown")]
	[TestCase ("duplicate")]
	[TestCase ("blank")]
	[TestCase ("stale")]
	[TestCase ("complete-mode")]
	public void InvalidDeclarationsRequireCorrection (string kind)
		{
		SubmissionGapDeclaration[] declarations = kind switch
			{
				"unknown" => [new ("omitted-policy-item", "Not tested.")],
				"duplicate" => [new ("endurance", "One."), new ("endurance", "Two.")],
				"blank" => [new ("endurance", " ")],
				"stale" => [new ("controls", "No longer matches the passing evidence.")],
				_ => [new ("endurance", "Not tested.")]
				};
		Assert.Throws<ArgumentException> (() => Assess ([Passing], declarations,
			kind == "complete-mode" ? SubmissionReviewMode.Complete : SubmissionReviewMode.DeclaredGaps));
		}

	[TestCase ("candidate", "identity-mismatch")]
	[TestCase ("digest", "evidence-digest")]
	[TestCase ("duplicate", "duplicate-observation")]
	[TestCase ("time", "invalid-time")]
	[TestCase ("enum", "invalid-outcome")]
	[TestCase ("not-applicable", "invalid-not-applicable")]
	[TestCase ("missing-file", "invalid-evidence-file")]
	public void DeclaringGapCannotWaiveUntrustworthyEvidence (string kind, string expected)
		{
		var observation = kind switch
			{
				"candidate" => Passing with { Identity = Identity with { PackageSha256 = new ('e', 64) } },
				"time" => Passing with { FinishedUtc = Now.AddDays (1) },
				"enum" => Passing with { Outcome = (SubmissionEvidenceOutcome)99 },
				"not-applicable" => Passing with { Outcome = SubmissionEvidenceOutcome.NotApplicable, Rationale = "Unable to test." },
				_ => Passing
				};
		if (kind == "digest") File.WriteAllText (Path.Combine (_root, _file.RelativePath), "Altered after capture");
		if (kind == "missing-file") File.Delete (Path.Combine (_root, _file.RelativePath));
		SubmissionObservation[] observations = kind == "duplicate" ? [observation, observation] : [observation];
		var report = Assess (observations, [new ("controls", "Attempt to waive problem."), new ("endurance", "Not tested.")]);
		Assert.That (report.ReadyForReview, Is.False);
		Assert.That (report.BlockingIssues.Select (issue => issue.Code), Does.Contain (expected));
		Assert.That (report.Requirements[0].Status, Is.EqualTo (SubmissionRequirementReviewStatus.InvalidEvidence));
		}

	[Test]
	public void HonestLackOfMeasurementsNeverSupportsCompletedCheckbox ()
		{
		var report = Assess ([Passing with { Files = [] }, Endurance], [new ("controls", "Capture was not retained.")]);
		Assert.That (report.ReadyForReview, Is.True);
		Assert.That (report.Requirements[0].ObservedOutcome, Is.EqualTo (SubmissionEvidenceOutcome.Passed));
		Assert.That (report.Requirements[0].Status, Is.EqualTo (SubmissionRequirementReviewStatus.GapDeclared));
		Assert.That (report.VerificationStatus, Is.EqualTo (SubmissionVerificationStatus.GapsDeclared));
		}

	[Test]
	public void AdditionalUnapprovedScopeBlocksEvenWhenAllDeclaredScopesPass ()
		{
		var report = Assess ([Passing, Endurance, Passing with { RequirementId = "not-in-policy" }]);
		Assert.That (report.ReadyForReview, Is.False);
		Assert.That (report.BlockingIssues.Select (issue => issue.Code), Does.Contain ("unknown-requirement"));
		}

	[Test]
	public void ReviewedNonApplicabilityRemainsSeparateFromSkipped ()
		{
		var observation = Passing with { Outcome = SubmissionEvidenceOutcome.NotApplicable, Rationale = "Driver has no such control.", Files = [] };
		var report = SubmissionReviewAssessment.Assess (Identity, [Policy[0] with { AllowNotApplicable = true }], [observation],
			_root, SubmissionReviewMode.Complete, [], Now);
		Assert.That (report.VerificationStatus, Is.EqualTo (SubmissionVerificationStatus.CompleteAgainstInterpretedRequirements));
		Assert.That (report.Requirements[0].ObservedOutcome, Is.EqualTo (SubmissionEvidenceOutcome.NotApplicable));
		}
	}