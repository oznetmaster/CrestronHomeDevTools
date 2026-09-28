// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
namespace CrestronHomeDevTools;

/// <summary>Completion closes only the operator monitor; request and response evidence is preserved.</summary>
public static class SubmissionOperatorInboxLifecycle
{
 private sealed record Completion(int SchemaVersion,string RunKey,DateTimeOffset CompletedUtc);
 private static string PathFor(SubmissionOperatorInbox inbox) {
  if(!Path.IsPathFullyQualified(inbox.Directory) || inbox.RunKey is not {Length:64} || inbox.RunKey.Any(c=>!char.IsAsciiDigit(c)&&c is not (>= 'a' and <= 'f')))
   throw new ArgumentException("Invalid operator inbox identity.");
  return Path.Combine(inbox.Directory,"completed-"+inbox.RunKey+".json");
 }
 public static bool IsClosed(SubmissionOperatorInbox inbox) {
  string path=PathFor(inbox);if(!File.Exists(path))return false;
  if(new FileInfo(path).Length>4096)throw new InvalidDataException("Invalid inbox completion record.");
  var record=JsonSerializer.Deserialize<Completion>(File.ReadAllBytes(path));
  if(record?.SchemaVersion!=1 || record.RunKey!=inbox.RunKey || record.CompletedUtc==default || record.CompletedUtc>DateTimeOffset.UtcNow)
   throw new InvalidDataException("Inbox completion belongs to another run or is invalid.");
  return true;
 }
 public static void Close(SubmissionOperatorInbox inbox) {
  if(IsClosed(inbox))return;
  // Never dismiss an action still visible to the operator. A successful review stage is
  // separately required by the workflow; this is not an evidence-acceptance operation.
  if(SubmissionOperatorStep.Pending(inbox.Directory,inbox.RunKey).Count!=0)
   throw new InvalidOperationException("Cannot close an inbox with pending physical actions.");
  string path=PathFor(inbox),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {
   using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
    JsonSerializer.Serialize(file,new Completion(1,inbox.RunKey,DateTimeOffset.UtcNow));file.Flush(true);
   }
   File.Move(temporary,path,false);
  } finally {if(File.Exists(temporary))File.Delete(temporary);}
 }
}
