// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO.Compression;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed partial class SubmissionReviewFilesTests
	{
	private SubmissionReviewBundleReport CreateReviewBundle () => SubmissionBundle.CreateReview (PathFor ("review.zip"),
		PathFor ("candidate.json"), _candidatePin, PathFor (PACKAGE), PathFor ("policy.json"), PathFor ("template.pdf"),
		PathFor ("observations.json"), _root, PathFor ("declarations.json"), _declarationsPin, SubmissionReviewMode.DeclaredGaps, Now);
	private SubmissionReviewBundleReport CheckReviewBundle () => SubmissionBundle.CheckReview (PathFor ("review.zip"),
		Digest ("review.zip"), _candidatePin, _root, _declarationsPin, SubmissionReviewMode.DeclaredGaps, Now);
	private void PutArchiveEntry (string name, byte[] bytes, bool replace = true)
		{
		using var zip = ZipFile.Open (PathFor ("review.zip"), ZipArchiveMode.Update);
		if (replace) zip.GetEntry (name)?.Delete ();
		using var stream = zip.CreateEntry (name).Open ();
		stream.Write (bytes);
		}

	[Test]
	public void ReviewBundleRetainsExactDeclarationsAndOriginalFailedObservationsOnly ()
		{
		File.WriteAllText (PathFor ("measurement.txt"), "Synthetic failed measurement.");
		File.WriteAllText (PathFor ("unreferenced-private.txt"), "Do not include this unrelated private file.");
		Write ("observations.json", new SubmissionEvidenceDocument (1, [new ("controls", _candidate.Identity,
			SubmissionEvidenceOutcome.Failed, Now.AddMinutes (-1), Now, [new ("measurement.txt", Digest ("measurement.txt"))])]));
		var created = CreateReviewBundle ();
		Assert.That (created.ReadyForReview, Is.True);
		Assert.That (created.Review.Validation.ValidationChecksPassed, Is.False);
		using (var zip = ZipFile.OpenRead (PathFor ("review.zip")))
			{
			Assert.That (zip.Entries.Select (entry => entry.FullName), Does.Contain ("declarations.json").And.Contain ("evidence/measurement.txt"));
			Assert.That (zip.Entries.Select (entry => entry.FullName), Does.Not.Contain ("evidence/unreferenced-private.txt"));
			foreach (var name in new[] { "declarations.json", "observations.json" })
				{
				using var input = zip.GetEntry (name)!.Open ();
				using var copy = new MemoryStream ();
				input.CopyTo (copy);
				Assert.That (copy.ToArray (), Is.EqualTo (File.ReadAllBytes (PathFor (name))));
				}
			}
		var checkedReport = CheckReviewBundle ();
		Assert.That (checkedReport.BundleSha256, Is.EqualTo (created.BundleSha256));
		Assert.That (checkedReport.Review.Assessment!.Requirements[0].ObservedOutcome, Is.EqualTo (SubmissionEvidenceOutcome.Failed));
		Assert.That (checkedReport.Review.DeclarationsSha256, Is.EqualTo (_declarationsPin));
		}

	[Test]
	public void ReviewBundleIsVerifiedFromItsOwnBytesWithoutOriginalSourceFiles ()
		{
		CreateReviewBundle ();
		File.Delete (PathFor ("observations.json"));
		File.Delete (PathFor ("declarations.json"));
		File.Delete (PathFor (PACKAGE));
		Assert.That (CheckReviewBundle ().ReadyForReview, Is.True);
		}

	[Test]
	public void CompleteOnlyArchiveCannotSilentlyAcceptDeclaredGapBundle ()
		{
		CreateReviewBundle ();
		Assert.Throws<InvalidDataException> (() => SubmissionBundle.Check (PathFor ("review.zip"), Digest ("review.zip"), _candidatePin, _root, Now));
		Assert.Throws<ArgumentException> (() => SubmissionBundle.CheckReview (PathFor ("review.zip"), Digest ("review.zip"), _candidatePin,
			_root, _declarationsPin, SubmissionReviewMode.Complete, Now));
		}

	[TestCase ("declarations")]
	[TestCase ("archive")]
	public void ReviewBundleRequiresIndependentPins (string changed)
		{
		var report = CreateReviewBundle ();
		Assert.Throws<InvalidDataException> (() => SubmissionBundle.CheckReview (PathFor ("review.zip"),
			changed == "archive" ? new ('f', 64) : report.BundleSha256, _candidatePin, _root,
			changed == "declarations" ? new ('f', 64) : _declarationsPin, SubmissionReviewMode.DeclaredGaps, Now));
		}

	[TestCase ("extra")]
	[TestCase ("traversal")]
	[TestCase ("duplicate")]
	[TestCase ("missing")]
	public void ReviewBundleRejectsExtraUnsafeDuplicateAndMissingFiles (string defect)
		{
		CreateReviewBundle ();
		if (defect == "missing")
			{
			using var zip = ZipFile.Open (PathFor ("review.zip"), ZipArchiveMode.Update);
			zip.GetEntry ("declarations.json")!.Delete ();
			}
		else
			PutArchiveEntry (defect == "traversal" ? "../outside.txt" : defect == "duplicate" ? "declarations.json" : "evidence/private.txt", "Unexpected"u8.ToArray (), false);
		Assert.Throws<InvalidDataException> (() => CheckReviewBundle ());
		}

	[Test]
	public void ReviewBundleRejectsChangedDeclarationEvenWhenArchiveDigestIsUpdated ()
		{
		CreateReviewBundle ();
		PutArchiveEntry ("declarations.json", JsonSerializer.SerializeToUtf8Bytes (_declarations with { Declarations = [] }, Json));
		Assert.Throws<InvalidDataException> (() => CheckReviewBundle ());
		}

	[Test]
	public void UnexplainedGapPublishesNoReviewArchiveAndPreservesExistingOutput ()
		{
		_declarations = _declarations with { Declarations = [] }; PinDeclarations ();
		Assert.Throws<InvalidDataException> (() => CreateReviewBundle ());
		Assert.That (File.Exists (PathFor ("review.zip")), Is.False);
		Assert.That (Directory.GetDirectories (_root, ".submission-*"), Is.Empty);
		File.WriteAllText (PathFor ("review.zip"), "Keep original");
		Assert.Throws<IOException> (() => CreateReviewBundle ());
		Assert.That (File.ReadAllText (PathFor ("review.zip")), Is.EqualTo ("Keep original"));
		}

	[Test]
	public void ReviewBundleConsoleRetainsGapStatusThroughCreateAndCheck ()
		{
		var args = Arguments ().Concat (["--output", PathFor ("review.zip")]).ToArray ();
		using var output = new StringWriter ();
		using var error = new StringWriter ();
		Assert.That (SubmissionReviewBundleCommand.Run (true, args, output, error), Is.Zero, error.ToString ());
		using var created = JsonDocument.Parse (output.ToString ());
		Assert.That (created.RootElement.GetProperty ("review").GetProperty ("validation").GetProperty ("validationChecksPassed").GetBoolean (), Is.False);
		using var checkedOutput = new StringWriter ();
		Assert.That (SubmissionReviewBundleCommand.Run (false,
			["--bundle", PathFor ("review.zip"), "--bundle-sha256", Digest ("review.zip"), "--candidate-sha256", _candidatePin,
			"--declarations-sha256", _declarationsPin, "--mode", "declared-gaps", "--scratch", _root], checkedOutput, error), Is.Zero, error.ToString ());
		using var checkedReport = JsonDocument.Parse (checkedOutput.ToString ());
		Assert.That (checkedReport.RootElement.GetProperty ("review").GetProperty ("assessment").GetProperty ("verificationStatus").GetString (), Is.EqualTo ("GapsDeclared"));
		}
	}