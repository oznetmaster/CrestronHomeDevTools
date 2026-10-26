// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License. See LICENSE.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ManagedChildReplacementTests
{
    private string _folder = null!;
    private static readonly ConfigurationCaptureIdentity Identity = new("192.0.2.1",new string('A',64),1,"MC4-R","4.012.0194");
    private static readonly ManagedDeviceRequest Request = new(10,"Example platform","2.1.2.4","device_example","Demo light","L530",3);
    [SetUp] public void Setup() => _folder = Path.Combine(Path.GetTempPath(),"devtools-replacement-test-"+Guid.NewGuid().ToString("N"));
    [TearDown] public void Cleanup()
    {
        if (Path.GetFullPath(_folder).StartsWith(Path.Combine(Path.GetTempPath(),"devtools-replacement-test-"),StringComparison.OrdinalIgnoreCase) && Directory.Exists(_folder)) Directory.Delete(_folder,true);
    }
    private async Task<ValidatedConfigurationCapture> Capture(ConfigurationClient client)
    {
        string path = Path.Combine(_folder,"capture");
        await LiveConfigurationCapture.CaptureAsync(client,Identity,path);
        return LiveConfigurationCapture.ReadValidated(path,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(path,"capture.json")))));
    }
    [Test]
    public async Task RecommissionsFreshIdentityAndRestoresSceneWithoutReplayingOldBindingRecords()
    {
        var wire = new Connection(); await using var client = new ConfigurationClient(wire);
        var capture = await Capture(client); var plan = ManagedChildReplacement.Plan(capture,Request,20);
        var result = await ManagedChildReplacement.ReplaceAsync(client,capture,plan,Path.Combine(_folder,"repair"),null,TimeSpan.FromSeconds(5));
        Assert.That(result.NewWrapperId,Is.EqualTo(40)); Assert.That(result.NewLoadId,Is.EqualTo(41));
        Assert.That(wire.TargetId,Is.EqualTo(41));
        Assert.That(wire.Mutations,Is.EqualTo(new[]{"cp.deviceConfiguration:setLocation","cp.platformController:commissionManagedDevice","cp.scene:updateSceneTargets"}));
        Assert.That(result.State,Is.EqualTo("RecommissionedAwaitingPersistenceVerification"));
        Assert.That(File.Exists(Path.Combine(_folder,"repair","restoration","complete.json")),Is.True);
    }
    [TestCase("scene")] [TestCase("identity")]
    public async Task ChangedConfigurationStopsBeforeRemovingAnything(string change)
    {
        var wire = new Connection(); await using var client = new ConfigurationClient(wire);
        var capture = await Capture(client); var plan = ManagedChildReplacement.Plan(capture,Request,20);
        if (change == "scene") wire.Level = .5; else wire.Devices[20] = wire.Devices[20] with { Name = "Changed" };
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await ManagedChildReplacement.ReplaceAsync(client,capture,plan,Path.Combine(_folder,"repair"),null,TimeSpan.FromSeconds(5)));
        Assert.That(wire.Mutations,Is.Empty);
    }
    [Test]
    public async Task UnknownGroupReferenceRejectsPlanBeforeRemoval()
    {
        var wire = new Connection { GroupReference = true }; await using var client = new ConfigurationClient(wire);
        var capture = await Capture(client);
        Assert.Throws<InvalidDataException>(()=>ManagedChildReplacement.Plan(capture,Request,20)); Assert.That(wire.Mutations,Is.Empty);
    }
    [Test]
    public async Task UncertainRemovalIsJournaledAndNeverAutomaticallyRetried()
    {
        var wire = new Connection { FailRemoval = true }; await using var client = new ConfigurationClient(wire);
        var capture = await Capture(client); var plan = ManagedChildReplacement.Plan(capture,Request,20);
        string journal = Path.Combine(_folder,"repair");
        await Assert.ThrowsAsync<IOException>(async()=>await ManagedChildReplacement.ReplaceAsync(client,capture,plan,journal,null,TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<IOException>(async()=>await ManagedChildReplacement.ReplaceAsync(client,capture,plan,journal,null,TimeSpan.FromSeconds(5)));
        Assert.That(wire.Mutations,Is.EqualTo(new[]{"cp.deviceConfiguration:setLocation"}));
        Assert.That(File.Exists(Path.Combine(journal,"remove-intent.json")),Is.True);
        Assert.That(File.Exists(Path.Combine(journal,"failure.json")),Is.True);
    }
    [Test]
    public async Task CustomizedVoiceSettingsAreRejectedBeforeRemoval()
    {
        var wire = new Connection(); wire.Devices[21].PropertyValues["cp.deviceVoiceControl:description"] = JsonSerializer.SerializeToElement("Custom name");
        await using var client = new ConfigurationClient(wire); var capture = await Capture(client);
        Assert.Throws<InvalidDataException>(()=>ManagedChildReplacement.Plan(capture,Request,20)); Assert.That(wire.Mutations,Is.Empty);
    }
    [Test]
    public void ProgrammingComparisonIgnoresGeneratedIdsButPreservesDeviceTargetsAndArguments()
    {
        var a = Copy(100,101,21); var b = Copy(200,201,21);
        Assert.That(ConfigurationProgramming.SemanticallyEqual(a,b),Is.True);
        Assert.That(ConfigurationProgramming.SemanticallyEqual(a,Copy(200,201,22)),Is.False);
        Assert.That(ConfigurationProgramming.SemanticallyEqual(a,Copy(200,201,21,1)),Is.False);
    }
    [Test]
    public void UnvalidatedNestedSequenceIsRejected()
    {
        var copy = JsonNode.Parse(Copy(100,101,21).GetRawText())!;
        var data = JsonNode.Parse(copy["ProgrammableEvents"]![0]!["Programming"]!["TriggerProgrammingData"]![0]!["ProgrammingData"]!.GetValue<string>())!;
        var block = JsonNode.Parse(data["SequenceProgrammingBlockJson"]!.GetValue<string>())!; block["IsNested"] = true;
        data["SequenceProgrammingBlockJson"] = block.ToJsonString();
        copy["ProgrammableEvents"]![0]!["Programming"]!["TriggerProgrammingData"]![0]!["ProgrammingData"] = data.ToJsonString();
        Assert.Throws<InvalidDataException>(()=>ConfigurationProgramming.Validate(JsonSerializer.SerializeToElement(copy)));
    }
    internal static JsonElement Copy(int blockId,int stepId,int target,int argument=0) => JsonSerializer.SerializeToElement(new {
        ProgrammableEvents = new[]{new { Programming = new { Mode = new { Type = "Sequence" }, ModeOptions = Array.Empty<object>(), TriggerProgrammingData = new[]{new {
            TriggerGesture = new { Trigger = "Default", Gesture = "Default", ToggleState = "None" },
            ProgrammingData = JsonSerializer.Serialize(new { ModeActionItems = Array.Empty<object>(), SequenceProgrammingBlockJson = JsonSerializer.Serialize(new {
                Id = blockId, ProgrammableEntity = new { Id = 50, EntityType = 2 }, EventId = "pressed", IsNested = false,
                Steps = new[]{new { Id = stepId, Type = 0, Target = new { Id = target, EntityType = 2 }, Operation = new { Id = "native:Off" }, Arguments = new[]{argument} }}
            }) })
        }} }, Engraving = (string?)null }}
    });
    private sealed class Connection : IConfigurationConnection
    {
        public readonly Dictionary<int,DeviceInfo> Devices = new(); public readonly List<string> Mutations = [];
        public int TargetId = 21; public double Level; public bool GroupReference,FailRemoval;
        private static JsonElement J(object? value)=>JsonSerializer.SerializeToElement(value);
        public Connection()
        {
            Devices[1] = new(){Id=1,Model="MC4-R"};
            Devices[10] = new(){Id=10,Model=Request.ParentModel,Name="Platform",ParentDeviceId=-6,LocationId=3,
                Commands=["cp.platformController:commissionManagedDevice"],PropertyValues=new(){["cp.driverInformation:version"]=J(Request.ParentVersion),["platform:managedDevices"]=J(new[]{new{Id=Request.ManagedDeviceId}})}};
            AddLight(20,21);
        }
        private void AddLight(int wrapper,int load)
        {
            Devices[wrapper] = new(){Id=wrapper,ParentDeviceId=10,Name=Request.Name,Model=Request.ChildModel,PropertyValues=new(){
                ["platform:managedDevices"]=J(new[]{new{Id=Request.ManagedDeviceId}}),["onlineIndicator:isOnline"]=J(true),["cp.driverConfiguration:driverLoadingStatus"]=J("Loaded")}};
            Devices[load] = new(){Id=load,ParentDeviceId=wrapper,Name=Request.Name,Model=Request.ChildModel,LocationId=3,Commands=["cp.deviceConfiguration:setLocation","lightDimmer:setLevel"],PropertyValues=new(){
                ["lightType:variant"]=J("load"),["cp.deviceVoiceControl:enabled"]=J(true),["cp.deviceVoiceControl:description"]=J(""),["lightDimmer:level"]=J(0)}};
        }
        private static T? Convert<T>(object? value)=>value==null?default:J(value).Deserialize<T>();
        public Task<T?> GetAsync<T>(string path,CancellationToken cancellationToken=default)
        {
            object? result = path switch {
                "v2/Devices"=>Devices.ToDictionary(p=>p.Key.ToString(),p=>p.Value),"v2/Locations"=>new[]{new{Id=3,Name="Office"}},
                "v2/Scenes"=>new{LightScenes=new[]{new{Id=9,Commands=new[]{"cp.scene:getCurrentTargets","cp.scene:getSceneDetails"}}}},
                "v2/Groups"=>GroupReference?new{UnknownId=21}:(object)new{},"v2/QuickActions"=>Array.Empty<object>(),"v2/Media"=>new{},
                "v2/Programming"=>new[]{"getProgrammableEntitiesWithEvents","getProgrammableEvents","getMode"}.Select(n=>"cp.programming:"+n).ToArray(),
                _=>Devices.GetValueOrDefault(int.Parse(path.Split('/').Last()))
            }; return Task.FromResult(Convert<T>(result));
        }
        public Task<T?> ExecuteResourceCommandAsync<T>(string path,string command,object? parameters=null,CancellationToken cancellationToken=default)
        {
            object? result = command switch {
                "cp.scene:getCurrentTargets"=>new{Targets=new[]{new{Id=TargetId,Type="light",TargetLevel=Level}}},"cp.scene:getSceneDetails"=>null,
                "cp.programming:getProgrammableEntitiesWithEvents"=>Array.Empty<object>(),
                "cp.scene:updateSceneTargets"=>UpdateTargets(parameters),
                _=>throw new InvalidDataException("Unexpected command.")
            };return Task.FromResult(Convert<T>(result));
        }
        private object? UpdateTargets(object? parameters)
        {
            Mutations.Add("cp.scene:updateSceneTargets"); TargetId=J(parameters).GetProperty("targets")[0].GetProperty("Id").GetInt32(); return null;
        }
        public Task<T?> ExecuteAsync<T>(int deviceId,string commandName,object? parameters=null,CancellationToken cancellationToken=default)
        {
            Mutations.Add(commandName); object? result = null;
            if (commandName=="cp.deviceConfiguration:setLocation")
            { if(FailRemoval)throw new IOException("Uncertain outcome."); Assert.That(deviceId,Is.EqualTo(21)); Devices.Remove(21);Devices.Remove(20); }
            else if(commandName=="cp.platformController:commissionManagedDevice")
            { Assert.That(deviceId,Is.EqualTo(10)); AddLight(40,41);result=new{Id=40,CommissioningResult="Success"}; }
            else throw new InvalidDataException("Unexpected mutation.");
            return Task.FromResult(Convert<T>(result));
        }
        public Task<OperationResult> WaitForOperationAsync(string operationId,TimeSpan timeout,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
