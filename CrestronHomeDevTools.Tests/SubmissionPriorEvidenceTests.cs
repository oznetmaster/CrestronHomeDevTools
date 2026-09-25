// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionPriorEvidenceTests
	{
	private static readonly DateTimeOffset Now = new (2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
	private static readonly JsonSerializerOptions Json = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
		};
	private string _root = null!;
	private SubmissionEvidenceIdentity _target = null!;
	private SubmissionEvidenceIdentity _source = null!;
	private SubmissionRequirement _oldRule = null!;
	private SubmissionEvidenceDocument _oldDocument = null!;
	private SubmissionEvidencePolicy _policy = null!;
	private SubmissionChangeImpactReview _review = null!;
	private SubmissionPriorEvidenceRequirements _prior = null!;

	private SubmissionEvidenceFile Write (string path, object value)
		{
		var full = Path.Combine (_root, path);
		Directory.CreateDirectory (Path.GetDirectoryName (full)!);
		File.WriteAllBytes (full, JsonSerializer.SerializeToUtf8Bytes (value, Json));
		return new (path, Convert.ToHexStringLower (SHA256.HashData (File.ReadAllBytes (full))));
		}

	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "prior-evidence-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		var proof = Write ("source/evidence/result.json", new { Purpose = "Synthetic physical observation; not hardware evidence" });
		var relative = proof with { RelativePath = "result.json" };
		_oldRule = new ("control", TimeSpan.FromSeconds (10), false,
			new ("device/control", "combined", SubmissionEvidenceOutcome.Passed, 2, true));
		var oldPolicy = Write ("source/policy.json", new SubmissionEvidencePolicy (1,
			[_oldRule, new ("other-unperformed", TimeSpan.Zero)]));
		_source = new (new ('a', 64), new ('b', 40), oldPolicy.Sha256, new ('c', 64));
		_target = new (new ('d', 64), new ('e', 40), new ('f', 64), _source.TemplateSha256);
		var original = new SubmissionObservation ("control", _source, SubmissionEvidenceOutcome.Passed,
			Now.AddHours (-2), Now.AddHours (-1), [relative], "Original synthetic execution",
			new ("device/control", "combined",
				new (Now.AddHours (-2).AddSeconds (1), Now.AddHours (-2).AddSeconds (2), "result.json", "result.json"),
				new (Now.AddHours (-2), Now.AddHours (-2).AddSeconds (1), Now.AddHours (-1).AddSeconds (-1),
					Now.AddHours (-1), true, "result.json", "result.json")));
		_oldDocument = new (1, [original, new ("other-unperformed", _source, SubmissionEvidenceOutcome.Failed,
			Now.AddHours (-2), Now.AddHours (-1), [relative], "Original unrelated failure remains retained")]);
		var oldObservations = Write ("source/observations.json", _oldDocument);
		var changeEvidence = Write ("change-analysis.json", new { Purpose = "Synthetic reviewed dependency comparison" });
		_review = new (1, _source, _target.PackageSha256, _target.SourceCommit, Now.AddMinutes (-1), "Example Reviewer",
			[new ("control", "Reviewed unchanged control and dependency behavior; other changes are covered separately.", [changeEvidence])]);
		_prior = new (_source, oldPolicy, oldObservations, "source/evidence", Write ("change-review.json", _review));
		_policy = new (1, [_oldRule with { PriorEvidence = _prior }]);
		}

	[TearDown]
	public void TearDown () => Directory.Delete (_root, true);
	private SubmissionEvidenceDocument Import () => SubmissionPriorEvidence.Import (_target, _policy, _root, Now);
	private SubmissionEvidenceReport Evaluate (SubmissionEvidenceDocument document) =>
		SubmissionEvidence.Evaluate (_target, _policy.Requirements, document.Observations, _root, Now);
	private void RepinSource (SubmissionEvidenceDocument document)
		{
		_prior = _prior with { Observations = Write ("source/observations.json", document) };
		_policy = new (1, [_oldRule with { PriorEvidence = _prior }]);
		}
	private void RepinReview (SubmissionChangeImpactReview review)
		{
		_prior = _prior with { ChangeReview = Write ("change-review.json", review) };
		_policy = new (1, [_oldRule with { PriorEvidence = _prior }]);
		}

	[Test]
	public void ExistingRequirementConstructorAndDeconstructionRemainAvailable ()
		{
		Assert.That (typeof (SubmissionRequirement).GetConstructor ([typeof (string), typeof (TimeSpan), typeof (bool), typeof (SubmissionExecutionRequirements)]), Is.Not.Null);
		var (id, duration, optional, execution) = _oldRule;
		Assert.That ((id, duration, optional, execution), Is.EqualTo ((_oldRule.Id, _oldRule.MinimumDuration, _oldRule.AllowNotApplicable, _oldRule.Execution)));
		}

	[Test]
	public void PriorPassIsExplicitAndOriginalExecutionAndUnrelatedFailureStayIntact ()
		{
		var before = File.ReadAllBytes (Path.Combine (_root, "source/observations.json"));
		var imported = Import ();
		var observation = imported.Observations.Single ();
		Assert.Multiple (() =>
			{
			Assert.That (observation.Outcome, Is.EqualTo (SubmissionEvidenceOutcome.ReviewedPriorPass));
			Assert.That (observation.StartedUtc, Is.EqualTo (_review.ReviewedUtc));
			Assert.That (observation.Execution, Is.Null);
			Assert.That (observation.Rationale, Does.Contain (_source.PackageSha256).And.Contain ("not a new execution"));
			Assert.That (File.ReadAllBytes (Path.Combine (_root, "source/observations.json")), Is.EqualTo (before));
			Assert.That (Evaluate (imported).EvidenceChecksPassed, Is.True);
			});
		var report = SubmissionReviewAssessment.Assess (_target, _policy.Requirements, imported.Observations,
			_root, SubmissionReviewMode.Complete, [], Now);
		Assert.That (report.Requirements.Single ().Status, Is.EqualTo (SubmissionRequirementReviewStatus.VerifiedPriorEvidence));
		}

	[TestCase (SubmissionEvidenceOutcome.Failed)]
	[TestCase (SubmissionEvidenceOutcome.Partial)]
	[TestCase (SubmissionEvidenceOutcome.NotTested)]
	[TestCase (SubmissionEvidenceOutcome.Inconclusive)]
	public void NonpassingOriginalCannotBePromoted (SubmissionEvidenceOutcome outcome)
		{
		RepinSource (new (1, [_oldDocument.Observations[0] with { Outcome = outcome }, _oldDocument.Observations[1]]));
		Assert.That (Import, Throws.TypeOf<InvalidDataException> ());
		}

	[TestCase (false)]
	[TestCase (true)]
	public void UnusedPolicyPermissionDoesNotCreateNestedOriginalEvidence (bool permissionOnSelectedScope)
		{
		var selected = permissionOnSelectedScope ? _oldRule with { PriorEvidence = _prior } : _oldRule;
		var other = new SubmissionRequirement ("other-unperformed", TimeSpan.Zero) with { PriorEvidence = _prior };
		var policyFile = Write ("source/policy.json", new SubmissionEvidencePolicy (1, [selected, other]));
		_source = _source with { PolicySha256 = policyFile.Sha256 };
		_oldDocument = _oldDocument with { Observations = _oldDocument.Observations.Select (o => o with { Identity = _source }).ToArray () };
		_review = _review with { SourceIdentity = _source };
		_prior = _prior with { Identity = _source, Policy = policyFile, Observations = Write ("source/observations.json", _oldDocument),
			ChangeReview = Write ("change-review.json", _review) };
		_policy = new (1, [_oldRule with { PriorEvidence = _prior }]);
		if (permissionOnSelectedScope)
			Assert.That (Import, Throws.TypeOf<InvalidDataException> ());
		else
			Assert.That (Evaluate (Import ()).EvidenceChecksPassed, Is.True);
		}

	[Test]
	public void ActualCarriedForwardObservationStillCannotBecomeAnOriginal ()
		{
		RepinSource (new (1, [_oldDocument.Observations[0] with { Outcome = SubmissionEvidenceOutcome.ReviewedPriorPass }, _oldDocument.Observations[1]]));
		Assert.That (Import, Throws.TypeOf<ArgumentException> ());
		}

	[Test]
	public void PassingLabelWithFailedResponseMeasurementCannotBePromoted ()
		{
		var original = _oldDocument.Observations[0];
		RepinSource (new (1, [original with { Execution = original.Execution! with
			{
			Response = original.Execution!.Response! with { ObservedUtc = original.StartedUtc.AddSeconds (30) }
			} }, _oldDocument.Observations[1]]));
		Assert.That (Import, Throws.TypeOf<InvalidDataException> ());
		}

	[Test]
	public void SameScopePassAndFailureCannotBeSelectedByPreference ()
		{
		RepinSource (new (1, [_oldDocument.Observations[0], _oldDocument.Observations[0] with { Outcome = SubmissionEvidenceOutcome.Failed }]));
		Assert.That (Import, Throws.TypeOf<InvalidDataException> ());
		}

	[TestCase ("duration")]
	[TestCase ("target")]
	[TestCase ("deadline")]
	[TestCase ("restore")]
	public void ChangedRequirementIsNotEquivalent (string difference)
		{
		var changed = difference switch
			{
			"duration" => _oldRule with { MinimumDuration = TimeSpan.FromDays (1) },
			"target" => _oldRule with { Execution = _oldRule.Execution! with { Target = "another/device" } },
			"deadline" => _oldRule with { Execution = _oldRule.Execution! with { ResponseLimitSeconds = 20 } },
			_ => _oldRule with { Execution = _oldRule.Execution! with { Restore = false } }
			};
		_policy = new (1, [changed with { PriorEvidence = _prior }]);
		Assert.That (Import, Throws.TypeOf<InvalidDataException> ());
		}

	[TestCase ("source")]
	[TestCase ("review")]
	[TestCase ("raw")]
	[TestCase ("analysis")]
	public void TamperingRemainsBlockingEvenInDeclaredGapsMode (string part)
		{
		var imported = Import ();
		var path = part switch { "source" => "source/observations.json", "review" => "change-review.json",
			"raw" => "source/evidence/result.json", _ => "change-analysis.json" };
		File.AppendAllText (Path.Combine (_root, path), " ");
		var report = SubmissionReviewAssessment.Assess (_target, _policy.Requirements, imported.Observations,
			_root, SubmissionReviewMode.DeclaredGaps, [new ("control", "Cannot waive corrupt evidence")], Now);
		Assert.That (report.ReadyForReview, Is.False);
		}

	[TestCase ("target")]
	[TestCase ("future")]
	[TestCase ("predates")]
	[TestCase ("scope")]
	public void ReviewMustBindActualTargetScopeAndChronology (string difference)
		{
		RepinReview (difference switch
			{
			"target" => _review with { TargetPackageSha256 = new ('1', 64) },
			"future" => _review with { ReviewedUtc = Now.AddHours (1) },
			"predates" => _review with { ReviewedUtc = Now.AddDays (-1) },
			_ => _review with { Decisions = [new ("different", "Not this scope", _review.Decisions[0].Evidence)] }
			});
		Assert.That (() => Import (), Throws.InstanceOf<Exception> ());
		}

	[Test]
	public void MissingSourceProvenanceCannotProducePortableBundle ()
		{
		var imported = Import ();
		var observation = imported.Observations.Single ();
		var changed = new SubmissionEvidenceDocument (1, [observation with
			{ Files = observation.Files.Where (file => file.RelativePath != "source/observations.json").ToArray () }]);
		Assert.That (Evaluate (changed).Issues.Select (issue => issue.Code), Does.Contain ("prior-provenance-missing"));
		}

	[Test]
	public void ExplicitPolicyOptInIsMandatory ()
		{
		var imported = Import ();
		_policy = new (1, [_oldRule]);
		Assert.That (Evaluate (imported).Issues.Select (issue => issue.Code), Does.Contain ("prior-review-not-authorized"));
		}

	[Test]
	public void PhysicalTimestampsCannotBeRelabelledAsNewRun ()
		{
		var imported = Import ();
		var observation = imported.Observations.Single () with { StartedUtc = Now.AddHours (-2), Execution = _oldDocument.Observations[0].Execution };
		Assert.That (Evaluate (new (1, [observation])).Issues.Select (issue => issue.Code), Does.Contain ("prior-review-time"));
		}
	}
