// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace CrestronHomeDevTools;

/// <summary>Bind startup prerequisites before a test. Configuration items are IDs, never secret values.</summary>
public sealed record CrestronDriverInitializationBinding(string DriverLogPath,string ConfigurationLogPath,string[] RequiredConfigurationItems);
/// <summary>Conservative initialization opportunity: Home loaded, instance created, and Home dispatched
/// saved configuration. Dispatch may precede receipt, so this must never establish a late-recovery failure.</summary>
public sealed record CrestronDriverInitializationObservation(DateTimeOffset EarliestOpportunityUtc,
 DateTimeOffset LatestOpportunityUtc,CrestronHomeLoadObservation HomeLoaded,string CreatedLine,string ConfigurationDispatchLine,
 bool DispatchMayPrecedeReceipt=true);
public sealed record CrestronDriverInitializationEvidence(CrestronDriverInitializationObservation Observation,
 CrestronDriverInitializationBinding Binding,ProcessorProgramUptimeSnapshot Program,
 IReadOnlyDictionary<string,string> Logs,DateTimeOffset ObservedUtc);

/// <summary>Observes Home's startup/configuration handoff, never the driver's Ready, Online,
/// Running or successful-command events. Does not change a submission plan or certify functionality.</summary>
public static class CrestronDriverInitializationLog
{
 public static async Task<CrestronDriverInitializationEvidence[]> ReadManyAsync(string host,NetworkCredential credential,
  string fingerprint,ProcessorProgramUptimeSnapshot program,IReadOnlyList<CrestronDriverInitializationBinding> bindings,
  TimeSpan timeout,CancellationToken token=default)
 {
  if(bindings is not {Count:>0 and <=128})throw new ArgumentException("Bind the expected startup instances.");
  foreach(var binding in bindings)Validate(binding);
  if(bindings.Select(b=>b.DriverLogPath).Distinct(StringComparer.Ordinal).Count()!=bindings.Count)
   throw new InvalidDataException("Duplicate startup instance binding.");
  var pinned=bindings.Select(b=>b with{RequiredConfigurationItems=[..b.RequiredConfigurationItems]}).ToArray();
  var result=await CrestronHomeLoadLog.ReadObservationAsync(host,credential,fingerprint,program,timeout,
   (logs,now)=>{
    var observations=pinned.Select(b=>Parse(logs,program,now,b)).ToArray();
    return observations.Any(o=>o==null)?null:observations.Select(o=>o!).ToArray();
   },token).ConfigureAwait(false);
  return pinned.Select((b,i)=>new CrestronDriverInitializationEvidence(result.Observation[i],b,program,result.Logs,result.ObservedUtc)).ToArray();
 }
 private static readonly Regex Line=new(@"^L:[0-9]+ \[(?<time>[0-9]{2}:[0-9]{2}:[0-9]{2})\]: NotUserVisible\|Information\|System\|NA\|GeneralMessage\|\[\s*[0-9]+\] \[[^\]]+\] \[INFO\]\s+(?<message>.*)$",RegexOptions.CultureInvariant);
 public static async Task<CrestronDriverInitializationEvidence> ReadAsync(string host,NetworkCredential credential,
  string fingerprint,ProcessorProgramUptimeSnapshot program,CrestronDriverInitializationBinding binding,
  TimeSpan timeout,CancellationToken token=default)
 {
  Validate(binding);
  var value=await CrestronHomeLoadLog.ReadObservationAsync(host,credential,fingerprint,program,timeout,
   (logs,now)=>Parse(logs,program,now,binding),token).ConfigureAwait(false);
  return new(value.Observation,binding,program,value.Logs,value.ObservedUtc);
 }
 private static void Validate(CrestronDriverInitializationBinding binding)
 {
  ArgumentNullException.ThrowIfNull(binding);
  static bool Path(string? value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=1024&&!value.Any(char.IsControl);
  if(!Path(binding.DriverLogPath)||!Regex.IsMatch(binding.DriverLogPath,@"^Drivers\\[^:\r\n]+\\[0-9]+$",RegexOptions.CultureInvariant)||
   !Path(binding.ConfigurationLogPath)||!binding.ConfigurationLogPath.StartsWith(@"Devices\Adapters\UniversalDeviceWrapper\",StringComparison.Ordinal)||
   binding.ConfigurationLogPath.Contains(':')||binding.RequiredConfigurationItems is not {Length:>0 and <=128}||
   binding.RequiredConfigurationItems.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>128||x.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))||
   binding.RequiredConfigurationItems.Distinct(StringComparer.Ordinal).Count()!=binding.RequiredConfigurationItems.Length)
   throw new InvalidDataException("Bind an exact driver, configuration wrapper and required saved-item IDs.");
  var id=binding.DriverLogPath[(binding.DriverLogPath.LastIndexOf('\\')+1)..];
  if(!binding.ConfigurationLogPath.Contains("\\"+id+"|",StringComparison.Ordinal))
   throw new InvalidDataException("Configuration wrapper must identify the same driver instance.");
 }
 public static CrestronDriverInitializationObservation? Parse(IReadOnlyDictionary<DateOnly,string> logs,
  ProcessorProgramUptimeSnapshot program,DateTimeOffset observedUtc,CrestronDriverInitializationBinding binding)
 {
  Validate(binding);
  var home=CrestronHomeLoadLog.Parse(logs,program,observedUtc);
  if(home==null)return null;
  var start=CrestronHomeLoadLog.LocalProgramStart(program);
  var entries=new List<(DateTime Time,string Message,string Raw)>();
  foreach(var (date,text) in logs.OrderBy(x=>x.Key))
  foreach(var raw in text.Split('\n')) {
   var line=raw.TrimEnd('\r');var match=Line.Match(line);if(!match.Success)continue;
   if(!TimeOnly.TryParseExact(match.Groups["time"].Value,"HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time))
    throw new InvalidDataException("Invalid startup log timestamp.");
   var timestamp=date.ToDateTime(time);if(timestamp<start)continue;
   entries.Add((timestamp,match.Groups["message"].Value,line));
  }
  var created=entries.Where(x=>x.Message==binding.DriverLogPath+": Create driver instance").ToArray();
  var prefix=binding.ConfigurationLogPath+": Apply configuration items: ";
  var configured=entries.Where(x=>x.Message.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
  if(created.Length>1 || configured.Length>1)throw new InvalidDataException("Reload or reconfiguration makes the startup handoff ambiguous.");
  if(created.Length==0 || configured.Length==0)return null;
  var items=configured[0].Message[prefix.Length..].Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
  if(!binding.RequiredConfigurationItems.All(x=>items.Contains(x,StringComparer.Ordinal)))return null;
  if(configured[0].Time<created[0].Time)throw new InvalidDataException("Configuration precedes driver creation.");
  var delta=configured[0].Time-start;
  var earliest=program.EarliestStartUtc+delta-TimeSpan.FromSeconds(1);
  var latest=program.LatestStartUtc+delta+TimeSpan.FromSeconds(1);
  if(earliest>observedUtc)throw new InvalidDataException("Configuration event is outside the observation.");
  if(latest>observedUtc)return null;
  return new(earliest>home.EarliestLoadedUtc?earliest:home.EarliestLoadedUtc,
   latest>home.LatestLoadedUtc?latest:home.LatestLoadedUtc,home,created[0].Raw,configured[0].Raw);
 }
}
