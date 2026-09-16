// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionValidationTests
	{
	private string _directory = null!;
	private string _candidateDigest = null!;
	private SubmissionCandidate _candidate = null!;
	private SubmissionEvidenceDocument _observations = null!;
	private static readonly DateTimeOffset Now = new (2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
	private const string PACKAGE_NAME = "ExampleDeveloper_Test_Example_IP.pkg";
	private static readonly JsonSerializerOptions JsonOptions = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter () }
		};

	[SetUp]
	public void SetUp ()
		{
		_directory = Path.Combine (TestContext.CurrentContext.WorkDirectory, "submission-check-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (_directory);
		Directory.CreateDirectory (PathFor ("evidence"));
		using var package = SubmissionPackageTests.Package ();
		File.WriteAllBytes (PathFor (PACKAGE_NAME), package.ToArray ());
		File.WriteAllText (PathFor ("template.pdf"), "%PDF-1.7\nTest template identity fixture");
		File.WriteAllText (PathFor ("evidence/trace.txt"), "Recorded fixture observations");
		Write ("policy.json", new SubmissionEvidencePolicy (1, [new ("ui.navigation", TimeSpan.FromSeconds (10))]));
		_candidate = new (1, new (Digest (PACKAGE_NAME), new ('b', 40), Digest ("policy.json"), Digest ("template.pdf")),
			new ("1286a404-144e-4d77-b96b-1d1272f21c64", "1.2.003.0000", PortalSubmissionKind.NewDriver, "ExampleDeveloper", "support@example.com"));
		SaveCandidate ();
		_observations = new (1, [new ("ui.navigation", _candidate.Identity, SubmissionEvidenceOutcome.Passed,
			Now.AddMinutes (-1), Now, [new ("trace.txt", Digest ("evidence/trace.txt"))])]);
		Write ("observations.json", _observations);
		}

	[TearDown]
	public void TearDown () => Directory.Delete (_directory, true);

	[Test]
	public void MatchingActualFilesPassAndReportExactDigests ()
		{
		var report = Check ();
		Assert.Multiple (() =>
			{
				Assert.That (report.ValidationChecksPassed, Is.True);
				Assert.That (report.CandidateSha256, Is.EqualTo (_candidateDigest));
				Assert.That (report.ObservationsSha256, Is.EqualTo (Digest ("observations.json")));
				Assert.That (report.Package!.Sha256, Is.EqualTo (Digest (PACKAGE_NAME)));
			});
		}

	[TestCase ("candidate.json", "candidate-digest")]
	[TestCase (PACKAGE_NAME, "package-digest")]
	[TestCase ("policy.json", "policy-digest")]
	[TestCase ("template.pdf", "template-digest")]
	public void ReplacedActualInputsCannotPassUsingOldDeclaredHashes (string file, string code)
		{
		File.AppendAllText (PathFor (file), " ");
		var report = Check ();
		Assert.That (report.Issues.Select (issue => issue.Code), Does.Contain (code));
		Assert.That (report.ValidationChecksPassed, Is.False);
		}

	[Test]
	public void ChangedEvidenceFileFailsCombinedGate ()
		{
		File.WriteAllText (PathFor ("evidence/trace.txt"), "Changed evidence");
		var report = Check ();
		Assert.That (report.ValidationChecksPassed, Is.False);
		Assert.That (report.Evidence!.Issues.Select (issue => issue.Code), Does.Contain ("evidence-digest"));
		}

	[Test]
	public void CombinedFileGateEnforcesExecutionRequirementsFromPinnedPolicy ()
		{
		Write ("policy.json", new SubmissionEvidencePolicy (1, [new ("ui.navigation", TimeSpan.Zero, Execution:
			new ("home/tile", "android", SubmissionEvidenceOutcome.Passed, null, false))]));
		RebindIdentity (_candidate.Identity with { PolicySha256 = Digest ("policy.json") });
		var missing = Check ();
		Assert.That (missing.ValidationChecksPassed, Is.False);
		Assert.That (missing.Evidence!.Issues.Select (i => i.Code), Does.Contain ("execution-missing"));
		_observations = _observations with { Observations = [_observations.Observations[0] with { Execution = new ("home/tile", "android") }] };
		Write ("observations.json", _observations);
		Assert.That (Check ().ValidationChecksPassed, Is.True);
		}

	[Test]
	public void CorrectPackageDigestDoesNotBypassPackageStructure ()
		{
		using var package = SubmissionPackageTests.Package ("missingPdf");
		File.WriteAllBytes (PathFor (PACKAGE_NAME), package.ToArray ());
		RebindIdentity (_candidate.Identity with
			{
			PackageSha256 = Digest (PACKAGE_NAME)
			});
		var report = Check ();
		Assert.That (report.Evidence!.EvidenceChecksPassed, Is.True);
		Assert.That (report.Package!.Issues.Select (issue => issue.Code), Does.Contain ("matching-pdf"));
		Assert.That (report.ValidationChecksPassed, Is.False);
		}

	[Test]
	public void ReusedObservationFromDifferentSourceCommitFails ()
		{
		_candidate = _candidate with
			{
			Identity = _candidate.Identity with
				{
				SourceCommit = new ('e', 40)
				}
			};
		SaveCandidate ();
		Assert.That (Check ().Evidence!.Issues.Select (issue => issue.Code), Does.Contain ("identity-mismatch"));
		}

	[Test]
	public void MissingObservationFailsWithoutFixedTestCount ()
		{
		Write ("policy.json", new SubmissionEvidencePolicy (1, [new ("ui.navigation", TimeSpan.Zero), new ("ui.feedback", TimeSpan.Zero)]));
		RebindIdentity (_candidate.Identity with
			{
			PolicySha256 = Digest ("policy.json")
			});
		Assert.That (Check ().Evidence!.Issues.Select (issue => issue.Code), Does.Contain ("missing-observation"));
		}

	[TestCase ("unknown")]
	[TestCase ("duplicate")]
	[TestCase ("missing")]
	[TestCase ("null")]
	[TestCase ("numeric-enum")]
	[TestCase ("misspelled")]
	public void AmbiguousOrIncompleteJsonIsRejected (string defect)
		{
		var json = File.ReadAllText (PathFor ("candidate.json"));
		json = defect switch
			{
				"unknown" => json.Replace ("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"allowSkipped\": true,"),
				"duplicate" => json.Replace ("\"schemaVersion\": 1,", "\"schemaVersion\": 2, \"schemaVersion\": 1,"),
				"missing" => json.Replace ("\"schemaVersion\": 1,", ""),
				"null" => json.Replace ("\"sourceCommit\": \"" + new string ('b', 40) + "\"", "\"sourceCommit\": null"),
				"numeric-enum" => json.Replace ("\"NewDriver\"", "1"),
				_ => json.Replace ("\"schemaVersion\"", "\"SchemaVersion\"")
				};
		File.WriteAllText (PathFor ("candidate.json"), json);
		_candidateDigest = Digest ("candidate.json");
		Assert.Throws<JsonException> (() => Check ());
		}

	[Test]
	public void UnsupportedSchemaIsRejected ()
		{
		_candidate = _candidate with
			{
			SchemaVersion = 2
			};
		SaveCandidate ();
		Assert.Throws<ArgumentException> (() => Check ());
		}

	[Test]
	public void OmittedRequiredObservationFilesAreRejected ()
		{
		File.WriteAllText (PathFor ("observations.json"), "{\"schemaVersion\":1,\"observations\":[{\"requirementId\":\"ui.navigation\"}]}");
		Assert.Throws<JsonException> (() => Check ());
		}

	[Test]
	public void InvalidPinnedDigestIsRejectedBeforeReadingFiles ()
		{
		_candidateDigest = "not-a-digest";
		Assert.Throws<ArgumentException> (() => Check ());
		}

	[TestCase (false, 0)]
	[TestCase (true, 1)]
	public async Task CliChecksEvidenceWithoutProcessorCredentials (bool changedEvidence, int exitCode)
		{
		var finished = DateTimeOffset.UtcNow.AddMinutes (-1);
		Write ("observations.json", _observations with
			{
			Observations = _observations.Observations.Select (item => item with { StartedUtc = finished.AddMinutes (-1), FinishedUtc = finished }).ToArray ()
			});
		if (changedEvidence)
			File.WriteAllText (PathFor ("evidence/trace.txt"), "Changed evidence");
		var start = new ProcessStartInfo ("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
		foreach (var argument in new[]
			{
			Path.Combine (AppContext.BaseDirectory, "CrestronHomeDevTools.Console.dll"), "submission-evidence-check",
			"--candidate", PathFor ("candidate.json"), "--candidate-sha256", _candidateDigest,
			"--package", PathFor (PACKAGE_NAME), "--policy", PathFor ("policy.json"), "--template", PathFor ("template.pdf"),
			"--observations", PathFor ("observations.json"), "--evidence", PathFor ("evidence"), "--profile", "nonexistent-submission-test-profile"
			})
			start.ArgumentList.Add (argument);
		using var process = Process.Start (start)!;
		var stdout = process.StandardOutput.ReadToEndAsync ();
		var stderr = process.StandardError.ReadToEndAsync ();
		using var timeout = new CancellationTokenSource (TimeSpan.FromSeconds (20));
		try
			{
			await process.WaitForExitAsync (timeout.Token);
			}
		finally { if (!process.HasExited) process.Kill (true); }
		var output = await stdout;
		Assert.That (process.ExitCode, Is.EqualTo (exitCode), (await stderr) + output);
		using var result = JsonDocument.Parse (output);
		Assert.That (result.RootElement.GetProperty ("ValidationChecksPassed").GetBoolean (), Is.EqualTo (!changedEvidence));
		}

	private string PathFor (string relative) => Path.Combine (_directory, relative);

	private SubmissionBundleReport Bundle () => SubmissionBundle.Create (PathFor ("private.zip"), PathFor ("candidate.json"), _candidateDigest,
		PathFor (PACKAGE_NAME), PathFor ("policy.json"), PathFor ("template.pdf"), PathFor ("observations.json"), PathFor ("evidence"), Now);
	private SubmissionBundleReport CheckBundle (string digest) => SubmissionBundle.Check (PathFor ("private.zip"), digest, _candidateDigest, _directory, Now);

	[Test]
	public void BundleRetainsOnlyReferencedFilesAndRevalidatesAfterOriginalsChange ()
		{
		File.WriteAllText (PathFor ("evidence/private-settings.json"), "Private fixture: must not be copied");
		var bundle = Bundle ();
		Assert.That (bundle.ValidationChecksPassed, Is.True);
		Assert.That (bundle.BundleSha256, Is.EqualTo (Digest ("private.zip")));
		Assert.That (bundle.FileCount, Is.EqualTo (6));
		using (var zip = ZipFile.OpenRead (PathFor ("private.zip")))
			{
			Assert.That (zip.Entries.Select (entry => entry.FullName), Does.Not.Contain ("evidence/private-settings.json"));
			Assert.That (zip.Entries.Select (entry => entry.FullName), Does.Contain ("package/" + PACKAGE_NAME));
			using var entry = zip.GetEntry ("package/" + PACKAGE_NAME)!.Open ();
			Assert.That (Convert.ToHexString (SHA256.HashData (entry)).ToLowerInvariant (), Is.EqualTo (_candidate.Identity.PackageSha256));
			}
		File.WriteAllText (PathFor ("evidence/trace.txt"), "Changed after snapshot");
		File.WriteAllText (PathFor (PACKAGE_NAME), "Changed after snapshot");
		Assert.That (CheckBundle (bundle.BundleSha256).ValidationChecksPassed, Is.True);
		Assert.That (Directory.GetDirectories (_directory, ".submission-*"), Is.Empty);
		}

	[Test]
	public void BundleDoesNotOverwriteExistingDestination ()
		{
		var first = Bundle ();
		Assert.Throws<IOException> (() => Bundle ());
		Assert.That (Digest ("private.zip"), Is.EqualTo (first.BundleSha256));
		}

	[TestCase ("candidate.json")]
	[TestCase (PACKAGE_NAME)]
	[TestCase ("policy.json")]
	[TestCase ("template.pdf")]
	[TestCase ("evidence/trace.txt")]
	public void InvalidInputsCannotProduceFinishedBundle (string changed)
		{
		File.AppendAllText (PathFor (changed), "changed");
		Assert.Throws<InvalidDataException> (() => Bundle ());
		Assert.That (File.Exists (PathFor ("private.zip")), Is.False);
		Assert.That (Directory.GetDirectories (_directory, ".submission-*"), Is.Empty);
		}

	[Test]
	public void RepackedArchiveRequiresNewIndependentBundleDigest ()
		{
		var bundle = Bundle ();
		using (var zip = ZipFile.Open (PathFor ("private.zip"), ZipArchiveMode.Update)) zip.GetEntry ("template.pdf")!.LastWriteTime = Now;
		Assert.Throws<InvalidDataException> (() => CheckBundle (bundle.BundleSha256));
		Assert.That (CheckBundle (Digest ("private.zip")).ValidationChecksPassed, Is.True);
		}

	[TestCase ("candidate.json", false)]
	[TestCase ("evidence/trace.txt", false)]
	[TestCase ("evidence/unreferenced.txt", true)]
	[TestCase ("../escape.txt", true)]
	[TestCase ("evidence/../escape.txt", true)]
	[TestCase ("evidence/CON.txt", true)]
	[TestCase ("evidence/Trace.txt", true)]
	[TestCase ("evidence/link.txt", true)]
	public void RehashingArchiveCannotBypassContainedEvidenceAndPathValidation (string name, bool extra)
		{
		Bundle ();
		using (var zip = ZipFile.Open (PathFor ("private.zip"), ZipArchiveMode.Update))
			{
			if (!extra) zip.GetEntry (name)!.Delete ();
			var entry = zip.CreateEntry (name);
			if (name == "evidence/link.txt") entry.ExternalAttributes = 0xA000 << 16;
			using var writer = new StreamWriter (entry.Open ());
			writer.Write ("Changed contents");
			}
		if (extra) Assert.Throws<InvalidDataException> (() => CheckBundle (Digest ("private.zip")));
		else Assert.That (CheckBundle (Digest ("private.zip")).ValidationChecksPassed, Is.False);
		Assert.That (Directory.GetDirectories (_directory, ".submission-*"), Is.Empty);
		}

	[Test]
	public void DuplicateArchiveEntriesAreRejected ()
		{
		Bundle ();
		using (var zip = ZipFile.Open (PathFor ("private.zip"), ZipArchiveMode.Update)) zip.CreateEntry ("candidate.json");
		Assert.Throws<InvalidDataException> (() => CheckBundle (Digest ("private.zip")));
		}

	[TestCase ("../private.txt")]
	[TestCase ("nested/../../private.txt")]
	[TestCase ("nested/CON.txt")]
	[TestCase ("nested/CON .txt")]
	[TestCase ("C:/private.txt")]
	public void UnsafeObservationCannotCopyFilesIntoBundle (string relative)
		{
		Write ("observations.json", _observations with { Observations = [_observations.Observations[0] with
			{ Files = [new (relative, new ('a', 64))] }] });
		Assert.Throws<InvalidDataException> (() => Bundle ());
		Assert.That (File.Exists (PathFor ("private.zip")), Is.False);
		}

	[Test]
	public void MissingArchiveEvidenceIsRejected ()
		{
		Bundle ();
		using (var zip = ZipFile.Open (PathFor ("private.zip"), ZipArchiveMode.Update)) zip.GetEntry ("evidence/trace.txt")!.Delete ();
		Assert.Throws<InvalidDataException> (() => CheckBundle (Digest ("private.zip")));
		}

	[Test]
	public void ExcessiveExpandedEntryIsRejectedBeforeExtraction ()
		{
		Bundle ();
		using (var zip = ZipFile.Open (PathFor ("private.zip"), ZipArchiveMode.Update))
			{
			using var destination = zip.CreateEntry ("evidence/oversized.txt", CompressionLevel.Fastest).Open ();
			var zeros = new byte[1024 * 1024];
			for (var i = 0; i < 65; i++) destination.Write (zeros);
			}
		Assert.Throws<InvalidDataException> (() => CheckBundle (Digest ("private.zip")));
		Assert.That (Directory.GetDirectories (_directory, ".submission-*"), Is.Empty);
		}

	[Test]
	public void ArchiveWithTooManyEntriesIsRejected ()
		{
		using (var zip = ZipFile.Open (PathFor ("private.zip"), ZipArchiveMode.Create))
			for (var i = 0; i < 4097; i++) zip.CreateEntry ("evidence/item" + i);
		Assert.Throws<InvalidDataException> (() => CheckBundle (Digest ("private.zip")));
		}

	[Test]
	public void IncompleteObservationCannotProduceBundle ()
		{
		Write ("observations.json", _observations with { Observations = [] });
		Assert.Throws<InvalidDataException> (() => Bundle ());
		Assert.That (File.Exists (PathFor ("private.zip")), Is.False);
		}

	[Test]
	public void CancellationLeavesNoPublishedBundle ()
		{
		Assert.Throws<OperationCanceledException> (() => SubmissionBundle.Create (PathFor ("private.zip"), PathFor ("candidate.json"), _candidateDigest,
			PathFor (PACKAGE_NAME), PathFor ("policy.json"), PathFor ("template.pdf"), PathFor ("observations.json"), PathFor ("evidence"), Now, new CancellationToken (true)));
		Assert.That (File.Exists (PathFor ("private.zip")), Is.False);
		Assert.That (Directory.GetDirectories (_directory, ".submission-*"), Is.Empty);
		}

	[Test]
	public async Task BundleCliCreatesAndChecksWithoutProcessorCredentials ()
		{
		var finished = DateTimeOffset.UtcNow.AddMinutes (-1);
		Write ("observations.json", _observations with { Observations = _observations.Observations.Select (item => item with
			{ StartedUtc = finished.AddMinutes (-1), FinishedUtc = finished }).ToArray () });
		using var created = await BundleCli ("submission-bundle-create", "--output", PathFor ("private.zip"),
			"--candidate", PathFor ("candidate.json"), "--candidate-sha256", _candidateDigest, "--package", PathFor (PACKAGE_NAME),
			"--policy", PathFor ("policy.json"), "--template", PathFor ("template.pdf"), "--observations", PathFor ("observations.json"), "--evidence", PathFor ("evidence"));
		Assert.That (created.RootElement.GetProperty ("ValidationChecksPassed").GetBoolean (), Is.True);
		using var verified = await BundleCli ("submission-bundle-check", "--bundle", PathFor ("private.zip"),
			"--bundle-sha256", created.RootElement.GetProperty ("BundleSha256").GetString ()!, "--candidate-sha256", _candidateDigest, "--scratch", _directory);
		Assert.That (verified.RootElement.GetProperty ("ValidationChecksPassed").GetBoolean (), Is.True);
		}

	private static async Task<JsonDocument> BundleCli (params string[] args)
		{
		var start = new ProcessStartInfo ("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
		start.ArgumentList.Add (Path.Combine (AppContext.BaseDirectory, "CrestronHomeDevTools.Console.dll"));
		foreach (var argument in args) start.ArgumentList.Add (argument);
		start.ArgumentList.Add ("--profile");
		start.ArgumentList.Add ("nonexistent-submission-test-profile");
		using var process = Process.Start (start)!;
		var output = process.StandardOutput.ReadToEndAsync ();
		var errors = process.StandardError.ReadToEndAsync ();
		using var deadline = new CancellationTokenSource (TimeSpan.FromSeconds (30));
		try { await process.WaitForExitAsync (deadline.Token); }
		finally { if (!process.HasExited) process.Kill (true); }
		Assert.That (process.ExitCode, Is.Zero, await errors);
		return JsonDocument.Parse (await output);
		}
	private string Digest (string relative) => Convert.ToHexString (SHA256.HashData (File.ReadAllBytes (PathFor (relative)))).ToLowerInvariant ();
	private void Write<T> (string relative, T value) => File.WriteAllText (PathFor (relative), JsonSerializer.Serialize (value, JsonOptions));
	private void SaveCandidate ()
		{
		Write ("candidate.json", _candidate);
		_candidateDigest = Digest ("candidate.json");
		}
	private void RebindIdentity (SubmissionEvidenceIdentity identity)
		{
		_candidate = _candidate with
			{
			Identity = identity
			};
		SaveCandidate ();
		_observations = _observations with
			{
			Observations = _observations.Observations.Select (item => item with { Identity = identity }).ToArray ()
			};
		Write ("observations.json", _observations);
		}
	private SubmissionValidationReport Check () => SubmissionValidation.CheckFiles (PathFor ("candidate.json"), _candidateDigest,
		PathFor (PACKAGE_NAME), PathFor ("policy.json"), PathFor ("template.pdf"), PathFor ("observations.json"), PathFor ("evidence"), Now);
	}