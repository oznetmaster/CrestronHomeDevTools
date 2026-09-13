// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO.Compression;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DeploymentTests
	{
	[Test]
	public void PackageInspectionReadsManifestWithoutLoadingAssembly ()
		{
		using var package = Package ();
		var info = DriverDeployment.Inspect (package);
		Assert.That (info.Model, Is.EqualTo ("Example Tests"));
		Assert.That (info.Version, Is.EqualTo ("1.0.000.0002"));
		}

	[TestCase ("missingAssembly")]
	[TestCase ("duplicateManifest")]
	[TestCase ("invalidId")]
	[TestCase ("invalidVersion")]
	public void InvalidPackageIsRejectedBeforeAnyConnection (string invalid)
		{
		using var package = Package (invalid);
		Assert.Throws<InvalidDataException> (() => DriverDeployment.Inspect (package));
		}

	[TestCase ("1.0.000.0002", "LocalByUser", "Example", true)]
	[TestCase ("1.0.0.2", "LocalByUser", "Example", true)]
	[TestCase ("1.0.000.0001", "LocalByUser", "Example", false)]
	[TestCase ("1.0.000.0002", "Remote", "Example", false)]
	[TestCase ("1.0.000.0002", "LocalByUser", "Another", false)]
	public void VerificationRequiresExpectedLocalPackageMetadata (string version, string state, string manufacturer, bool expected)
		{
		var package = new DriverPackageInfo (Guid.NewGuid ().ToString (), "Example Tests", "Example", "1.0.000.0002");
		var driver = new DriverInfo { Id = "catalogue-id", Model = package.Model, Manufacturer = manufacturer, Version = version, AvailabilityState = state };
		Assert.That (DriverDeployment.Matches (package, driver), Is.EqualTo (expected));
		}

	private static MemoryStream Package (string? invalid = null)
		{
		var output = new MemoryStream ();
		using (var zip = new ZipArchive (output, ZipArchiveMode.Create, true))
			{
			var metadata = JsonSerializer.Serialize (new
				{
				driverId = invalid == "invalidId" ? "invalid" : Guid.NewGuid ().ToString (),
				baseModel = "Example Tests",
				manufacturer = "Example",
				driverVersion = invalid == "invalidVersion" ? "invalid" : "1.0.000.0002"
				});
			using (var writer = new StreamWriter (zip.CreateEntry ("Example.dat").Open ()))
				writer.Write (metadata);
			if (invalid != "missingAssembly")
				using (var writer = new StreamWriter (zip.CreateEntry ("Example.dll").Open ()))
					writer.Write ("Not executed by metadata inspection");
			if (invalid == "duplicateManifest")
				using (var writer = new StreamWriter (zip.CreateEntry ("Other.dat").Open ()))
					writer.Write (metadata);
			}
		output.Position = 0;
		return output;
		}
	}