// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace CrestronHomeDevTools;

public sealed record CrestronHomeLoadObservation(DateTimeOffset EarliestLoadedUtc,
 DateTimeOffset LatestLoadedUtc, string StartingLine, string LoadedLine, double LoadMilliseconds);
public sealed record CrestronHomeLoadEvidence(CrestronHomeLoadObservation Observation,
 ProcessorProgramUptimeSnapshot Program, IReadOnlyDictionary<string,string> Logs, DateTimeOffset ObservedUtc);

/// <summary>Interprets the Home diagnostic log's global Loaded System event, not a
/// device-ready message. The caller retains the log and verifies the program epoch
/// before and after reading it. Missing events never substitute API availability.</summary>
public static class CrestronHomeLoadLog
{
 private static readonly Regex Line=new(@"^L:[0-9]+ \[(?<time>[0-9]{2}:[0-9]{2}:[0-9]{2})\]: NotUserVisible\|Information\|System\|NA\|GeneralMessage\|\[\s*[0-9]+\] \[[^\]]+\] \[INFO\]\s+(?<message>.*)$",RegexOptions.CultureInvariant);
 private static readonly Regex Loaded=new(@"^System\\Runner: \* Loaded System: (?<ms>[0-9]+) ms \(total\)$",RegexOptions.CultureInvariant);
 public static DateTime LocalProgramStart(ProcessorProgramUptimeSnapshot program)
 {
  if(program.Program!=new ProcessorProgramIdentity("/simpl/app00","Crestron.Seawolf","Crestron.Seawolf.dll") ||
   !DateTime.TryParseExact(program.LocalStartedAtDiagnostic,"dddd, d MMMM yyyy 'at' HH:mm:ss",
    CultureInfo.InvariantCulture,DateTimeStyles.None,out var value))
   throw new InvalidDataException("Expected the verified Home program and its explicit local start timestamp.");
  return DateTime.SpecifyKind(value,DateTimeKind.Unspecified);
 }
 public static CrestronHomeLoadObservation? Parse(string text,DateOnly logDate,
  ProcessorProgramUptimeSnapshot program,DateTimeOffset observedUtc)=>Parse(new Dictionary<DateOnly,string>{{logDate,text}},program,observedUtc);
 public static CrestronHomeLoadObservation? Parse(IReadOnlyDictionary<DateOnly,string> logs,
  ProcessorProgramUptimeSnapshot program,DateTimeOffset observedUtc)
 {
  ArgumentNullException.ThrowIfNull(logs);
  var start=LocalProgramStart(program);
  var day=DateOnly.FromDateTime(start);
  if(logs.Count is <1 or >2 || !logs.ContainsKey(day) || logs.Keys.Any(d=>d!=day&&d!=day.AddDays(1)) ||
   logs.Values.Any(t=>t==null||t.Length>64*1024*1024) || observedUtc<program.ObservedUtc || program.ObservedUtc<program.RequestSentUtc ||
   program.Uptime<TimeSpan.Zero)
   throw new InvalidDataException("Log date, program clock or bounded input is invalid.");
  var entries=new List<(DateTime Time,string Message,string Raw)>();
  foreach(var (logDate,text) in logs.OrderBy(p=>p.Key))
  foreach(var raw in text.Split('\n')) {
   var match=Line.Match(raw.TrimEnd('\r'));if(!match.Success)continue;
   if(!TimeOnly.TryParseExact(match.Groups["time"].Value,"HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))
    throw new InvalidDataException("Invalid Home log timestamp.");
   entries.Add((logDate.ToDateTime(time),match.Groups["message"].Value,raw.TrimEnd('\r')));
  }
  // A boot's writer starts shortly after PROGUPTIME's epoch. A prior boot from
  // the same daily log is not evidence for this outage. Midnight is represented
  // by separate dated files, never by guessing from a decreasing time of day.
  var starts=entries.Select((e,i)=>(e,i)).Where(x=>x.e.Message=="ControlSystem: Starting Crestron Home" &&
   x.e.Time>=start && x.e.Time<=start.AddSeconds(30)).ToArray();
  if(starts.Length==0)return null;
  if(starts.Length!=1)throw new InvalidDataException("Ambiguous Home startup epoch.");
  var first=starts[0];
  var subsequent=entries.Skip(first.i+1).ToArray();
  if(subsequent.Any(e=>e.Message=="ControlSystem: Starting Crestron Home"))
   throw new InvalidDataException("Home restarted after the selected epoch.");
  var loads=subsequent.Where(e=>Loaded.IsMatch(e.Message)).ToArray();
  if(loads.Length==0)return null;
  if(loads.Length!=1)throw new InvalidDataException("Ambiguous Home load completion.");
  var load=loads[0];
  if(!double.TryParse(Loaded.Match(load.Message).Groups["ms"].Value,CultureInfo.InvariantCulture,out var elapsed) ||
   elapsed<=0 || elapsed>TimeSpan.FromMinutes(30).TotalMilliseconds || load.Time<first.e.Time ||
   Math.Abs((load.Time-first.e.Time).TotalMilliseconds-elapsed)>2000)
   throw new InvalidDataException("Home load duration and calendar disagree; do not infer a recovery clock.");
  // Both local text timestamps are quantized to seconds. Their difference,
  // anchored to independently bounded uptime, avoids assuming either host's
  // time zone or identical wall clocks. Keep a full second on each side.
  var delta=load.Time-start;
  var earliest=program.EarliestStartUtc+delta-TimeSpan.FromSeconds(1);
  var latest=program.LatestStartUtc+delta+TimeSpan.FromSeconds(1);
  if(earliest<program.EarliestStartUtc || earliest>observedUtc)
   throw new InvalidDataException("Home load event is outside the verified observation.");
  // A freshly written event can straddle now because the log and diagnostic
  // timestamps are rounded. Wait for the full conservative interval; do not
  // reject a valid startup or move its recovery anchor to the later read.
  if(latest>observedUtc)return null;
  return new(earliest,latest,first.e.Raw,load.Raw,elapsed);
 }
 /// <summary>Read existing daily Home logs using pinned SFTP. No processor changes.
 /// Retain only the final successful snapshot; missing load completion times out.</summary>
 public static async Task<CrestronHomeLoadEvidence> ReadAsync(string host,NetworkCredential credential,
  string sshFingerprint,ProcessorProgramUptimeSnapshot program,TimeSpan timeout,CancellationToken token=default)
 {
  var result=await ReadObservationAsync(host,credential,sshFingerprint,program,timeout,
   (logs,now)=>Parse(logs,program,now),token).ConfigureAwait(false);
  return new(result.Observation,program,result.Logs,result.ObservedUtc);
 }
 internal static async Task<(T Observation,IReadOnlyDictionary<string,string> Logs,DateTimeOffset ObservedUtc)> ReadObservationAsync<T>(
  string host,NetworkCredential credential,string sshFingerprint,ProcessorProgramUptimeSnapshot program,TimeSpan timeout,
  Func<IReadOnlyDictionary<DateOnly,string>,DateTimeOffset,T?> parse,CancellationToken token) where T:class
 {
  ArgumentException.ThrowIfNullOrWhiteSpace(host);ArgumentNullException.ThrowIfNull(credential);
  ArgumentException.ThrowIfNullOrWhiteSpace(sshFingerprint);
  if(timeout<=TimeSpan.Zero||timeout>TimeSpan.FromMinutes(5))throw new ArgumentOutOfRangeException(nameof(timeout));
  var day=DateOnly.FromDateTime(LocalProgramStart(program));
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timeout);
  using var sftp=new SftpClient(host,credential.UserName,credential.Password);
  sftp.ConnectionInfo.Timeout=TimeSpan.FromSeconds(15);sftp.OperationTimeout=TimeSpan.FromSeconds(15);
  sftp.HostKeyReceived+=(_,e)=>e.CanTrust=string.Equals(e.FingerPrintSHA256,sshFingerprint,StringComparison.Ordinal);
  await sftp.ConnectAsync(deadline.Token).ConfigureAwait(false);
  while(true) {
   deadline.Token.ThrowIfCancellationRequested();
   var dated=new Dictionary<DateOnly,string>();var retained=new Dictionary<string,string>();
   foreach(var date in new[]{day,day.AddDays(1)}) {
    string stem=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    var path="/rm/SeawolfDiagnostic/"+stem+".log";
    if(!sftp.Exists(path))path="/rm/SeawolfDiagnostic/Log-"+stem+".bac";
    if(!sftp.Exists(path))continue;
    var size=sftp.GetAttributes(path).Size;
    if(size>24*1024*1024)throw new InvalidDataException("Home diagnostic log exceeds the capture budget.");
    using var stream=sftp.OpenRead(path);using var bytes=new MemoryStream();
    var buffer=new byte[65536];int read;
    while((read=await stream.ReadAsync(buffer,deadline.Token).ConfigureAwait(false))!=0) {
     if(bytes.Length+read>24*1024*1024)throw new InvalidDataException("Home diagnostic log exceeds the capture budget.");
     bytes.Write(buffer,0,read);
    }
    string text=Encoding.UTF8.GetString(bytes.ToArray());dated.Add(date,text);retained.Add(path,text);
   }
   var now=DateTimeOffset.UtcNow;
   if(dated.ContainsKey(day)&&parse(dated,now) is {} observation)return (observation,retained,now);
   await Task.Delay(TimeSpan.FromSeconds(2),deadline.Token).ConfigureAwait(false);
  }
 }
}
