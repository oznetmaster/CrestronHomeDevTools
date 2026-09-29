// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

public enum SubmissionOperatorOutcome { Done, Unable, Cancelled, Expired }
public sealed record SubmissionOperatorRequest(int SchemaVersion, string Id, string RunKey,
 string Step, string Target, string Instructions, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc)
{
 [JsonIgnore] public bool IsReadiness => SchemaVersion==2 && ExpiresUtc==DateTimeOffset.MaxValue;
 public bool IsExpired(DateTimeOffset now)=>!IsReadiness && now>=ExpiresUtc;
}
public sealed record SubmissionOperatorHandle(string Directory, string RequestSha256);
public sealed record SubmissionOperatorResponse(int SchemaVersion, string RequestId, string RequestSha256,
 SubmissionOperatorOutcome Outcome, DateTimeOffset RecordedUtc)
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public string? Reason {get;init;}
}
public sealed record SubmissionOperatorStatus(SubmissionOperatorRequest Request, SubmissionOperatorResponse? Response)
{
 public bool Waiting => Response == null;
}

/// <summary>Durable operator participation, never evidence of a physical event or a passing test.
/// Keep the directory private and on storage shared by the fixture and operator UI.</summary>
public static class SubmissionOperatorStep
{
 private static readonly JsonSerializerOptions Json = new() {
  PropertyNamingPolicy=JsonNamingPolicy.CamelCase, WriteIndented=true,
  UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,
  RespectRequiredConstructorParameters=true, AllowDuplicateProperties=false,
  Converters={new JsonStringEnumConverter(allowIntegerValues:false)}
 };
 public static SubmissionOperatorHandle Create(string parentDirectory, string runKey, string step,
  string target, string instructions, TimeSpan timeout) => Create(parentDirectory,runKey,step,target,instructions,timeout,TimeProvider.System);
 /// <summary>Reopens an immutable readiness checkpoint after a worker restart. Readiness is
 /// not permission to operate hardware and is never evidence that a test passed.</summary>
 public static SubmissionOperatorHandle GetOrCreateReadiness(string parentDirectory,string runKey,string step,
  string target,string instructions) {
  if(!Path.IsPathFullyQualified(parentDirectory) || !Hex(runKey) || !Text(step,128) || !Text(target,256) || !Text(instructions,4096))
   throw new ArgumentException("Readiness requires the exact private inbox, run, step and instructions.");
  System.IO.Directory.CreateDirectory(parentDirectory);
  using var gate=new FileStream(Path.Combine(parentDirectory,"readiness.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  var matches=new List<SubmissionOperatorHandle>();int count=0;
  foreach(string directory in System.IO.Directory.EnumerateDirectories(parentDirectory)) {
   if(++count>1024)throw new InvalidDataException("Operator inbox exceeds its bound.");
   if(!Guid.TryParseExact(Path.GetFileName(directory),"N",out _) || !File.Exists(Path.Combine(directory,"ready.sha256")))continue;
   if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Operator request directory is a link.");
   var handle=new SubmissionOperatorHandle(directory,System.Text.Encoding.ASCII.GetString(Bytes(Path.Combine(directory,"ready.sha256"))));
   var request=Read(handle).Request;
   if(request.RunKey!=runKey || request.Step!=step)continue;
   if(!request.IsReadiness || request.Target!=target || request.Instructions!=instructions)
    throw new InvalidDataException("Retained readiness instructions changed; use a new explicitly planned attempt.");
   matches.Add(handle);
  }
  if(matches.Count>1)throw new InvalidDataException("Ambiguous readiness checkpoint.");
  return matches.Count==1?matches[0]:Create(parentDirectory,runKey,step,target,instructions,Timeout.InfiniteTimeSpan,TimeProvider.System);
 }
 internal static SubmissionOperatorHandle Create(string parentDirectory, string runKey, string step,
  string target, string instructions, TimeSpan timeout, TimeProvider clock) {
  if(!Path.IsPathFullyQualified(parentDirectory) || !Hex(runKey) ||
   !Text(step,128) || !Text(target,256) || !Text(instructions,4096) ||
   (timeout!=Timeout.InfiniteTimeSpan && (timeout<=TimeSpan.Zero || timeout>TimeSpan.FromDays(1))))
   throw new ArgumentException("Operator steps require a private absolute directory, run identity, explicit target/instructions and bounded timeout.");
  var now=clock.GetUtcNow();string id=Guid.NewGuid().ToString("N"),directory=Path.Combine(parentDirectory,id);
  if(SubmissionOperatorInboxLifecycle.IsClosed(new(parentDirectory,runKey)))throw new InvalidOperationException("Operator inbox is closed for this run.");
  System.IO.Directory.CreateDirectory(directory);
  bool readiness=timeout==Timeout.InfiniteTimeSpan;
  byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(new SubmissionOperatorRequest(readiness?2:1,id,runKey,step,target,instructions,now,
   readiness?DateTimeOffset.MaxValue:now+timeout),Json);
  WriteNew(Path.Combine(directory,"request.json"),bytes);
  string digest=Convert.ToHexStringLower(SHA256.HashData(bytes));
  WriteNew(Path.Combine(directory,"ready.sha256"),System.Text.Encoding.ASCII.GetBytes(digest));
  return new(directory,digest);
 }
 /// <summary>Read a bounded, nonrecursive private inbox. Paths are resolved on this computer so
 /// the same shared directory can have a different mount path on the operator desktop.</summary>
 public static IReadOnlyList<SubmissionOperatorHandle> Pending(string parentDirectory,string runKey) {
  if(!Path.IsPathFullyQualified(parentDirectory) || !Hex(runKey))throw new ArgumentException("Use an absolute private inbox and exact run identity.");
  var pending=new List<(SubmissionOperatorHandle Handle,DateTimeOffset Created)>();int count=0;
  foreach(string directory in System.IO.Directory.EnumerateDirectories(parentDirectory)) {
   if(++count>1024)throw new InvalidDataException("Operator inbox exceeds its bound; archive completed runs.");
   if(!Guid.TryParseExact(Path.GetFileName(directory),"N",out _))continue;
   if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Operator request directory is a link.");
   string ready=Path.Combine(directory,"ready.sha256");
   if(!File.Exists(ready))continue; // Publication is not complete yet.
   var handle=new SubmissionOperatorHandle(directory,System.Text.Encoding.ASCII.GetString(Bytes(ready)));
   var status=Read(handle);
   if(status.Request.RunKey==runKey && status.Waiting && !status.Request.IsExpired(DateTimeOffset.UtcNow))
    pending.Add((handle,status.Request.CreatedUtc));
  }
  return pending.OrderBy(p=>p.Created).ThenBy(p=>p.Handle.Directory,StringComparer.Ordinal).Select(p=>p.Handle).ToArray();
 }
 public static SubmissionOperatorStatus Read(SubmissionOperatorHandle handle) => Read(handle,TimeProvider.System);
 internal static SubmissionOperatorStatus Read(SubmissionOperatorHandle handle,TimeProvider clock) {
  if(!Path.IsPathFullyQualified(handle.Directory) || !Hex(handle.RequestSha256))throw new ArgumentException("Invalid operator handle.");
  byte[] bytes=Bytes(Path.Combine(handle.Directory,"request.json"));
  if(Convert.ToHexStringLower(SHA256.HashData(bytes))!=handle.RequestSha256)throw new InvalidDataException("Operator request changed.");
  var request=JsonSerializer.Deserialize<SubmissionOperatorRequest>(bytes,Json)??throw new InvalidDataException("Missing operator request.");
  if(request.SchemaVersion is not (1 or 2) || (request.SchemaVersion==2 && !request.IsReadiness) || !Guid.TryParseExact(request.Id,"N",out _) || !Hex(request.RunKey) ||
   Path.GetFileName(Path.TrimEndingDirectorySeparator(handle.Directory))!=request.Id ||
   !Text(request.Step,128) || !Text(request.Target,256) || !Text(request.Instructions,4096) ||
   request.CreatedUtc==default || request.ExpiresUtc<=request.CreatedUtc || (!request.IsReadiness && request.ExpiresUtc-request.CreatedUtc>TimeSpan.FromDays(1)))
   throw new InvalidDataException("Invalid operator request.");
  string responsePath=Path.Combine(handle.Directory,"response.json");
  SubmissionOperatorResponse? response=null;
  if(File.Exists(responsePath))response=JsonSerializer.Deserialize<SubmissionOperatorResponse>(Bytes(responsePath),Json)
   ??throw new InvalidDataException("Empty operator response.");
  if(response!=null && (response.SchemaVersion is not (1 or 2) || response.RequestId!=request.Id || response.RequestSha256!=handle.RequestSha256 ||
   (response.Reason!=null && (response.SchemaVersion!=2 || !Text(response.Reason,2048) || response.Outcome!=SubmissionOperatorOutcome.Unable)) ||
   (request.IsReadiness && response.Outcome==SubmissionOperatorOutcome.Unable && string.IsNullOrWhiteSpace(response.Reason)) ||
   !Enum.IsDefined(response.Outcome) || response.RecordedUtc<request.CreatedUtc || response.RecordedUtc>clock.GetUtcNow() ||
   (response.Outcome is SubmissionOperatorOutcome.Done or SubmissionOperatorOutcome.Unable && response.RecordedUtc>=request.ExpiresUtc) ||
   (response.Outcome==SubmissionOperatorOutcome.Expired && (request.IsReadiness || response.RecordedUtc<request.ExpiresUtc))))
   throw new InvalidDataException("Operator response does not belong to this live request.");
  return new(request,response);
 }
 /// <summary>Records only the operator's acknowledgement. The fixture must verify the device/event separately.</summary>
 public static SubmissionOperatorResponse Respond(SubmissionOperatorHandle handle, SubmissionOperatorOutcome outcome) {
  return Respond(handle,outcome,null);
 }
 public static SubmissionOperatorResponse Respond(SubmissionOperatorHandle handle, SubmissionOperatorOutcome outcome,string? reason) {
  if(outcome is not (SubmissionOperatorOutcome.Done or SubmissionOperatorOutcome.Unable))throw new ArgumentException("Choose Done or Unable.");
  if(reason!=null && (!Text(reason,2048) || outcome!=SubmissionOperatorOutcome.Unable))throw new ArgumentException("A reason belongs to Cannot perform this action and must be 1-2048 characters.");
  if(Read(handle).Request.IsReadiness && outcome==SubmissionOperatorOutcome.Unable && string.IsNullOrWhiteSpace(reason))
   throw new ArgumentException("Explain why the action cannot be performed.");
  return Finish(handle,outcome,TimeProvider.System,reason);
 }
 internal static SubmissionOperatorResponse Finish(SubmissionOperatorHandle handle, SubmissionOperatorOutcome outcome, TimeProvider clock,string? reason=null) {
  using var gate=OpenGate(handle,clock);
  var status=Read(handle,clock);var now=clock.GetUtcNow();
  if(status.Response!=null)throw new InvalidOperationException("This operator step already has a recorded outcome.");
  if(now<status.Request.CreatedUtc || !Enum.IsDefined(outcome))throw new InvalidDataException("Invalid response clock or outcome.");
  if(status.Request.IsExpired(now)){outcome=SubmissionOperatorOutcome.Expired;reason=null;}
  else if(outcome==SubmissionOperatorOutcome.Expired)throw new InvalidOperationException("Request has not expired.");
  var response=new SubmissionOperatorResponse(reason==null?1:2,status.Request.Id,handle.RequestSha256,outcome,now){Reason=reason};
  WriteNew(Path.Combine(handle.Directory,"response.json"),JsonSerializer.SerializeToUtf8Bytes(response,Json));
  return response;
 }
 /// <summary>Waits without an AI session. Cancellation/expiry is retained and never interpreted as completion.</summary>
 public static async Task<SubmissionOperatorResponse> WaitAsync(SubmissionOperatorHandle handle, CancellationToken token=default) {
  int contentionRetries=0;
  while(true) {
   var status=Read(handle);
   if(status.Response!=null)return status.Response;
   // Worker shutdown leaves unattended readiness pending for the next process.
   if(status.Request.IsReadiness)token.ThrowIfCancellationRequested();
   if(token.IsCancellationRequested || status.Request.IsExpired(DateTimeOffset.UtcNow)) {
    try { return Finish(handle,token.IsCancellationRequested?SubmissionOperatorOutcome.Cancelled:SubmissionOperatorOutcome.Expired,TimeProvider.System); }
    catch(ResponseBusyException) when(contentionRetries++<20) { await Task.Delay(100,CancellationToken.None).ConfigureAwait(false); continue; }
    catch(InvalidOperationException) {
     // Only a validated competing response explains this race; otherwise propagate the error.
     var response=Read(handle).Response;
     if(response!=null)return response;
     throw;
    }
   }
   try { await Task.Delay(TimeSpan.FromSeconds(1),token).ConfigureAwait(false); }
   catch(OperationCanceledException) when(token.IsCancellationRequested) { }
  }
 }
 private sealed class ResponseBusyException(IOException inner):IOException("Operator response is being recorded by another process.",inner);
 private static FileStream OpenGate(SubmissionOperatorHandle handle,TimeProvider clock) {
  // Validate before opening a writable file at a supplied path.
  _=Read(handle,clock);
  try {return new FileStream(Path.Combine(handle.Directory,"response.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
  catch(IOException e) when((e.HResult&0xffff) is 32 or 33) {throw new ResponseBusyException(e);}
 }
 private static bool Hex(string? value)=>value is {Length:64} && value.All(c=>char.IsAsciiDigit(c)||c is >= 'a' and <= 'f');
 private static bool Text(string? value,int maximum)=>!string.IsNullOrWhiteSpace(value) && value.Length<=maximum && !value.Any(c=>char.IsControl(c)&&c!='\r'&&c!='\n'&&c!='\t');
 private static byte[] Bytes(string path) {
  using var input=File.OpenRead(path);
  if(input.Length>32768)throw new InvalidDataException("Operator record is too large.");
  var bytes=new byte[checked((int)input.Length)];input.ReadExactly(bytes);return bytes;
 }
 private static void WriteNew(string path,byte[] bytes) {
  string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {
   using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {output.Write(bytes);output.Flush(true);}
   File.Move(temporary,path,false);
  } finally {if(File.Exists(temporary))File.Delete(temporary);}
 }
}
