// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace CrestronHomeDevTools;

/// <summary>Owned run catalogue and bounded routine cleanup. Existing unregistered evidence is never adopted.
/// Closing requires the trusted owner to verify stopped work and cleanup, not merely a status file.</summary>
public sealed class SubmissionRunArchive : IDisposable
{
 private sealed record Entry(SubmissionRetentionGeneration Generation,string? ClosedStateSha256);
 private sealed record Catalogue(int SchemaVersion,List<Entry> Runs);
 private sealed record Closure(int SchemaVersion,string Key,string ClosedStateSha256);
 private sealed record Pruned(int SchemaVersion,string Key,string ClosedStateSha256,DateTimeOffset StartedUtc,bool Removed);
 private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,
  UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectRequiredConstructorParameters=true,
  RespectNullableAnnotations=true,AllowDuplicateProperties=false};
 private readonly string root,metadata;
 private readonly FileStream gate;
 private Catalogue catalogue;
 private SubmissionRunArchive(string privateRoot) {
  root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(privateRoot));
  if(!Path.IsPathFullyQualified(privateRoot)||!Directory.Exists(root))throw new ArgumentException("Use an existing protected workflow root.");
  NoLinks(root);metadata=Path.Combine(root,".retention");
  Directory.CreateDirectory(metadata);NoLinks(metadata);
  string lockPath=Path.Combine(metadata,"catalogue.lock");if(File.Exists(lockPath))NoLinks(lockPath);
  gate=new(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  try {
   string path=Path.Combine(metadata,"catalogue.json");
   catalogue=File.Exists(path)?Read<Catalogue>(path):new(1,[]);
   if(catalogue.SchemaVersion!=1 || catalogue.Runs==null || catalogue.Runs.Count>100000 ||
    catalogue.Runs.Any(e=>e==null || e.Generation==null) ||
    catalogue.Runs.Select(e=>e.Generation.Key).Distinct(StringComparer.Ordinal).Count()!=catalogue.Runs.Count)
    throw new InvalidDataException("Invalid retention catalogue.");
   foreach(var entry in catalogue.Runs) {
    _=RunPath(entry.Generation.Key);
    if(entry.Generation.Closed!=(entry.ClosedStateSha256!=null))throw new InvalidDataException("Incomplete closure record.");
    if(entry.ClosedStateSha256!=null && !Digest(entry.ClosedStateSha256))throw new InvalidDataException("Invalid closure digest.");
   }
  } catch {gate.Dispose();throw;}
 }
 internal static SubmissionRunArchive Acquire(string root)=>new(root);
 public void Dispose()=>gate.Dispose();

 /// <summary>Before creating candidate files, reject a previously pruned release. Does not create metadata.</summary>
 public static void RejectPruned(string root,SubmissionWorkflowRelease release) {
  string key=SubmissionWorkflow.RunKey(release);
  if(File.Exists(Path.Combine(root,".retention","pruned",key+".json")))
   throw new InvalidOperationException("This historical run was pruned. It cannot be replayed by a duplicate release event.");
 }
 internal static void RejectClosed(string directory) {
  if(File.Exists(Path.Combine(directory,"retention-closed.json")))
   throw new InvalidOperationException("This run has been closed for archival retention and cannot execute or recover.");
 }
 internal void RegisterNew(SubmissionWorkflowRelease release) {
  string key=SubmissionWorkflow.RunKey(release);RejectPruned(root,release);
  if(catalogue.Runs.Any(e=>e.Generation.Key==key))throw new InvalidDataException("A registered run lost its checkpoint; do not recreate it.");
  long sequence=checked(catalogue.Runs.Where(e=>string.Equals(e.Generation.Repository,release.Repository,StringComparison.OrdinalIgnoreCase))
   .Select(e=>e.Generation.Sequence).DefaultIfEmpty(0).Max()+1);
  catalogue.Runs.Add(new(new(key,release.Repository,sequence,false,false,false,[]),null));SaveCatalogue();
 }
 internal void RejectMissingCheckpoint(SubmissionWorkflowRelease release) {
  if(catalogue.Runs.Any(e=>e.Generation.Key==SubmissionWorkflow.RunKey(release)))
   throw new InvalidDataException("A registered run lost its checkpoint; do not recreate it.");
 }

 /// <summary>Close one known generation after verifying actual worker termination, restoration and released resources.
 /// The callback runs under catalogue and workflow locks. A submitted or review-stage run remains protected.</summary>
 public static void Close(string privateRoot,SubmissionWorkflowRelease release,Action<SubmissionWorkflowStepContext> verifyQuiescent) {
  ArgumentNullException.ThrowIfNull(verifyQuiescent);
  using var store=Acquire(privateRoot);RejectPruned(privateRoot,release);
  string key=SubmissionWorkflow.RunKey(release);var entry=store.Find(key);
  SubmissionWorkflow.WithVerifiedCheckpoint(privateRoot,release,c=> {
   if(c.Checkpoint.Status is SubmissionWorkflowStatus.Running or SubmissionWorkflowStatus.Waiting)
    throw new InvalidOperationException("Reconcile running work before archival closure.");
   verifyQuiescent(c);
   string hash=store.Hash(Path.Combine(c.RunDirectory,"state.json"));
   if(entry.Generation.Closed && entry.ClosedStateSha256!=hash)throw new InvalidDataException("Closed checkpoint changed.");
   bool submitted=c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.Deliver);
   bool review=c.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.PrepareReview) ||
    c.Checkpoint.Stage==SubmissionWorkflowStage.PrepareReview && c.Checkpoint.Status!=SubmissionWorkflowStatus.Ready ||
    c.Checkpoint.Stage is SubmissionWorkflowStage.SignReview or SubmissionWorkflowStage.Deliver or SubmissionWorkflowStage.Retain;
   var closed=entry with {Generation=entry.Generation with{Closed=true,Submitted=submitted,Pinned=entry.Generation.Pinned||review},ClosedStateSha256=hash};
   store.Write(Path.Combine(c.RunDirectory,"retention-closed.json"),new Closure(1,key,hash));
   store.Replace(closed);store.SaveCatalogue();return true;
  });
 }

 /// <summary>Register dependencies before reusing another generation's evidence. Pins can only be added here.</summary>
 public static void Protect(string privateRoot,SubmissionWorkflowRelease release,IReadOnlyList<string> dependencies,bool pin=false) {
  ArgumentNullException.ThrowIfNull(dependencies);
  using var store=Acquire(privateRoot);RejectPruned(privateRoot,release);
  var entry=store.Find(SubmissionWorkflow.RunKey(release));
  foreach(string key in dependencies) {
   _=store.Find(key);if(File.Exists(store.PrunedPath(key)))throw new InvalidDataException("Dependency has already been pruned.");
   if(!Directory.Exists(store.RunPath(key)))throw new InvalidDataException("Dependency evidence is missing.");
  }
  var next=entry with{Generation=entry.Generation with{Pinned=entry.Generation.Pinned||pin,
   Dependencies=entry.Generation.Dependencies.Concat(dependencies).Distinct(StringComparer.Ordinal).ToArray()}};
  store.Replace(next);store.SaveCatalogue();
 }

 internal void Rotate(SubmissionWorkflowRelease release) {
  RecoverPruning();
  var live=catalogue.Runs.Where(e=>!File.Exists(PrunedPath(e.Generation.Key))).Select(e=>e.Generation).ToArray();
  var plan=SubmissionRunRetention.Plan(SubmissionWorkflow.RunKey(release),live);
  foreach(string key in plan.Remove) {
   var entry=Find(key);string directory=RunPath(key);
   if(!entry.Generation.Closed || entry.ClosedStateSha256==null)throw new InvalidDataException("An unclosed run cannot be removed.");
   CheckTree(directory);
   using var intake=LockRun(directory,"intake.lock");using var run=LockRun(directory,"run.lock");
   if(Hash(Path.Combine(directory,"state.json"))!=entry.ClosedStateSha256 ||
    Read<Closure>(Path.Combine(directory,"retention-closed.json"))!=new Closure(1,key,entry.ClosedStateSha256))
    throw new InvalidDataException("Closed evidence changed before pruning.");
   // Durable outside-run identity prevents replay even after a crash during recursive removal.
   string tombstone=PrunedPath(key);Directory.CreateDirectory(Path.GetDirectoryName(tombstone)!);NoLinks(Path.GetDirectoryName(tombstone)!);
   var record=new Pruned(1,key,entry.ClosedStateSha256,DateTimeOffset.UtcNow,false);Write(tombstone,record);
   DeleteTree(directory);Write(tombstone,record with{Removed=true});
  }
 }
 internal void RecoverPruning() {
  foreach(var entry in catalogue.Runs) {
   string path=PrunedPath(entry.Generation.Key);if(!File.Exists(path))continue;
   var record=Read<Pruned>(path);
   if(record.SchemaVersion!=1 || record.Key!=entry.Generation.Key || record.ClosedStateSha256!=entry.ClosedStateSha256 || !entry.Generation.Closed)
    throw new InvalidDataException("Invalid pruning receipt.");
   string directory=RunPath(record.Key);
   if(record.Removed) {if(Directory.Exists(directory))throw new InvalidDataException("A pruned run directory was recreated.");continue;}
   if(Directory.Exists(directory)) {
    CheckTree(directory);
    using var intake=LockRun(directory,"intake.lock");using var run=LockRun(directory,"run.lock");
    DeleteTree(directory);
   }
   Write(path,record with{Removed=true});
  }
 }
 private Entry Find(string key)=>catalogue.Runs.SingleOrDefault(e=>e.Generation.Key==key)??throw new InvalidDataException("Generation is not registered; legacy evidence is protected.");
 private void Replace(Entry entry) {catalogue.Runs[catalogue.Runs.FindIndex(e=>e.Generation.Key==entry.Generation.Key)]=entry;}
 private string RunPath(string key) {
  if(!Digest(key))throw new InvalidDataException("Invalid generation key.");
  string result=Path.GetFullPath(Path.Combine(root,key));
  if(!string.Equals(Path.GetDirectoryName(result),root,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
   throw new InvalidDataException("Run path escapes retention root.");
  return result;
 }
 private string PrunedPath(string key) {_=RunPath(key);return Path.Combine(metadata,"pruned",key+".json");}
 private static bool Digest(string key)=>key is {Length:64}&&key.All(c=>char.IsAsciiDigit(c)||c is >= 'a' and <= 'f');
 private FileStream LockRun(string directory,string name) {string path=Path.Combine(directory,name);if(File.Exists(path))NoLinks(path);return new(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Delete);}
 private void NoLinks(string path) {
  string full=Path.GetFullPath(path);
  var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
  if(!string.Equals(full,root,comparison) && !full.StartsWith(root+Path.DirectorySeparatorChar,comparison))
   throw new InvalidDataException("Retention path escapes the configured root.");
  // The configured root may itself be a trusted mount. Only descendants are owned and removable.
  for(string? part=full;part!=null && !string.Equals(part,root,comparison);part=Path.GetDirectoryName(part))
   if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Retention paths cannot be redirected.");
 }
 private void CheckTree(string directory) {
  NoLinks(directory);var pending=new Stack<string>();pending.Push(directory);int count=0;
  while(pending.TryPop(out var folder))foreach(string path in Directory.EnumerateFileSystemEntries(folder)) {
   if(++count>1000000)throw new InvalidDataException("Retention tree exceeds its bound.");
   var attributes=File.GetAttributes(path);
   if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Retention tree contains a redirected path.");
   if((attributes&FileAttributes.Directory)!=0)pending.Push(path);
  }
 }
 private void DeleteTree(string directory) {CheckTree(directory);Directory.Delete(directory,true);}
 private T Read<T>(string path) {
  NoLinks(path);if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException("Retention document exceeds its bound.");
  return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path),Json)??throw new InvalidDataException("Missing retention document.");
 }
 private string Hash(string path) {NoLinks(path);using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream));}
 private void SaveCatalogue()=>Write(Path.Combine(metadata,"catalogue.json"),catalogue);
 private void Write<T>(string path,T value) {
  NoLinks(Path.GetDirectoryName(path)!);if(File.Exists(path))NoLinks(path);
  string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {
   byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value,Json);
   if(bytes.Length>16*1024*1024)throw new InvalidDataException("Retention document exceeds its bound.");
   using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes);stream.Flush(true);}
   SubmissionJournalFile.Replace(temporary,path);
  } finally {if(File.Exists(temporary))File.Delete(temporary);}
 }
}
