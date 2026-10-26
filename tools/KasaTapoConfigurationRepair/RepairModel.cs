// Copyright (c) 2026 Neil Colvin. MIT license.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestronHomeDevTools;

namespace KasaTapoConfigurationRepair;

internal sealed record Snapshot(string HomeVersion, byte[] Manifest, byte[] Cache, DeviceInfo[] Devices,
    ProcessorLocation[] Rooms);
internal sealed record Review(string SourceHash, ManagedLightConfigurationIdentity[] Children, string[] RoomNames);
internal static class RepairModel
{
    internal const string ManifestPath="/user/Data/PyngDeviceManifest/DeviceManifest.cfg";
    internal const string CachePath="/user/Data/ThirdParty/NeilColvin/KasaTapoCrestronDriver/managed-devices-cache.json";
    internal const string DriverVersion="2.1.002.0004", HomeVersion="4.012.0194";
    internal const string Catalogue="tp-link.kasatapoplatform.cloud.neilcolvin";
    internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    internal sealed class Manifest
    {
        internal JsonObject Root {get;}
        private readonly Dictionary<string,JsonObject> references=new(StringComparer.Ordinal);
        internal Manifest(byte[] bytes)
        {
            if(bytes.Length>16*1024*1024)throw new InvalidDataException("Manifest size limit.");
            Root=JsonNode.Parse(bytes,documentOptions:new(){MaxDepth=96})?.AsObject()??throw new InvalidDataException("No manifest.");
            int count=0;
            void Walk(JsonNode? node)
            {
                if(++count>300000)throw new InvalidDataException("Manifest object limit.");
                if(node is JsonObject obj){if(obj["$id"] is JsonValue id&&!references.TryAdd(id.GetValue<string>(),obj))throw new InvalidDataException("Duplicate reference.");foreach(var p in obj)Walk(p.Value);}
                else if(node is JsonArray array)foreach(var item in array)Walk(item);
            }
            Walk(Root);
        }
        internal JsonObject Resolve(JsonNode? node)
        {
            var obj=node as JsonObject??throw new InvalidDataException("Missing object.");
            if(obj["$ref"] is JsonValue r){if(obj.Count!=1||!references.TryGetValue(r.GetValue<string>(),out obj!))throw new InvalidDataException("Invalid reference.");}
            return obj;
        }
        internal Dictionary<int,JsonObject> Devices=>Resolve(Root["Devices"]).Where(p=>p.Key!="$id"&&p.Key!="$type")
            .ToDictionary(p=>int.Parse(p.Key,System.Globalization.CultureInfo.InvariantCulture),p=>Resolve(p.Value));
    }
    internal static Review Inspect(Snapshot snapshot)
    {
        if(snapshot.HomeVersion!=HomeVersion)throw new InvalidDataException("Unsupported Home version.");
        var doc=new Manifest(snapshot.Manifest);var all=doc.Devices;
        var roots=all.Values.Where(d=>d["Model"]?.GetValue<string>()=="KasaTapoPlatform").ToArray();
        if(roots.Length!=1)throw new InvalidDataException("Select a system with exactly one Kasa/Tapo platform.");
        var root=roots[0];int rootId=root["Id"]!.GetValue<int>();var driver=doc.Resolve(root["DriverInformation"]);
        if(driver["DriverGuid"]?.GetValue<string>()!=Catalogue||!Version.TryParse(driver["DriverVersion"]?.GetValue<string>(),out var version)||version!=Version.Parse(DriverVersion))throw new InvalidDataException("Unsupported driver version.");
        using var cache=JsonDocument.Parse(snapshot.Cache,new(){MaxDepth=48});
        var cached=new Dictionary<string,(string? Model,bool Configured)>(StringComparer.Ordinal);
        foreach(var item in cache.RootElement.GetProperty("devices").EnumerateArray())
        {
            var immutable=item.TryGetProperty("immutable",out var im)?im:item;var mutable=item.TryGetProperty("mutable",out var mu)?mu:item;
            if(!cached.TryAdd(immutable.GetProperty("controllerId").GetString()!,
                (immutable.GetProperty("model").GetString(),mutable.TryGetProperty("isConfigured",out var configured)&&configured.ValueKind==JsonValueKind.True)))throw new InvalidDataException("Duplicate cache binding.");
        }
        var children=new List<ManagedLightConfigurationIdentity>();var rooms=new List<string>();
        var selectedBindings=new HashSet<string>(StringComparer.Ordinal);
        foreach(var pair in all.OrderBy(p=>p.Key))
        {
            var device=pair.Value;if(device["ParentDeviceInfo"] is null)continue;
            var parent=doc.Resolve(device["ParentDeviceInfo"]);
            if(parent["ParentInfo"] is null||doc.Resolve(parent["ParentInfo"])["Id"]?.GetValue<int>()!=rootId)continue;
            string model=device["Model"]?.GetValue<string>()??"";
            if(model is not ("HS220(US)" or "HS200(US)" or "S515(US)"))continue;
            var attributes=device["UserAttributes"] as JsonArray;
            var markers=(attributes??[]).Select(doc.Resolve).Where(a=>a["AttributeId"]?.GetValue<string>() is "ActivationMarker" or "DriverDataStore").ToArray();
            if(markers.Length==2&&device["ConfigurationNeeded"]?.GetValue<int>()==1&&
                markers.SingleOrDefault(a=>a["AttributeId"]?.GetValue<string>()=="ActivationMarker")?["PersistedValue"]?.GetValue<string>()=="true"&&
                markers.SingleOrDefault(a=>a["AttributeId"]?.GetValue<string>()=="DriverDataStore") is not null)continue;
            if(markers.Length!=0)throw new InvalidDataException("Partial existing configuration requires support review.");
            var connections=device["ConnectionInfos"]?.AsArray()??throw new InvalidDataException("No binding.");
            if(connections.Count!=1)throw new InvalidDataException("Ambiguous binding.");
            string managed=doc.Resolve(connections[0])["GatewayManagedDeviceId"]!.GetValue<string>();
            if(!selectedBindings.Add(managed)||!cached.TryGetValue(managed,out var saved)||!saved.Configured||saved.Model!=model)throw new InvalidDataException("Cache and saved binding differ.");
            var identity=new ManagedLightConfigurationIdentity(pair.Key,rootId,managed,device["Name"]!.GetValue<string>(),model,
                device["RoomId"]!.GetValue<int>(),DriverVersion);
            var observed=snapshot.Devices.SingleOrDefault(d=>d.Id==identity.DeviceId);
            if(observed is null||observed.Name!=identity.Name||observed.Model!=model||observed.ParentDeviceId!=rootId)throw new InvalidDataException("Runtime identity differs.");
            _=MissingManagedLightConfiguration.PlanForValidatedHome(snapshot.Manifest,Hash(snapshot.Manifest),identity,snapshot.HomeVersion);
            children.Add(identity);rooms.Add(snapshot.Rooms.SingleOrDefault(r=>r.Id==identity.RoomId)?.Name??$"Room {identity.RoomId}");
        }
        return new(Hash(snapshot.Manifest),children.ToArray(),rooms.ToArray());
    }
    internal static byte[] Candidate(Snapshot fresh,Review review)
    {
        var current=Inspect(fresh);
        if(!current.Children.SequenceEqual(review.Children))throw new InvalidDataException("Reviewed device identities changed.");
        byte[] bytes=fresh.Manifest;
        foreach(var child in review.Children)bytes=MissingManagedLightConfiguration.PlanForValidatedHome(bytes,Hash(bytes),child,fresh.HomeVersion).CopyPrivateCandidateBytes();
        AssertOnlySelectedFieldsChanged(fresh.Manifest,bytes,review);
        return bytes;
    }
    internal static void AssertOnlySelectedFieldsChanged(byte[] original,byte[] candidate,Review review)
    {
        var before=new Manifest(original);var after=new Manifest(candidate);var old=before.Devices;var changed=after.Devices;
        foreach(var child in review.Children)
        {
            var attrs=changed[child.DeviceId]["UserAttributes"]!.AsArray();
            if(changed[child.DeviceId]["ConfigurationNeeded"]?.GetValue<int>()!=1)throw new InvalidDataException("Missing configuration flag.");
            int oldCount=(old[child.DeviceId]["UserAttributes"] as JsonArray)?.Count??0;
            for(int i=0;i<oldCount;i++)if(!JsonNode.DeepEquals(attrs[i],old[child.DeviceId]["UserAttributes"]![i]))throw new InvalidDataException("Existing user attribute changed.");
            if(attrs.Count!=oldCount+2||attrs[^2]?["AttributeId"]?.GetValue<string>()!="ActivationMarker"||attrs[^2]?["PersistedValue"]?.GetValue<string>()!="true"||
                attrs[^1]?["AttributeId"]?.GetValue<string>()!="DriverDataStore"||attrs[^1]?["PersistedValue"]?.GetValue<string>()!="")throw new InvalidDataException("Unexpected marker values.");
            attrs.RemoveAt(attrs.Count-1);attrs.RemoveAt(attrs.Count-1);
            changed[child.DeviceId]["UserAttributes"]=old[child.DeviceId]["UserAttributes"]?.DeepClone();
            changed[child.DeviceId]["ConfigurationNeeded"]=old[child.DeviceId]["ConfigurationNeeded"]?.DeepClone();
        }
        if(!JsonNode.DeepEquals(before.Root,after.Root))throw new InvalidDataException("Unrelated configuration changed.");
    }
    internal static bool IdentitiesMatch(DeviceInfo[] before,DeviceInfo[] after)=>before.Length==after.Length&&before.All(a=>after.Any(b=>a.Id==b.Id&&a.Name==b.Name&&a.Model==b.Model&&a.ParentDeviceId==b.ParentDeviceId&&a.LocationId==b.LocationId));
    internal static bool PersistedMatches(byte[] expected,byte[] observed,IEnumerable<int> selectedIds)
    {
        try{return PersistedMatchesCore(expected,observed,selectedIds);}
        catch(Exception error)when(error is JsonException or InvalidDataException){return false;}
    }
    private static bool PersistedMatchesCore(byte[] expected,byte[] observed,IEnumerable<int> selectedIds)
    {
        var first=new Manifest(expected);var second=new Manifest(observed);
        foreach(int id in selectedIds)
        {
            if(!first.Devices.TryGetValue(id,out var before)||!second.Devices.TryGetValue(id,out var after))return false;
            // Home marks newly deserialized children on its first save after startup.
            if(before["WasDeserializedFromDisk"] is JsonValue old&&old.TryGetValue<bool>(out _)&&
                after["WasDeserializedFromDisk"] is JsonValue current&&current.TryGetValue<bool>(out bool loaded)&&loaded)
                before["WasDeserializedFromDisk"]=true;
        }
        JsonObject Normalise(Manifest document)
        {
            var paths=new Dictionary<string,string>(StringComparer.Ordinal);
            void Index(JsonNode? node,string path)
            {
                if(node is JsonObject obj)
                {
                    if(obj["$id"] is JsonValue id)paths.Add(id.GetValue<string>(),path);
                    foreach(var p in obj.OrderBy(p=>p.Key,StringComparer.Ordinal))Index(p.Value,path+"/"+p.Key.Replace("~","~0").Replace("/","~1"));
                }
                else if(node is JsonArray array)for(int i=0;i<array.Count;i++)Index(array[i],path+"/"+i);
            }
            Index(document.Root,"");
            void Canonicalise(JsonNode? node,bool secure=false)
            {
                if(node is JsonObject obj)
                {
                    if(obj["$id"] is JsonValue id)obj["$id"]=paths[id.GetValue<string>()];
                    if(obj["$ref"] is JsonValue reference)
                    {if(obj.Count!=1||!paths.TryGetValue(reference.GetValue<string>(),out string? path))throw new InvalidDataException("Unresolved saved reference.");obj["$ref"]=path;}
                    // The opaque secure-string ciphertext may rotate at startup (for example a refreshed token).
                    // Keep its type/access/layout; do not compare or report encrypted payload bytes.
                    if(secure&&obj["$type"]?.GetValue<string>()=="Crestron.Tools.Commissioning.Common.Security.SecureString, Crestron.Tools.Commissioning.Common"&&obj["value"] is JsonValue value&&value.TryGetValue<string>(out _))
                        obj["value"]="<opaque Home secure string>";
                    foreach(var p in obj.ToArray())Canonicalise(p.Value,p.Key=="SecurePersistedValue");
                }
                else if(node is JsonArray array)foreach(var item in array)Canonicalise(item);
            }
            Canonicalise(document.Root);return document.Root;
        }
        return JsonNode.DeepEquals(Normalise(first),Normalise(second));
    }
}
