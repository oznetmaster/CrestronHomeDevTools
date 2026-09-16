// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
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