// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License. See LICENSE.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrestronHomeDevTools;

public sealed record ManagedLightConfigurationIdentity(int DeviceId, int ParentId, string ManagedDeviceId,
    string Name, string Model, int RoomId, string DriverVersion);

/// <summary>A private candidate, not an applied repair. Contains the processor's full private manifest.</summary>
public sealed class MissingManagedLightConfigurationPlan
{
    internal MissingManagedLightConfigurationPlan(ManagedLightConfigurationIdentity identity,string before,string after,byte[] bytes,
        IReadOnlyList<string> attributes,int oldConfigurationNeeded)
    { Identity=identity; OriginalSha256=before; CandidateSha256=after; _bytes=bytes; AddedAttributes=attributes; OriginalConfigurationNeeded=oldConfigurationNeeded; }
    private readonly byte[] _bytes;
    public ManagedLightConfigurationIdentity Identity { get; }
    public string OriginalSha256 { get; }
    public string CandidateSha256 { get; }
    public IReadOnlyList<string> AddedAttributes { get; }
    public int OriginalConfigurationNeeded { get; }
    /// <summary>Copies private manifest bytes for an encrypted backup/transfer pipeline. Never include them in logs or public reports.</summary>
    public byte[] CopyPrivateCandidateBytes()=>(byte[])_bytes.Clone();
    /// <summary>Saves the candidate locally without overwriting an existing file. Never uploads or applies it.</summary>
    public void SavePrivateCandidate(string newPath)
    {
        using var stream=new FileStream(newPath,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        stream.Write(_bytes);stream.Flush(true);
    }
    public override string ToString()=>$"Unapplied configuration candidate for device {Identity.DeviceId}; added {string.Join(", ",AddedAttributes)}";
}

/// <summary>Prepares a narrowly scoped candidate for missing Kasa/Tapo native-child configuration records.</summary>
/// <remarks>This is an experimental offline planner. It never edits a processor. Applying a candidate requires
/// a separately reviewed stopped-Home procedure, an unchanged source hash, a retained original and recovery validation.
/// Templates must be actual complete records from a healthy child of the same platform/version; arbitrary store data is rejected.</remarks>
public static class MissingManagedLightConfiguration
{
    private const string CatalogueKey="tp-link.kasatapoplatform.cloud.neilcolvin";
    private static readonly HashSet<string> MarkerFields=new(StringComparer.Ordinal)
    {"$id","$type","Label","Description","AttributeId","DefaultValue","DefaultValueLabel","UserPromptMode","AttributeType","DataType",
        "RequiresPersistence","PersistedValue","SecurePersistedValue","AvailableValues","ValueHasRange","MinValue","MaxValue","Flags","UsageContext"};
    public static MissingManagedLightConfigurationPlan Plan(byte[] manifestBytes,string expectedSha256,
        ManagedLightConfigurationIdentity identity,int healthyReferenceId)
        =>PlanCore(manifestBytes,expectedSha256,identity,healthyReferenceId,null);

    /// <summary>Uses the exact lab-validated marker schema when every customer child lacks the markers.</summary>
    /// <remarks>Only the validated Home/driver pair is accepted. No marker values are inferred from another product or version.</remarks>
    public static MissingManagedLightConfigurationPlan PlanForValidatedHome(byte[] manifestBytes,string expectedSha256,
        ManagedLightConfigurationIdentity identity,string homeVersion)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if(homeVersion!="4.012.0194" || !DriverVersions.Equal(identity.DriverVersion,"2.1.002.0004"))
            throw new InvalidDataException("No validated saved-configuration template exists for this Home/driver pair.");
        using var stream=typeof(MissingManagedLightConfiguration).Assembly.GetManifestResourceStream("CrestronHomeDevTools.KasaNativeLightConfiguration.4.012.0194.json")
            ??throw new InvalidDataException("The validated template resource is missing.");
        using var templateBytes=new MemoryStream();stream.CopyTo(templateBytes);
        if(templateBytes.Length>16384 || Convert.ToHexString(SHA256.HashData(templateBytes.ToArray()))!="0B637E36D28F43B41D641723CEB2ECD3D43ABDB099D81ECFB95BC006049CDEF6")
            throw new InvalidDataException("The saved-configuration template differs from its validated bytes.");
        using var document=JsonDocument.Parse(templateBytes.ToArray());var template=document.RootElement;
        if(template.GetProperty("FormatVersion").GetInt32()!=1 || template.GetProperty("HomeVersion").GetString()!=homeVersion ||
            template.GetProperty("CatalogueKey").GetString()!=CatalogueKey || !DriverVersions.Equal(template.GetProperty("DriverVersion").GetString(),identity.DriverVersion))
            throw new InvalidDataException("The embedded template identity differs from its supported pair.");
        return PlanCore(manifestBytes,expectedSha256,identity,null,JsonNode.Parse(template.GetProperty("Attributes").GetRawText())!.AsArray());
    }

    private static MissingManagedLightConfigurationPlan PlanCore(byte[] manifestBytes,string expectedSha256,
        ManagedLightConfigurationIdentity identity,int? healthyReferenceId,JsonArray? validatedAttributes)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);ArgumentNullException.ThrowIfNull(identity);
        if(manifestBytes.Length>16*1024*1024)throw new InvalidDataException("Manifest exceeds the planning limit.");
        string digest=Convert.ToHexString(SHA256.HashData(manifestBytes));
        if(!digest.Equals(expectedSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Manifest differs from its pinned digest.");
        if(identity.DeviceId<=0 || identity.ParentId<=0 || identity.RoomId<=0 || healthyReferenceId is <=0 || healthyReferenceId==identity.DeviceId ||
            string.IsNullOrWhiteSpace(identity.Name) || string.IsNullOrWhiteSpace(identity.Model) || string.IsNullOrWhiteSpace(identity.ManagedDeviceId) || !Version.TryParse(identity.DriverVersion,out _))
            throw new ArgumentException("An exact child identity and a separate healthy reference are required.");
        var root=JsonNode.Parse(manifestBytes,documentOptions:new JsonDocumentOptions{MaxDepth=96}) as JsonObject??throw new InvalidDataException("Manifest is missing.");
        var references=new Dictionary<string,JsonObject>(StringComparer.Ordinal);int count=0;
        void Index(JsonNode? node)
        {
            if(++count>300000)throw new InvalidDataException("Manifest exceeds the object limit.");
            if(node is JsonObject obj)
            {
                if(obj["$id"] is JsonValue id && !references.TryAdd(id.GetValue<string>(),obj))throw new InvalidDataException("Duplicate manifest reference.");
                foreach(var value in obj)Index(value.Value);
            }
            else if(node is JsonArray array)foreach(var value in array)Index(value);
        }
        Index(root);
        JsonObject Object(JsonNode? node)
        {
            var obj=node as JsonObject??throw new InvalidDataException("Expected manifest object is missing.");
            if(obj["$ref"] is JsonValue id)
            {
                if(obj.Count!=1 || !references.TryGetValue(id.GetValue<string>(),out obj!))throw new InvalidDataException("Invalid manifest reference.");
            }
            return obj;
        }
        var devices=Object(root["Devices"]);
        var parent=Object(devices[identity.ParentId.ToString(CultureInfo.InvariantCulture)]);
        var child=Object(devices[identity.DeviceId.ToString(CultureInfo.InvariantCulture)]);
        var reference=healthyReferenceId is int referenceId?Object(devices[referenceId.ToString(CultureInfo.InvariantCulture)]):null;
        if(parent["Model"]?.GetValue<string>()!="KasaTapoPlatform" || Object(parent["DriverInformation"])["DriverGuid"]?.GetValue<string>()!=CatalogueKey ||
            !DriverVersions.Equal(Object(parent["DriverInformation"])["DriverVersion"]?.GetValue<string>(),identity.DriverVersion))
            throw new InvalidDataException("The expected Kasa/Tapo platform is missing.");
        void VerifyChild(JsonObject device,int id,bool target)
        {
            var driver=Object(device["DriverInformation"]);
            var parentInfo=Object(Object(device["ParentDeviceInfo"])["ParentInfo"]);
            if(device["Id"]?.GetValue<int>()!=id || parentInfo["Id"]?.GetValue<int>()!=identity.ParentId ||
                driver["DriverGuid"]?.GetValue<string>()!=CatalogueKey || !DriverVersions.Equal(driver["DriverVersion"]?.GetValue<string>(),identity.DriverVersion))
                throw new InvalidDataException("The child/reference driver identity differs from the selected platform.");
            if(!target)return;
            if(device["Name"]?.GetValue<string>()!=identity.Name || device["Model"]?.GetValue<string>()!=identity.Model ||
                device["RoomId"]?.GetValue<int>()!=identity.RoomId || device["DevType"]?.GetValue<int>()!=8 || device["InfoType"]?.GetValue<int>()!=3 ||
                !string.Equals(driver["DriverUxCategory"]?.GetValue<string>(),"light",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The target native light identity changed.");
            if(device["ConnectionInfos"] is not JsonArray connections || connections.Count!=1)throw new InvalidDataException("The native-light binding layout is unsupported.");
            var connection=Object(connections[0]);
            if(connection["GatewayId"]?.GetValue<int>()!=identity.ParentId || connection["GatewayManagedDeviceId"]?.GetValue<string>()!=identity.ManagedDeviceId)
                throw new InvalidDataException("The physical managed-device binding differs from the selected child.");
        }
        VerifyChild(child,identity.DeviceId,true);
        JsonArray referenceAttributes;
        if(reference!=null)
        {
            VerifyChild(reference,healthyReferenceId!.Value,false);
            if(reference["ConfigurationNeeded"]?.GetValue<int>()!=1 || reference["UserAttributes"] is not JsonArray recorded)
                throw new InvalidDataException("The reference does not have the validated configured record layout.");
            referenceAttributes=recorded;
        }
        else referenceAttributes=validatedAttributes??throw new InvalidDataException("No validated marker template is available.");
        if(child["UserAttributes"] is not null && child["UserAttributes"] is not JsonArray)
            throw new InvalidDataException("The target attribute layout is unsupported.");
        var attributes=child["UserAttributes"] as JsonArray??new JsonArray();
        var existing=attributes.Select(Object).ToArray();
        if(existing.Any(a=>a["AttributeId"]?.GetValue<string>() is "ActivationMarker" or "DriverDataStore"))
            throw new InvalidDataException("This planner only adds wholly missing configuration markers; existing values require separate analysis.");
        int configurationNeeded=child["ConfigurationNeeded"]?.GetValue<int>()??throw new InvalidDataException("Configuration state is missing.");
        if(configurationNeeded!=0)throw new InvalidDataException("The target does not match the validated missing-record state.");
        int nextId=1;
        foreach(string id in references.Keys)
        {
            if(!int.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out int numeric) || numeric>int.MaxValue-3)
                throw new InvalidDataException("Reference numbering is unsupported.");
            nextId=Math.Max(nextId,numeric+1);
        }
        foreach(string key in new[]{"ActivationMarker","DriverDataStore"})
        {
            var candidates=referenceAttributes.Select(Object).Where(a=>a["AttributeId"]?.GetValue<string>()==key).ToArray();
            if(candidates.Length!=1)throw new InvalidDataException("The healthy reference marker is missing or duplicated.");
            var template=candidates[0];bool activation=key=="ActivationMarker";
            if(template["$type"]?.GetValue<string>()!="Crestron.DeviceWrapper.RadUserAttribute, BaseDeviceWrapper" ||
                template["RequiresPersistence"]?.GetValue<bool>()!=true || template["AttributeType"]?.GetValue<int>()!=4 ||
                template["DataType"]?.GetValue<int>()!=(activation?3:1) || template["PersistedValue"]?.GetValue<string>()!=(activation?"true":"") ||
                template["SecurePersistedValue"] is not null || template["UsageContext"]?.GetValue<string>()!=(activation?"Generic.Prompt":"Data") ||
                template["Flags"]?.GetValue<int>()!=1 || template["AvailableValues"] is not JsonArray choices || choices.Count!=0 ||
                template.Any(p=>p.Value is JsonObject || !MarkerFields.Contains(p.Key)))
                throw new InvalidDataException("The reference marker does not match the validated empty-store schema.");
            var added=(JsonObject)template.DeepClone();added["$id"]=(nextId++).ToString(CultureInfo.InvariantCulture);attributes.Add(added);
        }
        if(child["UserAttributes"] is null)child["UserAttributes"]=attributes;
        child["ConfigurationNeeded"]=1;
        byte[] candidate=JsonSerializer.SerializeToUtf8Bytes(root);
        return new(identity,digest,Convert.ToHexString(SHA256.HashData(candidate)),candidate,new[]{"ActivationMarker","DriverDataStore"},configurationNeeded);
    }
}
