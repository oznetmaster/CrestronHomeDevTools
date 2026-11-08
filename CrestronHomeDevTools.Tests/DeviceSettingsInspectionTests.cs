// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DeviceSettingsInspectionTests
    {
    private static JsonElement Json (string text) => JsonSerializer.Deserialize<JsonElement> (text);
    private static DeviceInfo Device (string? groups = null, string? items = null, bool encoded = false)
        {
        var properties = new Dictionary<string, JsonElement> { ["enabled"] = Json ("false"), ["secret"] = Json ("\"private-sentinel\""), ["explicitNull"] = Json ("null") };
        if (groups != null) properties["deviceSettings:groups"] = encoded ? JsonSerializer.SerializeToElement (groups) : Json (groups);
        if (items != null) properties["deviceSettings:items"] = encoded ? JsonSerializer.SerializeToElement (items) : Json (items);
        return new () { Id = -7, Commands = ["deviceSettings:setValue"], PropertyValues = properties };
        }

    [Test]
    public async Task OlderDeviceWithoutDynamicSettingsRemainsReadableWithoutCommands ()
        {
        var connection = new ProcessorCapabilitiesTests.ReadOnlyConnection (Device ());
        var snapshot = await new ConfigurationClient (connection).InspectDeviceSettingsAsync (-7);
        Assert.That (snapshot.GroupsAdvertised, Is.False); Assert.That (snapshot.ItemsAvailable, Is.False);
        Assert.That (snapshot.Items, Is.Empty);
        Assert.That (connection.Reads, Is.EqualTo (new[] { "v2/Devices/-7" }));
        }

    [TestCase (false)] [TestCase (true)]
    public async Task TypedMetadataRetainsParentFlagsAndUnknownHintsWithoutInventingTypes (bool encoded)
        {
        var device = Device ("""[{"Id":"general","Name":"General","Parent":"root","Icons":["ignored"],"Future":"ignored"}]""",
            """
            [{"Property":"enabled","Parent":"general","IsAvailable":true,"ReadOnly":false,"Description":"Enabled","Command":"deviceSettings:setValue","RenderHint":99},
             {"Property":"secret","Command":"unadvertised","RenderHint":"FutureHint"},{"Property":"missing"},{"Property":"explicitNull"}]
            """, encoded);
        var result = await new ConfigurationClient (new ProcessorCapabilitiesTests.ReadOnlyConnection (device)).InspectDeviceSettingsAsync (-7);
        Assert.That (result.Groups.Single ().Parent, Is.EqualTo ("root"));
        Assert.That (result.Items[0].RenderHint, Is.EqualTo ("99"));
        Assert.That (result.Items[0].CommandAdvertised, Is.True);
        Assert.That (result.Items[0].ReadOnly, Is.False);
        Assert.That (result.Items[1].CommandAdvertised, Is.False);
        Assert.That (result.Items[1].IsAvailable, Is.Null);
        Assert.That (result.Items[1].RenderHint, Is.EqualTo ("FutureHint"));
        Assert.That (result.Items[2].HasValue, Is.False);
        Assert.That (result.Items[2].ObservedValueKind, Is.Null);
        Assert.That (result.Items[3].HasValue, Is.True);
        Assert.That (result.Items[3].ObservedValueKind, Is.EqualTo ("Null"));
        Assert.That (result.ValuesIncluded, Is.False);
        Assert.That (result.Items.All (i => i.Value == null), Is.True);
        Assert.That (JsonSerializer.Serialize (result), Does.Not.Contain ("private-sentinel"));
        }

    [TestCase ("null", false)] [TestCase ("[]", true)]
    public void NullAndEmptyMetadataAreDistinct (string metadata, bool available)
        {
        var result = DeviceSettingsInspection.FromDevice (Device (metadata, metadata), false);
        Assert.That (result.GroupsAdvertised, Is.True); Assert.That (result.ItemsAdvertised, Is.True);
        Assert.That (result.GroupsAvailable, Is.EqualTo (available)); Assert.That (result.ItemsAvailable, Is.EqualTo (available));
        }

    [Test]
    public void OptedInValuesRetainTheirActualJsonTypesIncludingNullAndStringLookingLikeJson ()
        {
        var device = Device (items: """[{"Property":"enabled"},{"Property":"secret"},{"Property":"explicitNull"},{"Property":"object"},{"Property":"jsonString"}]""");
        device.PropertyValues["object"] = Json ("""{"x":[1,true,null]}""");
        device.PropertyValues["jsonString"] = JsonSerializer.SerializeToElement ("{\"x\":1}");
        var result = DeviceSettingsInspection.FromDevice (device, true);
        Assert.That (result.Items[0].Value!.Value.GetBoolean (), Is.False);
        Assert.That (result.Items[1].Value!.Value.GetString (), Is.EqualTo ("private-sentinel"));
        Assert.That (result.Items[2].Value!.Value.ValueKind, Is.EqualTo (JsonValueKind.Null));
        Assert.That (result.Items[3].Value!.Value.GetProperty ("x").GetArrayLength (), Is.EqualTo (3));
        Assert.That (result.Items[4].Value!.Value.ValueKind, Is.EqualTo (JsonValueKind.String));
        }

    [TestCase ("{}", "[]")] [TestCase ("[null]", "[]")] [TestCase ("[{\"Id\":\"\"}]", "[]")]
    [TestCase ("[{\"Id\":\"x\"},{\"Id\":\"x\"}]", "[]")] [TestCase ("[]", "{}")]
    [TestCase ("[]", "[null]")] [TestCase ("[]", "[{\"Property\":\"\"}]")]
    [TestCase ("[]", "[{\"Property\":\"x\"},{\"Property\":\"x\"}]")]
    [TestCase ("[]", "[{\"Property\":\"x\",\"ReadOnly\":\"private-sentinel\"}]")]
    [TestCase ("[]", "[{\"Property\":\"x\",\"RenderHint\":{}}]")]
    public void MalformedMetadataFailsWithoutExposingPrivateValues (string groups, string items)
        {
        var error = Assert.Throws<InvalidDataException> (() => DeviceSettingsInspection.FromDevice (Device (groups, items), true));
        Assert.That (error!.ToString (), Does.Not.Contain ("private-sentinel"));
        }

    [Test]
    public void SamePropertyInDifferentGroupsIsNotAmbiguous ()
        {
        var result = DeviceSettingsInspection.FromDevice (Device (items: """[{"Property":"enabled","Parent":"one"},{"Property":"enabled","Parent":"two"}]"""), false);
        Assert.That (result.Items.Count, Is.EqualTo (2));
        }

    [Test]
    public async Task ZeroSystemIdIsReadable ()
        {
        var connection = new ProcessorCapabilitiesTests.ReadOnlyConnection (Device () with { Id = 0 });
        var result = await new ConfigurationClient (connection).InspectDeviceSettingsAsync (0);
        Assert.That (result.DeviceId, Is.Zero);
        Assert.That (connection.Reads, Is.EqualTo (new[] { "v2/Devices/0" }));
        }
    }
