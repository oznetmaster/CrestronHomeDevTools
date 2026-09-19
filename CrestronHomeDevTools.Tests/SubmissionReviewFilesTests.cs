// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionReviewFilesTests
	{
	private string _root = null!, _candidatePin = null!, _declarationsPin = null!;
	private SubmissionCandidate _candidate = null!;
	private SubmissionGapDeclarations _declarations = null!;
	private const string PACKAGE = "ExampleDeveloper_Test_Example_IP.pkg";
	private static readonly DateTimeOffset Now = new (2026, 9, 19, 10, 0, 0, TimeSpan.Zero);
	private static readonly JsonSerializerOptions Json = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter (allowIntegerValues: false) }
		};

	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "review-files-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_root);
		using var package = SubmissionPackageTests.Package ();
		File.WriteAllBytes (PathFor (PACKAGE), package.ToArray ());
		File.WriteAllText (PathFor ("template.pdf"), "%PDF-1.7 synthetic identity fixture, not an official form");
		Write ("policy.json", new SubmissionEvidencePolicy (1, [new ("controls", TimeSpan.Zero), new ("endurance", TimeSpan.FromHours (24))]));
		_candidate = new (1, new (Digest (PACKAGE), new ('b', 40), Digest ("policy.json"), Digest ("template.pdf")),
			new ("1286a404-144e-4d77-b96b-1d1272f21c64", "1.2.003.0000", PortalSubmissionKind.NewDriver, "ExampleDeveloper", "support@example.com"));
		Write ("candidate.json", _candidate);
		_candidatePin = Digest ("candidate.json");
		Write ("observations.json", new SubmissionEvidenceDocument (1, []));
		_declarations = new (1, _candidate.Identity, SubmissionReviewMode.DeclaredGaps,
			[new ("controls", "Equipment unavailable."), new ("endurance", "Observation time unavailable.")]);
		PinDeclarations ();
		}

	[TearDown]
	public void TearDown () => Directory.Delete (_root, true);
	private string PathFor (string name) => Path.Combine (_root, name);
	private string Digest (string name) => Convert.ToHexStringLower (SHA256.HashData (File.ReadAllBytes (PathFor (name))));
	private void Write (string name, object value) => File.WriteAllBytes (PathFor (name), JsonSerializer.SerializeToUtf8Bytes (value, Json));
	private void PinDeclarations () { Write ("declarations.json", _declarations); _declarationsPin = Digest ("declarations.json"); }
	private SubmissionReviewFileReport Check (SubmissionReviewMode mode = SubmissionReviewMode.DeclaredGaps) =>
		SubmissionReviewFiles.Check (PathFor ("candidate.json"), _candidatePin, PathFor (PACKAGE), PathFor ("policy.json"),
			PathFor ("template.pdf"), PathFor ("observations.json"), _root, PathFor ("declarations.json"), _declarationsPin, mode, Now);
	private string[] Arguments () => ["--candidate", PathFor ("candidate.json"), "--candidate-sha256", _candidatePin,
		"--package", PathFor (PACKAGE), "--policy", PathFor ("policy.json"), "--template", PathFor ("template.pdf"),
		"--observations", PathFor ("observations.json"), "--evidence", _root, "--mode", "declared-gaps",
		"--declarations", PathFor ("declarations.json"), "--declarations-sha256", _declarationsPin];

	[Test]
	public void CompleteModeChecksEveryScopeAndKeepsEmptyDeclarationsBound ()
		{
		File.WriteAllText (PathFor ("trace.txt"), "Synthetic measurement");
		var observation = new SubmissionObservation ("controls", _candidate.Identity, SubmissionEvidenceOutcome.Passed,
			Now.AddHours (-24), Now, [new ("trace.txt", Digest ("trace.txt"))]);
		Write ("observations.json", new SubmissionEvidenceDocument (1, [observation, observation with { RequirementId = "endurance" }]));
		_declarations = _declarations with { Mode = SubmissionReviewMode.Complete, Declarations = [] }; PinDeclarations ();
		var report = Check (SubmissionReviewMode.Complete);
		Assert.That (report.ReadyForReview, Is.True);
		Assert.That (report.Validation.ValidationChecksPassed, Is.True);
		Assert.That (report.Assessment!.VerificationStatus, Is.EqualTo (SubmissionVerificationStatus.CompleteAgainstInterpretedRequirements));
		Write ("observations.json", new SubmissionEvidenceDocument (1, [observation]));
		Assert.That (Check (SubmissionReviewMode.Complete).ReadyForReview, Is.False);
		}

	[Test]
	public void FailedPhysicalObservationStaysFailedInConsoleReport ()
		{
		Write ("observations.json", new SubmissionEvidenceDocument (1,
			[new ("controls", _candidate.Identity, SubmissionEvidenceOutcome.Failed, Now.AddMinutes (-1), Now, [], "Synthetic failed action.")]));
		using var output = new StringWriter ();
		using var error = new StringWriter ();
		Assert.That (SubmissionReviewAssessmentCommand.Run (Arguments (), output, error), Is.Zero, error.ToString ());
		using var document = JsonDocument.Parse (output.ToString ());
		var row = document.RootElement.GetProperty ("assessment").GetProperty ("requirements")[0];
		Assert.That (row.GetProperty ("observedOutcome").GetString (), Is.EqualTo ("Failed"));
		Assert.That (row.GetProperty ("status").GetString (), Is.EqualTo ("GapDeclared"));
		Assert.That (document.RootElement.GetProperty ("validation").GetProperty ("validationChecksPassed").GetBoolean (), Is.False);
		}

	[Test]
	public void ExplicitReviewIsBoundToActualFilesWithoutChangingFailedValidation ()
		{
		var report = Check ();
		Assert.That (report.ReadyForReview, Is.True);
		Assert.That (report.Validation.ValidationChecksPassed, Is.False);
		Assert.That (report.Assessment!.VerificationStatus, Is.EqualTo (SubmissionVerificationStatus.GapsDeclared));
		Assert.That (report.DeclarationsSha256, Is.EqualTo (_declarationsPin));
		Assert.That (report.Validation.CandidateSha256, Is.EqualTo (_candidatePin));
		Assert.That (report.Validation.ObservationsSha256, Is.EqualTo (Digest ("observations.json")));
		Assert.That (report.Assessment.Requirements, Has.Count.EqualTo (2));
		}

	[TestCase ("candidate.json")]
	[TestCase (PACKAGE)]
	[TestCase ("policy.json")]
	[TestCase ("template.pdf")]
	public void ChangedCandidateInputCannotBeWaived (string name)
		{
		File.AppendAllText (PathFor (name), " ");
		var report = Check ();
		Assert.That (report.ReadyForReview, Is.False);
		Assert.That (report.Assessment, Is.Null);
		Assert.That (report.Validation.Issues, Is.Not.Empty);
		}

	[Test]
	public void ChangedGapExplanationInvalidatesItsIndependentPin ()
		{
		Write ("declarations.json", _declarations with { Declarations = [new ("controls", "A different decision.")] });
		Assert.Throws<InvalidDataException> (() => Check ());
		}

	[TestCase ("package")]
	[TestCase ("commit")]
	[TestCase ("policy")]
	[TestCase ("template")]
	public void DeclarationsCannotMoveToAnotherReleaseIdentity (string field)
		{
		var identity = field switch
			{
				"package" => _candidate.Identity with { PackageSha256 = new ('e', 64) },
				"commit" => _candidate.Identity with { SourceCommit = new ('e', 40) },
				"policy" => _candidate.Identity with { PolicySha256 = new ('e', 64) },
				_ => _candidate.Identity with { TemplateSha256 = new ('e', 64) }
				};
		_declarations = _declarations with { Identity = identity }; PinDeclarations ();
		Assert.Throws<InvalidDataException> (() => Check ());
		}

	[Test]
	public void CompleteSelectionCannotSilentlyConsumeDeclaredGaps () => Assert.Throws<ArgumentException> (() => Check (SubmissionReviewMode.Complete));

	[Test]
	public void IncompleteDeclarationListReportsEveryUndeclaredScope ()
		{
		_declarations = _declarations with { Declarations = [_declarations.Declarations[0]] }; PinDeclarations ();
		Assert.That (Check ().Assessment!.BlockingIssues.Select (issue => issue.RequirementId), Does.Contain ("endurance"));
		}

	[TestCase ("duplicate")]
	[TestCase ("unknown")]
	[TestCase ("missing")]
	[TestCase ("numeric-mode")]
	public void AmbiguousDeclarationJsonIsRejected (string defect)
		{
		var json = File.ReadAllText (PathFor ("declarations.json"));
		json = defect switch
			{
				"duplicate" => json.Replace ("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
				"unknown" => json.Replace ("\"schemaVersion\":1", "\"schemaVersion\":1,\"ignoreFailures\":true"),
				"missing" => json.Replace ("\"schemaVersion\":1,", ""),
				_ => json.Replace ("\"DeclaredGaps\"", "1")
				};
		File.WriteAllText (PathFor ("declarations.json"), json);
		_declarationsPin = Digest ("declarations.json");
		Assert.Throws<JsonException> (() => Check ());
		}

	[TestCase (false, 0)]
	[TestCase (true, 1)]
	public void ConsoleReportsReviewEligibilityAndOriginalFailureSeparately (bool missingReason, int code)
		{
		if (missingReason) { _declarations = _declarations with { Declarations = [] }; PinDeclarations (); }
		using var output = new StringWriter ();
		using var error = new StringWriter ();
		Assert.That (SubmissionReviewAssessmentCommand.Run (Arguments (), output, error), Is.EqualTo (code), error.ToString ());
		using var document = JsonDocument.Parse (output.ToString ());
		Assert.That (document.RootElement.GetProperty ("readyForReview").GetBoolean (), Is.EqualTo (!missingReason));
		Assert.That (document.RootElement.GetProperty ("validation").GetProperty ("validationChecksPassed").GetBoolean (), Is.False);
		Assert.That (document.RootElement.GetProperty ("assessment").GetProperty ("verificationStatus").GetString (),
			Is.EqualTo (missingReason ? "NeedsCorrection" : "GapsDeclared"));
		Assert.That (error.ToString (), Is.Empty);
		}

	[Test]
	public void ConsoleRejectsChangedDeclarationsAndUnknownOptionsWithoutResults ()
		{
		File.AppendAllText (PathFor ("declarations.json"), " ");
		foreach (var args in new[] { Arguments (), Arguments ().Concat (["--ignore-failures", "true"]).ToArray () })
			{
			using var output = new StringWriter ();
			using var error = new StringWriter ();
			Assert.That (SubmissionReviewAssessmentCommand.Run (args, output, error), Is.EqualTo (2));
			Assert.That (output.ToString (), Is.Empty);
			Assert.That (error.ToString (), Is.Not.Empty);
			}
		}
	}