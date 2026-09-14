// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class PackageStorageTests
	{
	[Test]
	public void GenericFilename_UsesManifestModelAndProtectsInstalledOlderVersion ()
		{
		var package = new DriverPackageInfo (Guid.NewGuid ().ToString (), "Example Tests", "Example", "1.2.0.4");
		var devices = new[] { new DeviceInfo { Id = 42, Model = "example tests " }, new DeviceInfo { Id = 43, Model = "Another Driver" } };
		var catalogue = new[] { new DriverInfo { Id = "new", Model = package.Model, Manufacturer = package.Manufacturer, Version = "1.002.000.0004", AvailabilityState = "LocalByUser" },
			new DriverInfo { Id = "old", Model = package.Model, Manufacturer = package.Manufacturer, Version = "1.1.0.1", AvailabilityState = "LocalByUser" } };
		var entry = DriverPackageStorage.Describe ("/storage/actual_.pkg", 123, DateTime.UtcNow, package, devices, catalogue);
		Assert.That (entry.MatchingInstalledDeviceIds, Is.EqualTo (new[] { 42 }));
		Assert.That (entry.MatchingCatalogueIds, Is.EqualTo (new[] { "new" }));
		}

	[Test]
	public void UnreadablePackage_RemainsVisibleWithItsInspectionError ()
		{
		var entry = DriverPackageStorage.Describe ("/storage/unknown.pkg", 123, DateTime.UtcNow, null, [], [], "Unknown format");
		Assert.That (entry.Path, Is.EqualTo ("/storage/unknown.pkg"));
		Assert.That (entry.Package, Is.Null);
		Assert.That (entry.InspectionError, Is.EqualTo ("Unknown format"));
		Assert.That (entry.Bytes, Is.EqualTo (123));
		}
	}