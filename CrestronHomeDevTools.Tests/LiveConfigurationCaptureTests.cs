// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License. See LICENSE.
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class LiveConfigurationCaptureTests
{
    private string _folder=null!;
    private static readonly ConfigurationCaptureIdentity Identity=new("192.0.2.1",new string('A',64),1,"MC4-R","4.012.0194");
    [SetUp] public void Setup()=>_folder=Path.Combine(Path.GetTempPath(),"devtools-capture-test-"+Guid.NewGuid().ToString("N"));
    [TearDown] public void Cleanup()
    {
        if(_folder!=null && Path.GetFullPath(_folder).StartsWith(Path.Combine(Path.GetTempPath(),"devtools-capture-test-"),StringComparison.OrdinalIgnoreCase) && Directory.Exists(_folder))Directory.Delete(_folder,true);
    }
    private string Digest()=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_folder,"capture.json"))));

    [TestCase(false)] [TestCase(true)]
    public async Task CaptureRetainsRawConfigurationAndProgrammingWithoutSendingMutationCommands(bool sequence)
    {
        var wire=new CaptureConnection{Sequence=sequence};await using var client=new ConfigurationClient(wire);
        var manifest=await LiveConfigurationCapture.CaptureAsync(client,Identity,_folder);
        var read=LiveConfigurationCapture.ReadValidated(_folder,Digest());
        Assert.That(read.Manifest,Is.EqualTo(manifest).Using<ConfigurationCaptureManifest>((a,b)=>JsonSerializer.Serialize(a)==JsonSerializer.Serialize(b)));
        Assert.That(read.Values.Single(v=>v.Entry.ResourcePath=="v2/Devices").Value.GetProperty("2").GetProperty("UnknownFutureConfiguration").GetString(),Is.EqualTo("example-account-value"));
        Assert.That(wire.Queries.All(q=>q.Command.Contains(":get",StringComparison.Ordinal)||q.Command=="cp.programming:copyProgramming"),Is.True);
        Assert.That(read.Values.Count(v=>v.Entry.Command=="cp.programming:copyProgramming"),Is.EqualTo(sequence?1:0));
    }
    [Test]
    public async Task CaptureRejectsChangingInventoryAndKeepsAnIncompleteCapture()
    {
        var wire=new CaptureConnection{ChangeInventory=true};await using var client=new ConfigurationClient(wire);
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await LiveConfigurationCapture.CaptureAsync(client,Identity,_folder));
        Assert.That(File.Exists(Path.Combine(_folder,"capture.json")),Is.False);
        Assert.That(File.Exists(Path.Combine(_folder,"capture-failure.json")),Is.True);
    }
    [Test]
    public async Task ExistingCaptureIsNotOverwritten()
    {
        Directory.CreateDirectory(_folder);File.WriteAllText(Path.Combine(_folder,"owned.txt"),"preserve");
        await using var client=new ConfigurationClient(new CaptureConnection());
        await Assert.ThrowsAsync<IOException>(async()=>await LiveConfigurationCapture.CaptureAsync(client,Identity,_folder));
        Assert.That(File.ReadAllText(Path.Combine(_folder,"owned.txt")),Is.EqualTo("preserve"));
    }
    [TestCase("entry")] [TestCase("extra")] [TestCase("manifest")]
    public async Task ValidationRejectsAlteredOrExtraSnapshotData(string change)
    {
        await using var client=new ConfigurationClient(new CaptureConnection());
        var manifest=await LiveConfigurationCapture.CaptureAsync(client,Identity,_folder);string pin=Digest();
        if(change=="entry")File.WriteAllText(Path.Combine(_folder,manifest.Entries[0].File),"{}");
        else if(change=="extra")File.WriteAllText(Path.Combine(_folder,"extra.json"),"{}");
        else File.AppendAllText(Path.Combine(_folder,"capture.json")," ");
        Assert.Throws<InvalidDataException>(()=>LiveConfigurationCapture.ReadValidated(_folder,pin));
    }
    [TestCase("https://example.com")] [TestCase("v2/../Scenes")] [TestCase("v2/Scenes?x=1")] [TestCase("v2/Scenes/Command")] [TestCase("v2/Scenes\\52025")]
    public void InvalidResourcePathsCannotReachTheConnection(string path)
    {
        var wire=new CaptureConnection();var client=new ConfigurationClient(wire);
        Assert.Throws<ArgumentException>(()=>client.ExecuteConfigurationCommandAsync(path,"cp.scene:getCurrentTargets"));
        Assert.That(wire.Queries,Is.Empty);
    }
    [Test]
    public async Task NullResourceCommandResultIsPreserved()
    {
        await using var client=new ConfigurationClient(new CaptureConnection());
        Assert.That(await client.ExecuteConfigurationCommandAsync("v2/Scenes/9","cp.scene:getSceneDetails"),Is.Null);
    }
    private sealed class CaptureConnection:IConfigurationConnection
    {
        internal bool Sequence,ChangeInventory;private int _inventoryReads;
        internal readonly List<(string Path,string Command)> Queries=[];
        private static readonly object Entity=new{Id=2,EntityType="Device",ComponentId=(string?)null,ParentComponentId=(string?)null,Name="Demo",LocationId=3};
        private static T? Convert<T>(object? value)=>value==null?default:JsonSerializer.SerializeToElement(value).Deserialize<T>();
        public Task<T?> GetAsync<T>(string path,CancellationToken cancellationToken=default)
        {
            object result=path switch
            {
                "v2/Devices"=>new Dictionary<string,object>{{"1",new{Id=1,Model="MC4-R"}},{"2",new{Id=2,Name=ChangeInventory&&++_inventoryReads>1?"changed":"Demo",Model="Light",ParentDeviceId=10,LocationId=3,UnknownFutureConfiguration="example-account-value"}}},
                "v2/Locations"=>new[]{new{Id=3,Name="Office"}},
                "v2/Scenes"=>new{LightScenes=new[]{new{Id=9,Commands=new[]{"cp.scene:getCurrentTargets","cp.scene:getSceneDetails"}}}},
                "v2/Groups"=>new{LightGroups=new{}},"v2/QuickActions"=>Array.Empty<object>(),"v2/Media"=>new{},
                "v2/Programming"=>new[]{"getProgrammableEntitiesWithEvents","getProgrammableEvents","getMode","getAvailableModes","getModeTriggers","getModeSupportedGestures","getModeOptions","getSequenceProgrammingBlock","copyProgramming"}.Select(n=>"cp.programming:"+n).ToArray(),
                _=>throw new InvalidDataException("Unexpected read.")
            };return Task.FromResult(Convert<T>(result));
        }
        public Task<T?> ExecuteResourceCommandAsync<T>(string path,string command,object? parameters=null,CancellationToken cancellationToken=default)
        {
            Queries.Add((path,command));object? response=command switch
            {
                "cp.scene:getCurrentTargets"=>new{Targets=new[]{new{Id=2,Type="light",TargetLevel=0.0}}},"cp.scene:getSceneDetails"=>null,
                "cp.programming:getProgrammableEntitiesWithEvents"=>new[]{Entity},
                "cp.programming:getProgrammableEvents"=>new[]{new{Entity,Events=new[]{new{EventSource="pressed",EventType="RisingEdge"}}}},
                "cp.programming:getMode"=>new{Type=Sequence?"Sequence":"None"},
                "cp.programming:getAvailableModes"=>new[]{new{Type="None"},new{Type="Sequence"}},
                "cp.programming:getModeTriggers"=>new[]{new{Trigger="Default",Gesture="Default",ToggleState="None"}},
                "cp.programming:getModeOptions" or "cp.programming:getModeSupportedGestures"=>Array.Empty<object>(),
                "cp.programming:getSequenceProgrammingBlock"=>new{Id=200,ProgrammableEntity=Entity,Steps=Array.Empty<object>()},
                "cp.programming:copyProgramming"=>new{ProgrammableEvents=new[]{new{Programming=new{Mode=new{Type="Sequence"}}}}},
                _=>throw new InvalidDataException("Unexpected or mutating command.")
            };return Task.FromResult(Convert<T>(response));
        }
        public Task<T?> ExecuteAsync<T>(int deviceId,string commandName,object? parameters=null,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("Capture cannot mutate devices.");
        public Task<OperationResult> WaitForOperationAsync(string operationId,TimeSpan timeout,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}

[TestFixture]
public sealed class ConfigurationReferenceRemapperTests
{
    [Test]
    public void RemapsTypedSceneAndDeviceReferencesInsideNestedSerializedProgramming()
    {
        string block=JsonSerializer.Serialize(new{Id=7,ProgrammableEntity=new{Id=21,EntityType=2},Steps=new[]{new{Id=9,Target=new{Id=22,EntityType=2},Arguments=Array.Empty<object>()}}});
        var dto=JsonSerializer.SerializeToElement(new{ProgrammingData=JsonSerializer.Serialize(new{SequenceProgrammingBlockJson=block}),Targets=new[]{new{Id=22,Type="light",TargetLevel=0.25}}});
        var result=ConfigurationReferenceRemapper.Remap(dto,new Dictionary<int,int>{{21,31},{22,32}});
        Assert.That(result.UnsupportedReferences,Is.Empty);Assert.That(result.Changes.Count,Is.EqualTo(3));
        using var data=JsonDocument.Parse(result.Value.GetProperty("ProgrammingData").GetString()!);
        using var mapped=JsonDocument.Parse(data.RootElement.GetProperty("SequenceProgrammingBlockJson").GetString()!);
        Assert.That(mapped.RootElement.GetProperty("Id").GetInt32(),Is.EqualTo(7));
        Assert.That(mapped.RootElement.GetProperty("Steps")[0].GetProperty("Target").GetProperty("Id").GetInt32(),Is.EqualTo(32));
        Assert.That(result.Value.GetProperty("Targets")[0].GetProperty("TargetLevel").GetDouble(),Is.EqualTo(0.25));
    }
    [Test]
    public void UnknownReferenceLocationsAreReportedWithoutBlindReplacement()
    {
        var dto=JsonSerializer.SerializeToElement(new{UnrecognizedTarget=21,Text="device:21"});
        var result=ConfigurationReferenceRemapper.Remap(dto,new Dictionary<int,int>{{21,31}});
        Assert.That(result.Changes,Is.Empty);Assert.That(result.UnsupportedReferences.Count,Is.EqualTo(2));
        Assert.That(result.Value.GetProperty("UnrecognizedTarget").GetInt32(),Is.EqualTo(21));
    }
    [Test]
    public void SceneAndDeviceNamespacesStayDistinct()
    {
        var dto=JsonSerializer.SerializeToElement(new{Scene=new{Id=21,EntityType="Scene"},Device=new{Id=21,EntityType="Device"}});
        var result=ConfigurationReferenceRemapper.Remap(dto,new Dictionary<int,int>{{21,31}});
        Assert.That(result.Value.GetProperty("Scene").GetProperty("Id").GetInt32(),Is.EqualTo(21));
        Assert.That(result.Value.GetProperty("Device").GetProperty("Id").GetInt32(),Is.EqualTo(31));
    }
}
