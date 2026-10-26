// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License. See LICENSE.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record ConfigurationCaptureIdentity(string Host, string CertificateSha256, int ProcessorId, string Model, string HomeVersion);
public sealed record ConfigurationCaptureEntry(string File, string ResourcePath, string? Command, JsonElement? Parameters, long Bytes, string Sha256);
public sealed record ConfigurationCaptureManifest(int FormatVersion, ConfigurationCaptureIdentity Identity, DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc, string Scope, IReadOnlyList<ConfigurationCaptureEntry> Entries);
public sealed record ConfigurationCapturedValue(ConfigurationCaptureEntry Entry,JsonElement Value);
public sealed record ValidatedConfigurationCapture(ConfigurationCaptureManifest Manifest,string ManifestSha256,IReadOnlyList<ConfigurationCapturedValue> Values);

/// <summary>Captures the live device, location, scene and programming graph for replacement planning.</summary>
/// <remarks>Contains private configuration. Capture is read-only; it does not authorize removal or promise a complete processor restore.</remarks>
public static class LiveConfigurationCapture
{
    private const int MaximumEntryBytes=16*1024*1024, MaximumEntries=8192;
    private const long MaximumTotalBytes=128*1024*1024;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};

    /// <summary>Loads the exact pinned snapshot into memory and rejects missing, altered or extra files.</summary>
    public static ValidatedConfigurationCapture ReadValidated(string privateDirectory,string expectedManifestSha256)
    {
        string folder=Path.GetFullPath(privateDirectory);
        if(!System.Text.RegularExpressions.Regex.IsMatch(expectedManifestSha256??"",@"\A[0-9a-fA-F]{64}\z"))throw new ArgumentException("A pinned manifest digest is required.",nameof(expectedManifestSha256));
        byte[] ReadFile(string name,long limit)
        {
            string file=Path.Combine(folder,name);
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Snapshot links are unsupported.");
            using var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
            if(stream.Length>limit)throw new InvalidDataException("Snapshot file exceeds its limit.");
            using var content=new MemoryStream();stream.CopyTo(content);
            if(content.Length>limit)throw new InvalidDataException("Snapshot file changed size.");
            return content.ToArray();
        }
        if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Snapshot links are unsupported.");
        byte[] bytes=ReadFile("capture.json",MaximumEntryBytes);
        string digest=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if(!digest.Equals(expectedManifestSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The snapshot manifest does not match its pinned digest.");
        var manifest=JsonSerializer.Deserialize<ConfigurationCaptureManifest>(bytes)??throw new InvalidDataException("Snapshot manifest is missing.");
        if(manifest.FormatVersion!=1 || manifest.Entries==null || manifest.Entries.Count is <1 or >MaximumEntries || manifest.CompletedUtc<manifest.StartedUtc)
            throw new InvalidDataException("Unsupported or incomplete snapshot manifest.");
        var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"capture.json","capture-intent.json"};
        var values=new List<ConfigurationCapturedValue>();long total=0;
        foreach(var entry in manifest.Entries)
        {
            if(entry.File!=(values.Count+1).ToString("D4")+".json" || !files.Add(entry.File) || entry.Bytes<0 || entry.Bytes>MaximumEntryBytes || total+entry.Bytes>MaximumTotalBytes)
                throw new InvalidDataException("Invalid snapshot entry.");
            ProcessorConnection.ValidateConfigurationResourcePath(entry.ResourcePath);
            byte[] content=ReadFile(entry.File,MaximumEntryBytes);
            if(content.LongLength!=entry.Bytes || !Convert.ToHexString(SHA256.HashData(content)).Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A captured configuration entry was altered.");
            using var document=JsonDocument.Parse(content);values.Add(new(entry,document.RootElement.Clone()));total+=content.LongLength;
        }
        if(Directory.EnumerateFileSystemEntries(folder).Any(path=>!files.Contains(Path.GetFileName(path)) || Directory.Exists(path)))
            throw new InvalidDataException("The snapshot contains unexpected files.");
        return new(manifest,digest,values);
    }

    public static async Task<ConfigurationCaptureManifest> CaptureAsync(ConfigurationClient client, ConfigurationCaptureIdentity identity,
        string newPrivateDirectory, CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(client); ArgumentNullException.ThrowIfNull(identity);
        if(string.IsNullOrWhiteSpace(identity.Host) || identity.Host.Any(char.IsControl) || identity.ProcessorId<=0 ||
            string.IsNullOrWhiteSpace(identity.Model) || string.IsNullOrWhiteSpace(identity.HomeVersion) ||
            !System.Text.RegularExpressions.Regex.IsMatch(identity.CertificateSha256??"",@"\A[0-9a-fA-F]{64}\z"))
            throw new ArgumentException("An explicit processor identity and certificate pin are required.",nameof(identity));
        string folder=Path.GetFullPath(newPrivateDirectory);
        if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Capture requires a new private directory.");
        Directory.CreateDirectory(folder);
        var started=DateTimeOffset.UtcNow;
        // CreateNew wins even when two callers race creation of the same directory.
        using(var intent=new FileStream(Path.Combine(folder,"capture-intent.json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read))
        {JsonSerializer.Serialize(intent,new{identity,StartedUtc=started},Json);intent.Flush(true);}
        var entries=new List<ConfigurationCaptureEntry>(); long total=0;
        async Task<JsonElement> Save(string resource,string? command=null,object? parameters=null)
        {
            token.ThrowIfCancellationRequested();
            if(entries.Count>=MaximumEntries)throw new InvalidDataException("Capture exceeds the entry limit.");
            JsonElement? response=command==null?await client.GetConfigurationResourceAsync(resource,token).ConfigureAwait(false):
                await client.ExecuteConfigurationCommandAsync(resource,command,parameters,token).ConfigureAwait(false);
            var data=response??JsonSerializer.SerializeToElement<object?>(null);
            byte[] bytes=Encoding.UTF8.GetBytes(data.GetRawText());
            if(bytes.Length>MaximumEntryBytes || total+bytes.Length>MaximumTotalBytes)throw new InvalidDataException("Capture exceeds the byte limit.");
            string file=(entries.Count+1).ToString("D4")+".json";
            using(var output=new FileStream(Path.Combine(folder,file),FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            {await output.WriteAsync(bytes,token).ConfigureAwait(false);output.Flush(true);}
            entries.Add(new(file,resource,command,parameters==null?null:JsonSerializer.SerializeToElement(parameters),bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
            total+=bytes.Length;
            return data;
        }
        try
        {
            var devices=await Save("v2/Devices").ConfigureAwait(false);
            if(devices.ValueKind!=JsonValueKind.Object || !devices.TryGetProperty(identity.ProcessorId.ToString(System.Globalization.CultureInfo.InvariantCulture),out var processor)
                || processor.GetProperty("Model").GetString()!=identity.Model)
                throw new InvalidDataException("The capture processor identity does not match the inventory.");
            var locations=await Save("v2/Locations").ConfigureAwait(false);
            var scenes=await Save("v2/Scenes").ConfigureAwait(false);
            await Save("v2/Groups").ConfigureAwait(false);
            var quickActions=await Save("v2/QuickActions").ConfigureAwait(false);
            await Save("v2/Media").ConfigureAwait(false);
            var programmingCommands=await Save("v2/Programming").ConfigureAwait(false);
            if(programmingCommands.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Programming command metadata is unavailable.");
            var advertised=programmingCommands.EnumerateArray().Select(x=>x.GetString()).ToHashSet(StringComparer.Ordinal);
            async Task<JsonElement> Programming(string command,object parameters)
            {
                string name="cp.programming:"+command;
                if(!advertised.Contains(name))throw new InvalidDataException("A required programming query is unavailable.");
                return await Save("v2/Programming",name,parameters).ConfigureAwait(false);
            }
            if(scenes.ValueKind!=JsonValueKind.Object || locations.ValueKind!=JsonValueKind.Array || quickActions.ValueKind!=JsonValueKind.Array)
                throw new InvalidDataException("Configuration resource shape is unsupported.");
            foreach(var collection in scenes.EnumerateObject().Where(p=>p.Value.ValueKind==JsonValueKind.Array))
                foreach(var scene in collection.Value.EnumerateArray())
                {
                    int id=scene.GetProperty("Id").GetInt32();
                    var commands=scene.GetProperty("Commands").EnumerateArray().Select(x=>x.GetString()).ToHashSet(StringComparer.Ordinal);
                    foreach(string query in new[]{"cp.scene:getCurrentTargets","cp.scene:getSceneDetails"})
                        if(commands.Contains(query))await Save("v2/Scenes/"+id,query,new{}).ConfigureAwait(false);
                        else if(query.EndsWith("getCurrentTargets",StringComparison.Ordinal))throw new InvalidDataException("Scene targets cannot be captured.");
                }
            var entities=new Dictionary<string,JsonElement>(StringComparer.Ordinal);
            // Home's location 0 query includes system-wide and unlocated entities (including
            // platform wrappers). Room-only enumeration misses their incoming references.
            foreach(int locationId in locations.EnumerateArray().Select(x=>x.GetProperty("Id").GetInt32()).Prepend(0).Distinct())
            {
                var found=await Programming("getProgrammableEntitiesWithEvents",new{locationId}).ConfigureAwait(false);
                if(found.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Programmable entities could not be enumerated.");
                foreach(var entity in found.EnumerateArray())
                {
                    string key=EntityKey(entity);
                    if(entities.TryGetValue(key,out var prior) && !JsonElement.DeepEquals(prior,entity))throw new InvalidDataException("Conflicting programmable entity records.");
                    entities[key]=entity;
                }
            }
            if(entities.Count>2048)throw new InvalidDataException("Too many programmable entities.");
            foreach(var batch in entities.Values.Chunk(32))
            {
                var groups=await Programming("getProgrammableEvents",new{programmableEntities=batch}).ConfigureAwait(false);
                if(groups.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Programmable events could not be read.");
                var received=new HashSet<string>(StringComparer.Ordinal);
                foreach(var group in groups.EnumerateArray())
                {
                    var entity=group.GetProperty("Entity"); string key=EntityKey(entity);
                    if(!batch.Any(e=>EntityKey(e)==key)||!received.Add(key))throw new InvalidDataException("Unexpected programmable event owner.");
                    foreach(var eventInfo in group.GetProperty("Events").EnumerateArray())
                    {
                        var parameters=new{programmableEntity=entity,eventInfo};
                        var mode=await Programming("getMode",parameters).ConfigureAwait(false);
                        string type=mode.GetProperty("Type").GetString()??throw new InvalidDataException("Programming mode is missing.");
                        if(type=="None")continue;
                        await Programming("getAvailableModes",parameters).ConfigureAwait(false);
                        var triggers=await Programming("getModeTriggers",parameters).ConfigureAwait(false);
                        await Programming("getModeSupportedGestures",parameters).ConfigureAwait(false);
                        await Programming("getModeOptions",parameters).ConfigureAwait(false);
                        if(triggers.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Programmed event trigger layout is unsupported.");
                        foreach(var trigger in triggers.EnumerateArray())
                        {
                            var triggerParameters=new{programmableEntity=entity,eventInfo,trigger};
                            await Programming(type=="Sequence"?"getSequenceProgrammingBlock":"getModeActionItems",triggerParameters).ConfigureAwait(false);
                            // Copy DTOs preserve mode options and opaque serialized sequence
                            // structure required by Home's paste API; summaries are insufficient.
                            await Programming("copyProgramming",new{sourceEntity=entity,sourceEvent=eventInfo,sourceGesture=trigger}).ConfigureAwait(false);
                        }
                    }
                }
                if(received.Count!=batch.Length)throw new InvalidDataException("A programmable entity's events were omitted.");
            }
            // A snapshot is not a transaction. Detect structural edits during collection;
            // callers must revalidate the saved graph again immediately before removal.
            var after=await client.GetConfigurationResourceAsync("v2/Devices",token).ConfigureAwait(false);
            if(after==null || !DeviceStructure(devices).SequenceEqual(DeviceStructure(after.Value)))
                throw new InvalidDataException("Device structure changed during capture.");
            var manifest=new ConfigurationCaptureManifest(1,identity,started,DateTimeOffset.UtcNow,
                "Live replacement configuration graph; excludes House and MeetingSpaces resource summaries, which are not reliable reads on the validated Home release. Does not represent a full processor backup or authorize deletion.",entries);
            using(var output=new FileStream(Path.Combine(folder,"capture.json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            {JsonSerializer.Serialize(output,manifest,Json);output.Flush(true);}
            return manifest;
        }
        catch(Exception error)
        {
            try
            {
                using var failure=new FileStream(Path.Combine(folder,"capture-failure.json"),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
                JsonSerializer.Serialize(failure,new{FailureType=error.GetType().FullName,CompletedEntries=entries},Json);failure.Flush(true);
            }
            catch(IOException) { /* Preserve the original capture failure. */ }
            throw;
        }
    }

    internal static string EntityKey(JsonElement entity)=>string.Join("|",new[]{"EntityType","Id","ComponentId","ParentComponentId"}.Select(k=>entity.TryGetProperty(k,out var v)?v.GetRawText():"<absent>"));
    private static string[] DeviceStructure(JsonElement inventory)=>inventory.EnumerateObject().Select(p=>string.Join("|",new[]{"Id","Name","Model","ParentDeviceId","LocationId"}.Select(k=>p.Value.TryGetProperty(k,out var value)?value.GetRawText():"<absent>"))).Order(StringComparer.Ordinal).ToArray();
}
