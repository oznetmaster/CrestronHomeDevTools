// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DriverReadinessTests
{
    private static DeviceInfo Device(int id, int? parent = null) => new()
    {
        Id = id, ParentDeviceId = parent, Name = "Demo", Model = "Platform", LocationId = 10,
        PropertyValues = new()
        {
            ["cp.driverInformation:version"] = JsonSerializer.SerializeToElement("2.1.002.0000"),
            ["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement("Loaded"),
            ["cp.driverConfiguration:isConfigured"] = JsonSerializer.SerializeToElement(true),
            ["readyIndicator:isReady"] = JsonSerializer.SerializeToElement(true),
            ["onlineIndicator:isOnline"] = JsonSerializer.SerializeToElement(true)
        }
    };
    private static DriverReadinessReport Check(params DeviceInfo[] devices) => DriverReadiness.Inspect(devices, 1, "Platform", "2.1.2.0");

    [TestCase("cp.driverConfiguration:isConfigured", "configuration-required-or-unconfirmed")]
    [TestCase("onlineIndicator:isOnline", "device-offline-or-unconfirmed")]
    [TestCase("readyIndicator:isReady", "device-not-ready")]
    public void OnlineRootDoesNotHideBrokenInstalledChild(string key, string reason)
    {
        var child = Device(2, 1); child.PropertyValues[key] = JsonSerializer.SerializeToElement(false);
        Assert.That(Check(Device(1), child).Issues, Has.Some.Matches<DriverReadinessIssue>(i => i != null && i.DeviceId == 2 && i.Reason == reason));
    }
    [Test]
    public void DetectsDifferentBuildAndMissingRoot()
    {
        var root = Device(1); root.PropertyValues["cp.driverInformation:version"] = JsonSerializer.SerializeToElement("2.0.001.0006");
        Assert.That(Check(root).Ready, Is.False);
        Assert.That(Check().Issues.Single().Reason, Is.EqualTo("root-missing"));
    }
    [Test]
    public void DiscoveryOnlyAndUnrelatedDriversAreNotTreatedAsInstalledChildren()
    {
        var root = Device(1); root.PropertyValues["platform:managedDevices"] = JsonSerializer.SerializeToElement(new[] { new { Id = "discovered-only" } });
        Assert.That(Check(root, new DeviceInfo { Id = 9, Model = "Other" }).Ready, Is.True);
    }
    [Test]
    public void NativeLightUsesWrapperAvailabilityAndActualControls()
    {
        var wrapper = new DeviceInfo { Id = 2, ParentDeviceId = 1, PropertyValues = new()
        {
            ["platform:managedDevices"] = JsonSerializer.SerializeToElement(new[] { new { Id = "physical" } }),
            ["onlineIndicator:isOnline"] = JsonSerializer.SerializeToElement(true),
            ["cp.driverConfiguration:driverLoadingStatus"] = JsonSerializer.SerializeToElement("Loaded")
        }};
        var light = new DeviceInfo { Id = 3, ParentDeviceId = 2, Commands = ["lightDimmer:setLevel"], PropertyValues = new()
        {
            ["lightType:variant"] = JsonSerializer.SerializeToElement("load"),
            ["lightDimmer:level"] = JsonSerializer.SerializeToElement(0.0)
        }};
        Assert.That(Check(Device(1), wrapper, light).Ready, Is.True);
        Assert.That(Check(Device(1), wrapper, light with { Commands = [] }).Ready, Is.False);
        wrapper.PropertyValues["onlineIndicator:isOnline"] = JsonSerializer.SerializeToElement(false);
        Assert.That(Check(Device(1), wrapper, light).Ready, Is.False);
    }
    [Test]
    public void ConfigurationReviewIsReportedWithoutLeakingValues()
    {
        var child = Device(2, 1);
        child.PropertyValues["cp.driverConfiguration:configurationItems"] = JsonSerializer.SerializeToElement(new[] { new { IsReviewNeeded = true, Value = "secret-do-not-log" } });
        var report = Check(Device(1), child);
        Assert.That(report.Issues.Single().Reason, Is.EqualTo("configuration-review-required"));
        Assert.That(JsonSerializer.Serialize(report), Does.Not.Contain("secret-do-not-log"));
    }
    [Test]
    public void RequiredControlsAndRetainedIdsMustStillMatch()
    {
        var inventory = new[] { Device(1), Device(2, 1) };
        Assert.That(DriverReadiness.Inspect(inventory, 1, "Platform", "2.1.2.0", new Dictionary<int, string[]> { [2] = ["extension:doCommand"] }).Ready, Is.False);
        Assert.That(DriverReadiness.Inspect(inventory, 1, "Platform", "2.1.2.0", new Dictionary<int, string[]> { [4] = [] }).Ready, Is.False);
    }
}
