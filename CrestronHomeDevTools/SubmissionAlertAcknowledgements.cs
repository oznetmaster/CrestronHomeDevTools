// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
namespace CrestronHomeDevTools;

/// <summary>Local notification dismissals only. These never acknowledge a physical action or authorize submission.</summary>
public sealed class SubmissionAlertAcknowledgements
{
 private readonly string path;
 private readonly HashSet<string> dismissed;
 public SubmissionAlertAcknowledgements(string path) {
  if(!Path.IsPathFullyQualified(path))throw new ArgumentException("Use an absolute local acknowledgement file.");
  this.path=path;
  if(File.Exists(path) && new FileInfo(path).Length>100000)throw new InvalidDataException("Alert acknowledgement file is too large.");
  dismissed=File.Exists(path)?new(JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(path))??throw new InvalidDataException("Invalid acknowledgements."),StringComparer.Ordinal):new(StringComparer.Ordinal);
  Validate(dismissed);
 }
 private static void Validate(IEnumerable<string> keys) {
  if(keys.Count()>1000 || keys.Any(k=>k is not {Length:64} || k.Any(c=>!char.IsAsciiHexDigit(c))))throw new InvalidDataException("Invalid alert keys.");
 }
 public bool IsDismissed(string key)=>dismissed.Contains(key);
 public void Dismiss(string key) {Validate([key]);if(dismissed.Add(key))Save();}
 public void RetainActive(IEnumerable<string> keys) {
  var active=keys.ToHashSet(StringComparer.Ordinal);Validate(active);
  if(dismissed.RemoveWhere(k=>!active.Contains(k))>0)Save();
 }
 private void Save() {
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {File.WriteAllBytes(temporary,JsonSerializer.SerializeToUtf8Bytes(dismissed.OrderBy(k=>k,StringComparer.Ordinal)));File.Move(temporary,path,true);}
  finally {if(File.Exists(temporary))File.Delete(temporary);}
 }
}
