// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrestronHomeDevTools;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

/// <summary>Rehearsal runs real permitted tests and prepares documents, but cannot sign or contact delivery providers.</summary>
public enum SubmissionAutomationMode { Rehearsal, Submit }

public sealed record SubmissionAutomationSettings(int SchemaVersion, string PrivateRoot, SubmissionWorkflowRelease Release,
 string SourceRepository, SubmissionPackageRequirements PackageRequirements, string CredentialBindings,
 WorkflowPlan NUnit, SubmissionEnduranceWorkerPlan? Endurance = null,
 SubmissionAutomationMode Mode = SubmissionAutomationMode.Rehearsal);

internal static class AutomationFiles
{
 internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive=true,
  UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters=true,
  RespectNullableAnnotations=true, AllowDuplicateProperties=false, WriteIndented=true,
  Converters={new JsonStringEnumConverter(allowIntegerValues:false)} };
 internal static string Hash(string path) { using var f=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(f)); }
 internal static T Read<T>(string path) {
  if(new FileInfo(path).Length>16*1024*1024) throw new InvalidDataException("Retained document exceeds its size limit.");
  return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path),Json)??throw new InvalidDataException("Empty document.");
 }
 internal static void Write<T>(string path,T value) {
  byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value,Json);
  if(File.Exists(path)) { if(!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("Retained operation record changed.");return; }
  string temp=path+".tmp";
  using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(bytes);f.Flush(true);}
  File.Move(temp,path);
 }
 internal static SubmissionWorkflowStepResult Complete<T>(SubmissionWorkflowStepContext context,string name,T receipt) {
  var path=Path.Combine(context.RunDirectory,name);Write(path,receipt);
  return new(SubmissionWorkflowStatus.Completed,new(name,Hash(path)));
 }
}
