// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License. See LICENSE.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CrestronHomeDevTools;

public sealed record ConfigurationReferenceChange(string Path,int OldId,int NewId);
public sealed record ConfigurationReferenceRemap(JsonElement Value,IReadOnlyList<ConfigurationReferenceChange> Changes,IReadOnlyList<string> UnsupportedReferences);

/// <summary>Remaps known device-reference DTOs, including Home's nested serialized programming data.</summary>
public static class ConfigurationReferenceRemapper
{
    /// <remarks>Unknown possible references are reported, never guessed. Callers must reject them before removing anything.</remarks>
    public static ConfigurationReferenceRemap Remap(JsonElement definition,IReadOnlyDictionary<int,int> deviceIds)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);
        if(deviceIds.Count==0 || deviceIds.Any(p=>p.Key<=0 || p.Value<=0) || deviceIds.Values.Distinct().Count()!=deviceIds.Count)
            throw new ArgumentException("Distinct positive device identifiers are required.",nameof(deviceIds));
        if(definition.GetRawText().Length>16*1024*1024)throw new InvalidDataException("Definition exceeds the remapping limit.");
        var changes=new List<ConfigurationReferenceChange>(); var unsupported=new HashSet<string>(StringComparer.Ordinal); int nodes=0;
        JsonNode? Visit(JsonNode? node,string path,string? field,int depth)
        {
            if(++nodes>300000 || depth>96)throw new InvalidDataException("Definition exceeds the remapping depth or object limit.");
            if(node is JsonObject obj)
            {
                bool entity=obj.TryGetPropertyValue("EntityType",out var entityType) &&
                    (String(entityType)=="Device" || Integer(entityType)==2);
                bool lightTarget=String(obj["Type"])=="light";
                foreach(string key in obj.Select(p=>p.Key).ToArray())
                {
                    var value=obj[key]; string itemPath=path+"/"+key.Replace("~","~0",StringComparison.Ordinal).Replace("/","~1",StringComparison.Ordinal);
                    bool known=(key=="Id"&&(entity||lightTarget)) || key=="DeviceId";
                    if(known && Integer(value) is int id && deviceIds.TryGetValue(id,out int replacement))
                    {
                        if(id!=replacement){obj[key]=replacement;changes.Add(new(itemPath,id,replacement));}
                        continue;
                    }
                    var visited=Visit(value,itemPath,key,depth+1);
                    if(!ReferenceEquals(value,visited))obj[key]=visited;
                }
                return obj;
            }
            if(node is JsonArray array)
            {
                for(int i=0;i<array.Count;i++)
                {
                    var value=array[i];var visited=Visit(value,path+"/"+i,field,depth+1);
                    if(!ReferenceEquals(value,visited))array[i]=visited;
                }
                return array;
            }
            if(node is JsonValue scalar)
            {
                if(field=="EntityType")return node;
                if(Integer(scalar) is int possible && deviceIds.ContainsKey(possible))unsupported.Add(path);
                if(scalar.TryGetValue<string>(out var text))
                {
                    if(field is "ProgrammingData" or "SequenceProgrammingBlockJson")
                    {
                        if(text.Length>16*1024*1024)throw new InvalidDataException("Serialized programming exceeds limit.");
                        JsonNode? nested;
                        try{nested=JsonNode.Parse(text);}catch(JsonException){throw new InvalidDataException("Serialized programming is not valid JSON.");}
                        var mapped=Visit(nested,path+"/$json",null,depth+1);
                        return JsonValue.Create(mapped?.ToJsonString()??"null");
                    }
                    foreach(int id in deviceIds.Keys)
                        if(Regex.IsMatch(text,@"(?<![0-9])"+id.ToString(System.Globalization.CultureInfo.InvariantCulture)+@"(?![0-9])"))unsupported.Add(path);
                }
            }
            return node;
        }
        JsonNode? result=Visit(JsonNode.Parse(definition.GetRawText()),"",null,0);
        return new(JsonSerializer.SerializeToElement(result),changes,unsupported.Order(StringComparer.Ordinal).ToArray());
    }
    private static string? String(JsonNode? node)=>node is JsonValue value && value.TryGetValue<string>(out var text)?text:null;
    private static int? Integer(JsonNode? node)=>node is JsonValue value && value.TryGetValue<int>(out int number)?number:null;
}
