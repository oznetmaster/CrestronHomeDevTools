// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestronHomeDevTools;
using NUnit.Framework;
using KasaTapoConfigurationRepair;

namespace KasaTapoConfigurationRepair.Tests;

[TestFixture]
public sealed class RepairTests
{
    private static Snapshot Fixture()
    {
        JsonObject Driver()=>new(){["DriverGuid"]=RepairModel.Catalogue,["DriverVersion"]=RepairModel.DriverVersion,["DriverUxCategory"]="light"};
        var children=new JsonArray();var devices=new JsonObject{["10"]=new JsonObject{["$id"]="1",["Id"]=10,["Name"]="Lighting",["Model"]="KasaTapoPlatform",["DriverInformation"]=Driver(),["ChildDevices"]=children}};
        var cache=new JsonArray();var inventory=new List<DeviceInfo>{new(){Id=10,Name="Lighting",Model="KasaTapoPlatform"}};
        for(int i=0;i<11;i++)
        {
            int id=20+i;string model=i<4?"HS220(US)":i<8?"HS200(US)":"S515(US)";string name="Example "+i,managed="device_example"+i;
            var child=new JsonObject{["$id"]=(i+2).ToString(),["Id"]=id,["Name"]=name,["Model"]=model,["RoomId"]=3,["DevType"]=8,["InfoType"]=3,["DriverInformation"]=Driver(),
                ["ParentDeviceInfo"]=new JsonObject{["ParentInfo"]=new JsonObject{["$ref"]="1"}},["ConnectionInfos"]=new JsonArray(new JsonObject{["GatewayId"]=10,["GatewayManagedDeviceId"]=managed}),
                ["ConfigurationNeeded"]=0,["UserAttributes"]=null,["UnrecognisedSecret"]="SECRET_CONFIGURATION"};
            children.Add(child);devices[id.ToString()]=new JsonObject{["$ref"]=(i+2).ToString()};
            cache.Add(new JsonObject{["immutable"]=new JsonObject{["controllerId"]=managed,["model"]=model},["mutable"]=new JsonObject{["isConfigured"]=true}});
            inventory.Add(new(){Id=id,ParentDeviceId=10,Name=name,Model=model});
        }
        var root=new JsonObject{["Devices"]=devices,["Scenes"]=new JsonArray(new JsonObject{["Target"]="unchanged",["Id"]=999}),["Programming"]=new JsonObject{["Private"]="SECRET_PROGRAMMING"}};
        return new(RepairModel.HomeVersion,JsonSerializer.SerializeToUtf8Bytes(root),JsonSerializer.SerializeToUtf8Bytes(new JsonObject{["devices"]=cache}),inventory.ToArray(),[new(3,"Example room",null)]);
    }
    private static RepairState State(Snapshot s)=>new(){Owner=Guid.NewGuid().ToString("N"),Processor=new("example.invalid","certificate","ssh"),Review=RepairModel.Inspect(s),Baseline=s};
    [Test]
    public void ElevenMissingChildrenRepairWithoutHealthySiblingAndPreserveAllOtherFields()
    {
        var source=Fixture();var review=RepairModel.Inspect(source);Assert.That(review.Children,Has.Length.EqualTo(11));
        var result=RepairModel.Candidate(source,review);RepairModel.AssertOnlySelectedFieldsChanged(source.Manifest,result,review);
        var doc=new RepairModel.Manifest(result);var ids=new HashSet<string>();
        foreach(var c in review.Children)
        {
            var child=doc.Devices[c.DeviceId];Assert.That(child["ConfigurationNeeded"]!.GetValue<int>(),Is.EqualTo(1));
            foreach(var attribute in child["UserAttributes"]!.AsArray())Assert.That(ids.Add(attribute!["$id"]!.GetValue<string>()),Is.True);
        }
        Assert.That(RepairModel.Inspect(source with{Manifest=result}).Children,Is.Empty);
        Assert.That(RepairModel.Hash(source.Manifest),Is.EqualTo(review.SourceHash));
    }
    [TestCase("home")] [TestCase("driver")] [TestCase("binding")] [TestCase("partial")] [TestCase("duplicate")] [TestCase("configured")]
    public void UnsupportedOrInconsistentSourcesRefusedBeforeStop(string change)
    {
        var source=Fixture();var doc=new RepairModel.Manifest(source.Manifest);var child=doc.Devices[20];
        switch(change)
        {
            case "home":source=source with{HomeVersion="4.013.0000"};break;
            case "driver":doc.Devices[10]["DriverInformation"]!["DriverVersion"]="2.1.002.0002";break;
            case "binding":child["ConnectionInfos"]![0]!["GatewayManagedDeviceId"]="device_other";break;
            case "partial":child["UserAttributes"]=new JsonArray(new JsonObject{["AttributeId"]="ActivationMarker",["PersistedValue"]="true"});break;
            case "duplicate":child["$id"]="1";break;
            case "configured":child["ConfigurationNeeded"]=1;break;
        }
        source=source with{Manifest=JsonSerializer.SerializeToUtf8Bytes(doc.Root)};
        Assert.Throws<InvalidDataException>(()=>RepairModel.Inspect(source));
    }
    [Test]
    public void UncommissionedCacheDevicesDoNotInvalidateAffectedBindings()
    {
        var s=Fixture();var cache=JsonNode.Parse(s.Cache)!;
        cache["devices"]!.AsArray().Add(new JsonObject{["immutable"]=new JsonObject{["controllerId"]="device_uncommissioned",["model"]="Other"},["mutable"]=new JsonObject()});
        Assert.That(RepairModel.Inspect(s with{Cache=JsonSerializer.SerializeToUtf8Bytes(cache)}).Children,Has.Length.EqualTo(11));
    }
    [Test]
    public void StaleReviewCannotRepairDifferentPhysicalDevice()
    {
        var source=Fixture();var review=RepairModel.Inspect(source);var changed=review with{Children=review.Children.Select((c,i)=>i==0?c with{ManagedDeviceId="device_other"}:c).ToArray()};
        Assert.Throws<InvalidDataException>(()=>RepairModel.Candidate(source,changed));
    }
    [Test]
    public async Task SuccessfulWorkflowStagesBackupsBeforeWritesAndVerifiesBeforeReleasing()
    {
        var source=Fixture();var state=State(source);var journal=new MemoryJournal();await using var fake=new FakeProcessor(source);
        await RepairWorkflow.Apply(fake,journal,state,_=>{});
        Assert.That(state.Stage,Is.EqualTo("Complete"));Assert.That(fake.Stops,Is.EqualTo(1));Assert.That(fake.Reboots,Is.EqualTo(1));Assert.That(fake.Released,Is.True);
        Assert.That(journal.Stages.IndexOf("BackupSaved"),Is.LessThan(journal.Stages.IndexOf("UploadIntent")));
        Assert.That(journal.Stages.IndexOf("Verified"),Is.LessThan(journal.Stages.IndexOf("Complete")));
        Assert.That(fake.Files.ContainsKey(state.RemoteOriginal),Is.True);RepairWorkflow.RequireHash(fake.Files[state.RemoteOriginal],source.Manifest);
    }
    [TestCase("stop")] [TestCase("upload")] [TestCase("rename1")] [TestCase("rename2")] [TestCase("reboot")] [TestCase("verify")] [TestCase("staged-hash")]
    public void UncertainFailuresAreNeverReplayedOrReportedComplete(string failure)
    {
        var source=Fixture();var state=State(source);var journal=new MemoryJournal();var fake=new FakeProcessor(source){Failure=failure};
        Assert.ThrowsAsync(failure=="staged-hash"?Is.TypeOf<InvalidDataException>():Is.TypeOf<IOException>(),async()=>await RepairWorkflow.Apply(fake,journal,state,_=>{}));
        Assert.That(journal.Stages,Does.Not.Contain("Complete"));Assert.That(fake.Released,Is.False);
        Assert.That(fake.Stops,Is.LessThanOrEqualTo(1));Assert.That(fake.Uploads,Is.LessThanOrEqualTo(1));Assert.That(fake.Reboots,Is.LessThanOrEqualTo(1));
        Assert.That(fake.Renames,Is.LessThanOrEqualTo(2));
    }
    [TestCase("snapshot")] [TestCase("uptime")]
    public void FreshPreflightFailureReleasesLeaseWithoutStoppingHome(string failure)
    {
        var source=Fixture();var state=State(source);var fake=new FakeProcessor(source){Failure=failure};
        Assert.ThrowsAsync<IOException>(async()=>await RepairWorkflow.Apply(fake,new MemoryJournal(),state,_=>{}));
        Assert.That(fake.Stops,Is.Zero);Assert.That(fake.Uploads,Is.Zero);Assert.That(fake.Released,Is.True);Assert.That(state.Stage,Is.EqualTo("Unchanged"));
    }
    [Test]
    public void RecoveryAcceptsKnownCandidateAfterHomeReserializesIt()
    {
        var source=Fixture();var state=State(source);state.Original=source.Manifest;state.Candidate=RepairModel.Candidate(source,state.Review);
        byte[] reformatted=System.Text.Encoding.UTF8.GetBytes(JsonNode.Parse(state.Candidate)!.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
        Assert.That(RepairModel.Hash(reformatted),Is.Not.EqualTo(RepairModel.Hash(state.Candidate)));
        Assert.That(RepairWorkflow.RecoveryNeedsRestore(reformatted,state),Is.True);
    }
    [Test]
    public void DeserializationBookkeepingAllowanceIsLimitedToSelectedDevices()
    {
        var s=Fixture();var doc=new RepairModel.Manifest(s.Manifest);doc.Devices[20]["WasDeserializedFromDisk"]=false;doc.Devices[21]["WasDeserializedFromDisk"]=false;
        byte[] before=JsonSerializer.SerializeToUtf8Bytes(doc.Root);doc.Devices[20]["WasDeserializedFromDisk"]=true;
        Assert.That(RepairModel.PersistedMatches(before,JsonSerializer.SerializeToUtf8Bytes(doc.Root),[20]),Is.True);
        doc.Devices[21]["WasDeserializedFromDisk"]=true;
        Assert.That(RepairModel.PersistedMatches(before,JsonSerializer.SerializeToUtf8Bytes(doc.Root),[20]),Is.False);
    }
    [TestCase(false)] [TestCase(true)]
    public async Task RecoveryHandlesInterruptedTwoRenameSwap(bool candidateInstalled)
    {
        var source=Fixture();var state=State(source);state.Original=source.Manifest;state.Candidate=RepairModel.Candidate(source,state.Review);state.Stage="InstallCandidateIntent";
        var fake=new FakeProcessor(source);fake.Files[state.RemoteOriginal]=source.Manifest;
        if(candidateInstalled)fake.Files[RepairModel.ManifestPath]=state.Candidate;else fake.Files.Remove(RepairModel.ManifestPath);
        await RepairWorkflow.Recover(fake,new MemoryJournal(),state,_=>{});
        Assert.That(state.Stage,Is.EqualTo("Restored"));RepairWorkflow.RequireHash(fake.Files[RepairModel.ManifestPath],source.Manifest);Assert.That(fake.Released,Is.True);
    }
    [Test]
    public void RecoveryRefusesUnrecognisedExternalChanges()
    {
        var source=Fixture();var state=State(source);state.Original=source.Manifest;state.Candidate=RepairModel.Candidate(source,state.Review);
        Assert.Throws<InvalidDataException>(()=>RepairWorkflow.RecoveryNeedsRestore([1,2,3],state));
    }
    [Test]
    public void PersistenceAllowsReferenceRenumberingButRejectsReferenceRetargetingAndProgrammingChanges()
    {
        var source=Fixture();var expected=RepairModel.Candidate(source,RepairModel.Inspect(source));var doc=new RepairModel.Manifest(expected);
        void Renumber(JsonNode? node)
        {
            if(node is JsonObject o){foreach(string key in new[]{"$id","$ref"})if(o[key] is JsonValue v)o[key]="renumbered-"+v.GetValue<string>();foreach(var p in o.ToArray())Renumber(p.Value);}
            else if(node is JsonArray a)foreach(var n in a)Renumber(n);
        }
        Renumber(doc.Root);byte[] observed=JsonSerializer.SerializeToUtf8Bytes(doc.Root);
        Assert.That(RepairModel.PersistedMatches(expected,observed,[20]),Is.True);
        doc.Root["Programming"]!["Private"]="changed";
        Assert.That(RepairModel.PersistedMatches(expected,JsonSerializer.SerializeToUtf8Bytes(doc.Root),[20]),Is.False);
        doc.Root["Programming"]!["Private"]="SECRET_PROGRAMMING";
        doc.Root["Devices"]!["20"]!["$ref"]="renumbered-3";
        Assert.That(RepairModel.PersistedMatches(expected,JsonSerializer.SerializeToUtf8Bytes(doc.Root),[20]),Is.False);
    }
    [Test]
    public void OnlyKnownOpaqueSecureStringPayloadMayRotate()
    {
        var s=Fixture();var doc=new RepairModel.Manifest(s.Manifest);
        doc.Devices[10]["SecurePersistedValue"]=new JsonObject{["$type"]="Crestron.Tools.Commissioning.Common.Security.SecureString, Crestron.Tools.Commissioning.Common",["access"]=1,["value"]="encrypted-one"};
        byte[] before=JsonSerializer.SerializeToUtf8Bytes(doc.Root);doc.Devices[10]["SecurePersistedValue"]!["value"]="encrypted-two";
        Assert.That(RepairModel.PersistedMatches(before,JsonSerializer.SerializeToUtf8Bytes(doc.Root),[]),Is.True);
        doc.Devices[10]["SecurePersistedValue"]!["access"]=2;
        Assert.That(RepairModel.PersistedMatches(before,JsonSerializer.SerializeToUtf8Bytes(doc.Root),[]),Is.False);
    }
    [Test]
    public void PrivateJournalEncryptsConfigurationAndFilteredReportHasNoSecrets()
    {
        string path=Path.Combine(Path.GetTempPath(),"KasaRepairTest-"+Guid.NewGuid().ToString("N"));
        try
        {
            var state=State(Fixture());state.Original=state.Baseline.Manifest;var journal=new PrivateJournal(path);journal.Save(state,"StopIntent");journal.Report(state,"Incomplete","IOException");
            Assert.That(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(path,"*.dpapi").Single())),Does.Not.Contain("SECRET_"));
            Assert.That(RepairModel.Hash(journal.Load().Original!),Is.EqualTo(RepairModel.Hash(state.Original)));
            var report=File.ReadAllText(Path.Combine(path,"Support-report.json"));Assert.That(report,Does.Not.Contain("SECRET_").And.Not.Contain("certificate").And.Not.Contain("example.invalid"));
        }
        finally{if(Directory.Exists(path))Directory.Delete(path,true);}
    }
    private sealed class MemoryJournal:IJournal
    {
        internal readonly List<string> Stages=[];
        public void Save(RepairState state,string stage){Stages.Add(stage);state.Stage=stage;}
    }
    private sealed class FakeProcessor(Snapshot source):IRepairProcessor
    {
        internal readonly Dictionary<string,byte[]> Files=new(){[RepairModel.ManifestPath]=source.Manifest,[RepairModel.CachePath]=source.Cache};
        internal string? Failure;internal int Stops,Reboots,Uploads,Renames;internal bool Released;
        private void Fail(string point){if(Failure==point)throw new IOException("Injected uncertainty");}
        public Task Acquire(string owner,bool resume)=>Task.CompletedTask;
        public Task Release(){Released=true;return Task.CompletedTask;}
        public Task<Snapshot> Snapshot(){Fail("snapshot");return Task.FromResult(source);}
        public Task<ProcessorUptimeSnapshot> Uptime(){Fail("uptime");return Task.FromResult(new ProcessorUptimeSnapshot(TimeSpan.FromDays(1),DateTime.Now.AddDays(-1),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));}
        public Task StopConfirmed(){Stops++;Fail("stop");return Task.CompletedTask;}
        public Task<byte[]?> Read(string path)=>Task.FromResult(Files.GetValueOrDefault(path));
        public Task Upload(string path,byte[] bytes){Uploads++;Fail("upload");Files.Add(path,Failure=="staged-hash"?[0]:bytes);return Task.CompletedTask;}
        public Task Rename(string sourcePath,string destination){Renames++;Fail("rename"+Renames);Files.Add(destination,Files[sourcePath]);Files.Remove(sourcePath);return Task.CompletedTask;}
        public Task Reboot(){Reboots++;Fail("reboot");return Task.CompletedTask;}
        public Task VerifyStartup(RepairState state,bool restored){Fail("verify");return Task.CompletedTask;}
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
