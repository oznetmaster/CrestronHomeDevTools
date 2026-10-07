// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace CrestronHomeDevTools;

/// <summary>A specific driver's Running event. It does not assert device functionality.</summary>
public sealed record CrestronDriverLoadObservation(string DriverLogPath,
 DateTimeOffset EarliestLoadedUtc, DateTimeOffset LatestLoadedUtc,
 string CreatedLine, string RunningLine);
public sealed record CrestronDriverLoadEvidence(CrestronDriverLoadObservation Observation,
 ProcessorProgramUptimeSnapshot Program,IReadOnlyDictionary<string,string> Logs,DateTimeOffset ObservedUtc);

/// <summary>Extracts a driver-specific load event from retained Home logs. The caller
/// binds the exact driver path to the installed candidate and verifies the program
/// epoch around collection. Root and child events are never interchangeable.</summary>
public static class CrestronDriverLoadLog
{
 private static readonly Regex Line=new(@"^L:[0-9]+ \[(?<time>[0-9]{2}:[0-9]{2}:[0-9]{2})\]: NotUserVisible\|Information\|System\|NA\|GeneralMessage\|\[\s*[0-9]+\] \[[^\]]+\] \[INFO\]\s+(?<message>.*)$",RegexOptions.CultureInvariant);

 /// <summary>Read the bound driver's own load event without waiting for global
 /// Home completion. No equipment changes. Caller verifies the program epoch again.</summary>
 public static async Task<CrestronDriverLoadEvidence> ReadAsync(string host,NetworkCredential credential,
  string sshFingerprint,ProcessorProgramUptimeSnapshot program,string driverLogPath,TimeSpan timeout,CancellationToken token=default)
 {
  var result=await CrestronHomeLoadLog.ReadObservationAsync(host,credential,sshFingerprint,program,timeout,
   (logs,now)=>Parse(logs,program,now,driverLogPath),token).ConfigureAwait(false);
  return new(result.Observation,program,result.Logs,result.ObservedUtc);
 }

 public static CrestronDriverLoadObservation? Parse(IReadOnlyDictionary<DateOnly,string> logs,
  ProcessorProgramUptimeSnapshot program, DateTimeOffset observedUtc, string driverLogPath)
 {
  ArgumentException.ThrowIfNullOrWhiteSpace(driverLogPath);
  if(driverLogPath.Length>512 || !Regex.IsMatch(driverLogPath,@"^Drivers\\[^:\r\n]+\\[0-9]+$",RegexOptions.CultureInvariant))
   throw new ArgumentException("Bind the exact root or child driver log path.",nameof(driverLogPath));
  ArgumentNullException.ThrowIfNull(logs);
  var start=CrestronHomeLoadLog.LocalProgramStart(program);
  var day=DateOnly.FromDateTime(start);
  if(logs.Count is <1 or >2 || !logs.ContainsKey(day) || logs.Keys.Any(d=>d!=day&&d!=day.AddDays(1)) ||
   logs.Values.Any(t=>t==null||t.Length>64*1024*1024) || observedUtc<program.ObservedUtc ||
   program.ObservedUtc<program.RequestSentUtc || program.Uptime<TimeSpan.Zero)
   throw new InvalidDataException("Log date, program clock or bounded input is invalid.");
  var events=new List<(DateTime Time,string Message,string Raw)>();
  foreach(var (date,text) in logs.OrderBy(p=>p.Key))
  foreach(var raw in text.Split('\n')) {
   var line=raw.TrimEnd('\r');var match=Line.Match(line);if(!match.Success)continue;
   if(!TimeOnly.TryParseExact(match.Groups["time"].Value,"HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))
    throw new InvalidDataException("Invalid driver log timestamp.");
   var timestamp=date.ToDateTime(time);
   if(timestamp>=start)events.Add((timestamp,match.Groups["message"].Value,line));
  }
  // A driver's load must be observable before Home finishes loading unrelated
  // drivers. Validate the startup epoch without waiting for Loaded System.
  var starts=events.Select((e,i)=>(e,i)).Where(x=>x.e.Message=="ControlSystem: Starting Crestron Home").ToArray();
  if(starts.Length==0)return null;
  if(starts.Length!=1 || starts[0].e.Time>start.AddSeconds(30))
   throw new InvalidDataException("Home restarted or has an ambiguous startup epoch.");
  var current=events.Skip(starts[0].i+1).ToArray();
  var created=current.Where(e=>e.Message==driverLogPath+": Create driver instance").ToArray();
  var running=current.Where(e=>e.Message==driverLogPath+": Status changed: Running").ToArray();
  if(created.Length>1 || running.Length>1)throw new InvalidDataException("Driver reloaded or has an ambiguous load event.");
  if(created.Length==0 || running.Length==0)return null;
  if(running[0].Time<created[0].Time)throw new InvalidDataException("Driver Running precedes its creation.");
  var delta=running[0].Time-start;
  var earliest=program.EarliestStartUtc+delta-TimeSpan.FromSeconds(1);
  var latest=program.LatestStartUtc+delta+TimeSpan.FromSeconds(1);
  if(earliest<program.EarliestStartUtc || earliest>observedUtc)
   throw new InvalidDataException("Driver load is outside the verified program epoch.");
  if(latest>observedUtc)return null;
  return new(driverLogPath,earliest,latest,created[0].Raw,running[0].Raw);
 }
}
