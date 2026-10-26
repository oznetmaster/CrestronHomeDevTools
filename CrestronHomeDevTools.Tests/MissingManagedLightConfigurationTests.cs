// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License. See LICENSE.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class MissingManagedLightConfigurationTests
{
    private static readonly ManagedLightConfigurationIdentity Identity=new(20,10,"device_example","Demo dimmer","HS220(US)",3,"2.1.002.0004");
    private static JsonObject Fixture()
    {
        JsonObject Attribute(string id,string key,int type,string value,string usage)=>new(){["$id"]=id,["$type"]="Crestron.DeviceWrapper.RadUserAttribute, BaseDeviceWrapper",
            ["AttributeId"]=key,["RequiresPersistence"]=true,["AttributeType"]=4,["DataType"]=type,["PersistedValue"]=value,["SecurePersistedValue"]=null,["UsageContext"]=usage,["Flags"]=1,["AvailableValues"]=new JsonArray()};
        JsonObject Driver()=>new(){["DriverGuid"]="tp-link.kasatapoplatform.cloud.neilcolvin",["DriverVersion"]=Identity.DriverVersion,["DriverUxCategory"]="light"};
        JsonObject Parent()=>new(){["ParentInfo"]=new JsonObject{["$ref"]="1"}};
        var child=new JsonObject{["$id"]="2",["Id"]=20,["Name"]=Identity.Name,["Model"]=Identity.Model,["RoomId"]=3,["DevType"]=8,["InfoType"]=3,["DriverInformation"]=Driver(),
            ["ParentDeviceInfo"]=Parent(),["ConnectionInfos"]=new JsonArray(new JsonObject{["GatewayId"]=10,["GatewayManagedDeviceId"]=Identity.ManagedDeviceId}),
            ["UserAttributes"]=null,["ConfigurationNeeded"]=0,["FuturePrivateField"]=new JsonObject{["Token"]="private-example"}};
        return new(){["Devices"]=new JsonObject{
            ["10"]=new JsonObject{["$id"]="1",["Id"]=10,["Model"]="KasaTapoPlatform",["DriverInformation"]=Driver(),["ChildDevices"]=new JsonArray(child)},
            ["20"]=new JsonObject{["$ref"]="2"},
            ["30"]=new JsonObject{["$id"]="3",["Id"]=30,["DriverInformation"]=Driver(),["ParentDeviceInfo"]=Parent(),["ConfigurationNeeded"]=1,
                ["UserAttributes"]=new JsonArray(Attribute("4","ActivationMarker",3,"true","Generic.Prompt"),Attribute("5","DriverDataStore",1,"","Data"))}
        }};
    }
    private static MissingManagedLightConfigurationPlan Plan(JsonObject source)
    {
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(source);
        return MissingManagedLightConfiguration.Plan(bytes,Convert.ToHexString(SHA256.HashData(bytes)),Identity,30);
    }
    [Test]
    public void AddsMissingRecordsWithoutChangingIdsBindingsOrUnknownConfiguration()
    {
        var source=Fixture();string original=source.ToJsonString();var plan=Plan(source);
        string path=Path.Combine(Path.GetTempPath(),"devtools-candidate-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            plan.SavePrivateCandidate(path);var patched=JsonNode.Parse(File.ReadAllBytes(path))!;
            var child=patched["Devices"]!["10"]!["ChildDevices"]![0]!;
            Assert.That(child["UserAttributes"]!.AsArray().Select(a=>a!["AttributeId"]!.GetValue<string>()),Is.EqualTo(new[]{"ActivationMarker","DriverDataStore"}));
            Assert.That(child["UserAttributes"]!.AsArray().Select(a=>a!["$id"]!.GetValue<string>()),Is.EqualTo(new[]{"6","7"}));
            Assert.That(child["ConfigurationNeeded"]!.GetValue<int>(),Is.EqualTo(1));
            child["UserAttributes"]=null;child["ConfigurationNeeded"]=0;
            Assert.That(JsonNode.DeepEquals(source,patched),Is.True,"Only the missing attributes and validated configuration flag may change.");
            Assert.That(source.ToJsonString(),Is.EqualTo(original));
            Assert.Throws<IOException>(()=>plan.SavePrivateCandidate(path));
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
    [TestCase("binding")] [TestCase("version")] [TestCase("store")]
    [TestCase("existing")] [TestCase("duplicate-reference")] [TestCase("unknown-marker-field")] [TestCase("reference-overflow")]
    public void UnexpectedOrPartlyConfiguredRecordsAreRejected(string change)
    {
        var source=Fixture();var child=source["Devices"]!["10"]!["ChildDevices"]![0]!;
        switch(change)
        {
            case "binding":child["ConnectionInfos"]![0]!["GatewayManagedDeviceId"]="device_other";break;
            case "version":child["DriverInformation"]!["DriverVersion"]="2.1.002.0002";break;
            case "store":source["Devices"]!["30"]!["UserAttributes"]![1]!["PersistedValue"]="not-empty";break;
            case "existing":child["UserAttributes"]=new JsonArray(new JsonObject{["AttributeId"]="ActivationMarker",["PersistedValue"]="false"});break;
            case "duplicate-reference":source["Devices"]!["30"]!["$id"]="2";break;
            case "unknown-marker-field":source["Devices"]!["30"]!["UserAttributes"]![1]!["FuturePrivateState"]="must-not-copy";break;
            case "reference-overflow":source["Devices"]!["30"]!["$id"]=(int.MaxValue-1).ToString();break;
        }
        Assert.Throws<InvalidDataException>(()=>Plan(source));
    }
    [Test]
    public void UnrelatedUserAttributesAreRetained()
    {
        var source=Fixture();source["Devices"]!["10"]!["ChildDevices"]![0]!["UserAttributes"]=new JsonArray(new JsonObject{["AttributeId"]="UnrecognizedFutureSetting",["PersistedValue"]="retain-me"});
        Assert.That(Plan(source).AddedAttributes,Is.EqualTo(new[]{"ActivationMarker","DriverDataStore"}));
    }
    [Test]
    public void StaleSourceDigestIsRejected()
    {
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(Fixture());
        Assert.Throws<InvalidDataException>(()=>MissingManagedLightConfiguration.Plan(bytes,new string('0',64),Identity,30));
    }
    [Test]
    public void ValidatedEmbeddedTemplateDoesNotRequireAHealthyCustomerSibling()
    {
        var source=Fixture();source["Devices"]!.AsObject().Remove("30");byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(source);
        var plan=MissingManagedLightConfiguration.PlanForValidatedHome(bytes,Convert.ToHexString(SHA256.HashData(bytes)),Identity,"4.012.0194");
        var candidate=JsonNode.Parse(plan.CopyPrivateCandidateBytes())!;var child=candidate["Devices"]!["10"]!["ChildDevices"]![0]!;
        Assert.That(child["UserAttributes"]!.AsArray().Count,Is.EqualTo(2));
        Assert.That(child["UserAttributes"]![1]!["PersistedValue"]!.GetValue<string>(),Is.Empty);
        child["UserAttributes"]=null;child["ConfigurationNeeded"]=0;
        Assert.That(JsonNode.DeepEquals(source,candidate),Is.True);
    }
    [TestCase("4.011.322")] [TestCase("4.013.0000")]
    public void UnvalidatedHomeVersionsCannotUseTheEmbeddedTemplate(string version)
    {
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(Fixture());
        Assert.Throws<InvalidDataException>(()=>MissingManagedLightConfiguration.PlanForValidatedHome(bytes,Convert.ToHexString(SHA256.HashData(bytes)),Identity,version));
    }
}
