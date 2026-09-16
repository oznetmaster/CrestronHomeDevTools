// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO.Compression;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class SubmissionPackageTests
	{
	private const string DriverId = "1286a404-144e-4d77-b96b-1d1272f21c64";
	private const string Basename = "ExampleDeveloper_Test_Example_IP";
	private static SubmissionPackageRequirements Requirements => new (DriverId, "1.2.003.0000", PortalSubmissionKind.NewDriver, "ExampleDeveloper", "support@example.com");

	[Test]
	public void MatchingPackagePassesStructuralChecksWithoutExecutingAssembly ()
		{
		using var stream = Package ();
		var report = SubmissionPackage.Inspect (stream, Basename + ".pkg", Requirements);
		Assert.Multiple (() =>
			{
				Assert.That (report.PackageChecksPassed, Is.True);
				Assert.That (report.Identity!.DriverId, Is.EqualTo (DriverId));
				Assert.That (report.Sha256, Has.Length.EqualTo (64));
			});
		}

	[TestCase ("missingPdf", "matching-pdf")]
	[TestCase ("pdfWrongCase", "matching-pdf")]
	[TestCase ("pdfInSubfolder", "matching-pdf")]
	[TestCase ("invalidPdf", "help-pdf-header")]
	[TestCase ("missingDeveloper", "developer-metadata")]
	[TestCase ("missingEmail", "support-email")]
	[TestCase ("missingDependency", "dependency-group")]
	[TestCase ("wrongReference", "assembly-reference")]
	[TestCase ("duplicatePdf", "duplicate-entry")]
	[TestCase ("traversal", "archive-path")]
	[TestCase ("backslash", "archive-path")]
	[TestCase ("privateInput", "private-test-input")]
	public void PackagingDefectsBlockPreflight (string defect, string code)
		{
		using var stream = Package (defect);
		var report = SubmissionPackage.Inspect (stream, Basename + ".pkg", Requirements);
		Assert.That (report.Issues.Select (issue => issue.Code), Does.Contain (code));
		Assert.That (report.PackageChecksPassed, Is.False);
		}

	[Test]
	public void RenamingOnlyOuterPackageDoesNotSatisfyNamingRules ()
		{
		using var stream = Package ();
		var report = SubmissionPackage.Inspect (stream, "ExampleDeveloper_Another.pkg", Requirements);
		Assert.That (report.Issues.Select (issue => issue.Code), Is.SupersetOf (new[] { "matching-dll", "matching-dat", "matching-pdf", "assembly-reference" }));
		}

	[Test]
	public void ExistingPortalUpdateHasOnlyTheDeveloperFilenameExemption ()
		{
		using var stream = Package ();
		var newDriver = SubmissionPackage.Inspect (stream, Basename + ".pkg", Requirements with
			{
			DeveloperFilenameToken = "DifferentCompany"
			});
		Assert.That (newDriver.Issues.Select (issue => issue.Code), Does.Contain ("developer-filename"));
		var existing = SubmissionPackage.Inspect (stream, Basename + ".pkg", Requirements with
			{
			Kind = PortalSubmissionKind.ExistingDriverUpdate,
			DeveloperFilenameToken = ""
			});
		Assert.That (existing.PackageChecksPassed, Is.True);
		using var missingHelp = Package ("missingPdf");
		Assert.That (SubmissionPackage.Inspect (missingHelp, Basename + ".pkg", Requirements with
			{
			Kind = PortalSubmissionKind.ExistingDriverUpdate
			}).PackageChecksPassed, Is.False);
		}

	[Test]
	public void CandidateIdentityMismatchIsReported ()
		{
		using var stream = Package ();
		var report = SubmissionPackage.Inspect (stream, Basename + ".pkg", Requirements with
			{
			DriverId = Guid.NewGuid ().ToString (),
			DriverVersion = "1.2.004.0000"
			});
		Assert.That (report.Issues.Select (issue => issue.Code), Is.SupersetOf (new[] { "driver-id", "driver-version" }));
		}

	[Test]
	public void InvalidZipProducesFailureReport ()
		{
		using var stream = new MemoryStream ("not a package"u8.ToArray ());
		var report = SubmissionPackage.Inspect (stream, Basename + ".pkg", Requirements);
		Assert.That (report.Issues.Select (issue => issue.Code), Does.Contain ("invalid-package"));
		}

	[TestCase ("id")]
	[TestCase ("version")]
	[TestCase ("kind")]
	[TestCase ("developer")]
	[TestCase ("email")]
	public void IncompleteCandidateDeclarationIsRejected (string invalid)
		{
		using var stream = Package ();
		var requirements = invalid switch
			{
				"id" => Requirements with { DriverId = "" },
				"version" => Requirements with { DriverVersion = "1.2.3" },
				"kind" => Requirements with { Kind = PortalSubmissionKind.Unspecified },
				"email" => Requirements with { PublicSupportEmail = "not-an-email" },
				_ => Requirements with { DeveloperFilenameToken = "" }
				};
		Assert.Throws<ArgumentException> (() => SubmissionPackage.Inspect (stream, Basename + ".pkg", requirements));
		}

	private static MemoryStream Package (string? defect = null)
		{
		var stream = new MemoryStream ();
		using (var zip = new ZipArchive (stream, ZipArchiveMode.Create, true))
			{
			void Add (string path, string content)
				{
				using var writer = new StreamWriter (zip.CreateEntry (path).Open ());
				writer.Write (content);
				}
			var metadata = new Dictionary<string, object?>
				{
				["driverId"] = DriverId,
				["baseModel"] = "Example",
				["manufacturer"] = "Example Manufacturer",
				["driverVersion"] = "1.2.003.0000",
				["developer"] = "Example Developer",
				["developerContact"] = new { company = "Example Developer", email = defect == "missingEmail" ? "" : "support@example.com" },
				["dependencyGroup"] = "",
				["assemblyFileName"] = defect == "wrongReference" ? "Other.dll" : Basename + ".dll"
				};
			if (defect == "missingDeveloper")
				metadata.Remove ("developer");
			if (defect == "missingDependency")
				metadata.Remove ("dependencyGroup");
			Add (Basename + ".dat", JsonSerializer.Serialize (metadata));
			Add (Basename + ".dll", "Never loaded or executed");
			if (defect != "missingPdf")
				Add (defect == "pdfWrongCase" ? Basename.ToUpperInvariant () + ".pdf" : defect == "pdfInSubfolder" ? "docs/" + Basename + ".pdf" : Basename + ".pdf",
					defect == "invalidPdf" ? "not a PDF" : "%PDF-1.7\nMinimal header fixture; full document validation is a separate gate.");
			if (defect == "duplicatePdf")
				Add (Basename + ".pdf", "%PDF-1.7");
			if (defect == "traversal")
				Add ("../outside.txt", "invalid");
			if (defect == "backslash")
				Add ("dir\\file.txt", "invalid");
			if (defect == "privateInput")
				Add ("settings/LiveTestSettings.json", "{}");
			}
		stream.Position = 0;
		return stream;
		}
	}