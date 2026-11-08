// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class InspectionCommandTests
    {
    [Test]
    public void DefaultsKeepSensitiveValuesAndReceiptsOptIn ()
        {
        var options = InspectionCommandOptions.Read ("refresh", new Dictionary<string, string> ());
        Assert.That (options.IncludeValues, Is.False); Assert.That (options.Receipt, Is.False);
        Assert.That (options.BufferCapacity, Is.EqualTo (32)); Assert.That (options.DurationSeconds, Is.EqualTo (30));
        }

    [Test]
    public void ExplicitOptionsAllowNegativeSystemParents ()
        {
        var options = InspectionCommandOptions.Read ("monitor-managed-children", new Dictionary<string, string>
            { ["parents"] = "-6,0,52001", ["include-values"] = "true", ["duration"] = "1", ["buffer"] = "256" });
        Assert.That (options.Parents, Is.EqualTo (new[] { -6, 0, 52001 }));
        Assert.That (options.IncludeValues, Is.True); Assert.That (options.BufferCapacity, Is.EqualTo (256));
        }

    [TestCase ("include-values", "yes")] [TestCase ("receipt", "1")]
    [TestCase ("duration", "0")] [TestCase ("duration", "3601")]
    [TestCase ("buffer", "0")] [TestCase ("buffer", "257")]
    [TestCase ("parents", "7,7")]
    [TestCase ("parents", "7,")] [TestCase ("parents", "nonsense")]
    public void InvalidOptionsAreRejectedBeforeConnection (string key, string value)
        {
        var options = new Dictionary<string, string> { ["parents"] = "7", [key] = value };
        Assert.Throws<ArgumentException> (() => InspectionCommandOptions.Read ("monitor-managed-children", options));
        }

    [Test]
    public async Task SnapshotOnlyConnectionProducesNoSyntheticChangesAndDoesNotRevealValues ()
        {
        var device = new DeviceInfo { Id = -6, PropertyValues = new () {
            ["cp.platformController:managedDevicesExtended"] = JsonSerializer.Deserialize<JsonElement> ("""[{"Id":"opaque","PropertyValues":{"secret":"private-sentinel"}}]""") } };
        var output = new StringWriter ();
        var summary = await InspectionCommands.MonitorAsync (new ConfigurationClient (new ProcessorCapabilitiesTests.ReadOnlyConnection (device)),
            InspectionCommandOptions.Read ("monitor-managed-children", new Dictionary<string, string> { ["parents"] = "-6" }), output, default);
        Assert.That (output.ToString ().Trim ().Split ('\n').Length, Is.EqualTo (1));
        Assert.That (output.ToString (), Does.Contain ("InitialSnapshots").And.Not.Contain ("ManagedChildStateChange").And.Not.Contain ("private-sentinel"));
        var result = JsonSerializer.SerializeToElement (summary);
        Assert.That (result.GetProperty ("EventCount").GetInt32 (), Is.Zero);
        Assert.That (result.GetProperty ("HasObservedStateEvent").GetBoolean (), Is.False);
        }
    }
