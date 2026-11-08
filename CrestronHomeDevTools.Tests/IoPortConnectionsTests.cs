// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class IoPortConnectionsTests
    {
    [TestCase ("4.007.0001", IoPortConnectionFormat.Legacy)]
    [TestCase ("4.010.0260", IoPortConnectionFormat.Legacy)]
    [TestCase ("4.012.0194", IoPortConnectionFormat.Legacy)]
    [TestCase ("4.013.0134", IoPortConnectionFormat.Typed)]
    [TestCase ("4.014.0001", IoPortConnectionFormat.Unknown)]
    [TestCase ("5.0.0", IoPortConnectionFormat.Unknown)]
    [TestCase ("4.6.0", IoPortConnectionFormat.Unknown)]
    [TestCase ("unknown", IoPortConnectionFormat.Unknown)]
    [TestCase (null, IoPortConnectionFormat.Unknown)]
    public void FormatUsesHomeVersionNotSchemaOrReleaseChannel (string? version, IoPortConnectionFormat expected)
        => Assert.That (IoPortConnections.FormatForVersion (version), Is.EqualTo (expected));

    [TestCase ("52001|1", null)]
    [TestCase ("52001|Serial|1", "Serial")]
    [TestCase ("52001|Com|1", "Serial")]
    [TestCase ("52001|FuturePort|1", "FuturePort")]
    public void BothReadFormatsPreserveTypeInformation (string value, string? type)
        => Assert.That (IoPortConnections.Parse (value), Is.EqualTo (new IoPortConnection (52001, type, 1)));

    [TestCase ("password|user")]
    [TestCase ("52001|Serial|-1")]
    [TestCase ("52001|Serial|4294967296")]
    [TestCase ("52001||1")]
    [TestCase ("52001|123|1")]
    [TestCase ("52001|Serial|1|2")]
    [TestCase ("52001| 1")]
    public void ArbitraryConfigurationStringsAreNotPortConnections (string value)
        => Assert.Throws<FormatException> (() => IoPortConnections.Parse (value));

    [Test]
    public void KnownLegacyAndTypedFormattingIsCultureIndependentAndDoesNotGuessMissingTypes ()
        {
        using var culture = new CultureScope ("ar-SA");
        var selection = new IoPortConnection (52001, "Com", 1);
        Assert.That (IoPortConnections.Format (selection, IoPortConnectionFormat.Legacy), Is.EqualTo ("52001|1"));
        Assert.That (IoPortConnections.Format (selection, IoPortConnectionFormat.Typed), Is.EqualTo ("52001|Serial|1"));
        Assert.Throws<InvalidOperationException> (() => IoPortConnections.Format (selection, IoPortConnectionFormat.Unknown));
        Assert.Throws<InvalidOperationException> (() => IoPortConnections.Format (selection with { PortType = null }, IoPortConnectionFormat.Typed));
        Assert.Throws<InvalidOperationException> (() => IoPortConnections.Format (selection with { PortType = "FuturePort" }, IoPortConnectionFormat.Typed));
        }

    private static readonly DriverInstanceReady Target = new (100, "Mock", "1.0.0.0", "Inspect");
    private static ProcessorCapabilitiesTests.IdentityConnection Setup (string version = "4.013.0134", string usage = "com", string portType = "Serial")
        => new (version, new () { Id = 52, PropertyValues = new () { ["cp.ioPorts"] = JsonSerializer.SerializeToElement (new[] { new { Type = portType, PortNumber = 1 } }) } },
            new () { Id = Target.DeviceId, Model = Target.Model, PropertyValues = new () {
                ["cp.driverInformation:version"] = JsonSerializer.SerializeToElement (Target.Version),
                ["cp.driverConfiguration:configurationItems"] = JsonSerializer.SerializeToElement (new[] { new { Id = "port", UsageContext = "cp.driverConfigurationUsageContext:" + usage + ".port", Value = new { ReadOnly = false, CurrentValue = "do-not-change" } } }) } });

    [TestCase ("4.012.0194", "52|1")]
    [TestCase ("4.013.0134", "52|Serial|1")]
    public async Task PreparationSelectsCorrectWireValueWithoutApplyingOrChangingInputs (string version, string expected)
        {
        var connection = Setup (version);
        var before = JsonSerializer.Serialize (connection.Devices);
        var value = await IoPortConnections.PrepareValueAsync (new (connection), Target, "port", new (52, "Serial", 1));
        Assert.That (value, Is.EqualTo (expected));
        Assert.That (JsonSerializer.Serialize (connection.Devices), Is.EqualTo (before));
        Assert.That (connection.Reads, Is.EqualTo (new[] { "v2/Devices", "v2/Devices/100" }));
        }

    [TestCase ("unknownVersion")]
    [TestCase ("wrongTarget")]
    [TestCase ("unknownItem")]
    [TestCase ("missingItems")]
    [TestCase ("readOnly")]
    [TestCase ("wrongUsage")]
    [TestCase ("unknownProvider")]
    [TestCase ("missingPorts")]
    [TestCase ("duplicatePort")]
    [TestCase ("wrongNumber")]
    [TestCase ("missingType")]
    [TestCase ("futureType")]
    public async Task UnconfirmedSelectionsNeverIssueACommand (string scenario)
        {
        var connection = Setup (scenario == "unknownVersion" ? "4.14.1" : "4.013.0134", scenario == "wrongUsage" ? "network" : "com");
        var selection = new IoPortConnection (scenario == "unknownProvider" ? 999 : 52,
            scenario == "missingType" ? null : scenario == "futureType" ? "FuturePort" : "Serial", scenario == "wrongNumber" ? 2u : 1u);
        if (scenario == "missingPorts") connection.Devices[0].PropertyValues.Remove ("cp.ioPorts");
        if (scenario == "duplicatePort") connection.Devices[0].PropertyValues["cp.ioPorts"] = JsonSerializer.SerializeToElement (new[] { new { Type = "Serial", PortNumber = 1 }, new { Type = "Serial", PortNumber = 1 } });
        if (scenario == "missingItems") connection.Devices[1].PropertyValues.Remove ("cp.driverConfiguration:configurationItems");
        if (scenario == "readOnly") connection.Devices[1].PropertyValues["cp.driverConfiguration:configurationItems"] = JsonSerializer.SerializeToElement (new[] { new { Id = "port", UsageContext = "cp.driverConfigurationUsageContext:com.port", Value = new { ReadOnly = true } } });
        await Assert.ThrowsAsync<InvalidOperationException> (async () => await IoPortConnections.PrepareValueAsync (new (connection),
            scenario == "wrongTarget" ? Target with { Model = "Other" } : Target, scenario == "unknownItem" ? "unknown" : "port", selection));
        }

    [TestCase ("cec", "CecInput")]
    [TestCase ("cec", "CecOutput")]
    [TestCase ("cecInput", "CecInput")]
    [TestCase ("cecOutput", "CecOutput")]
    public async Task CecDirectionIsExplicitAndMustMatchAdvertisedPort (string usage, string type)
        {
        var connection = Setup (usage: usage, portType: type);
        Assert.That (await IoPortConnections.PrepareValueAsync (new (connection), Target, "port", new (52, type, 1)), Is.EqualTo ($"52|{type}|1"));
        await Assert.ThrowsAsync<InvalidOperationException> (async () => await IoPortConnections.PrepareValueAsync (new (connection), Target, "port", new (52, "Cec", 1)));
        }

    [Test]
    public async Task ProcessorVersionIsReadAgainWhenPreparingTheNextValue ()
        {
        var connection = Setup ("4.012.0194");
        await using var client = new ConfigurationClient (connection);
        Assert.That (await IoPortConnections.PrepareValueAsync (client, Target, "port", new (52, "Serial", 1)), Is.EqualTo ("52|1"));
        connection.Version = "4.013.0134";
        Assert.That (await IoPortConnections.PrepareValueAsync (client, Target, "port", new (52, "Serial", 1)), Is.EqualTo ("52|Serial|1"));
        }

    [Test]
    public async Task GenericCecOnLegacyCannotDropAnAmbiguousDirection ()
        {
        var connection = Setup ("4.012.0194", "cec", "CecInput");
        connection.Devices[0].PropertyValues["cp.ioPorts"] = JsonSerializer.SerializeToElement (new[] {
            new { Type = "CecInput", PortNumber = 1 }, new { Type = "CecOutput", PortNumber = 1 } });
        await Assert.ThrowsAsync<InvalidOperationException> (async () => await IoPortConnections.PrepareValueAsync (new (connection), Target, "port", new (52, "CecInput", 1)));
        connection.Version = "4.013.0134";
        Assert.That (await IoPortConnections.PrepareValueAsync (new (connection), Target, "port", new (52, "CecInput", 1)), Is.EqualTo ("52|CecInput|1"));
        }

    [Test]
    public async Task CancellationDoesNotStartConfigurationOrContinueInventoryReads ()
        {
        var connection = Setup ();
        using var cancelled = new CancellationTokenSource ();
        cancelled.Cancel ();
        await Assert.ThrowsAsync<OperationCanceledException> (async () => await IoPortConnections.PrepareValueAsync (new (connection), Target, "port", new (52, "Serial", 1), cancelled.Token));
        Assert.That (connection.Reads, Is.Empty);
        }

    private sealed class CultureScope : IDisposable
        {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
        public CultureScope (string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo (name);
        public void Dispose () => CultureInfo.CurrentCulture = _previous;
        }
    }
