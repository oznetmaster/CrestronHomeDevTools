// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace CrestronHomeDevTools;

public sealed record ProcessorProgramIdentity(string BootDirectory,string ApplicationName,string ProgramFile);
public sealed record ProcessorProgramUptimeSnapshot(ProcessorProgramIdentity Program,TimeSpan Uptime,
 string LocalStartedAtDiagnostic,DateTimeOffset RequestSentUtc,DateTimeOffset ObservedUtc,string RawResponse)
{
 // The observed firmware prints milliseconds. Include one full unit of quantization plus
 // request latency; local calendar text is diagnostic and never converted into the UTC clock.
 public DateTimeOffset EarliestStartUtc=>RequestSentUtc-Uptime-TimeSpan.FromMilliseconds(1);
 public DateTimeOffset LatestStartUtc=>ObservedUtc-Uptime+TimeSpan.FromMilliseconds(1);
}

/// <summary>Read the default program's PROGUPTIME over pinned SSH, with PROGCOMMENTS identity
/// checks before and after. Does not restart/reload a program or select another slot. Firmware
/// that does not provide this exact response fails without substituting processor uptime.</summary>
public static class ProcessorProgramUptime
{
 private static readonly Regex Prompt=new(@"(?:^|[\r\n])[A-Za-z0-9][A-Za-z0-9_.:-]{0,80}>",RegexOptions.CultureInvariant);
 // Firmware emits the integer millisecond component without zero padding: .47 means
 // 47 ms, not 470 ms. Confirmed in consecutive replies across a second boundary.
 private static readonly Regex Duration=new(@"^The program has been running for ([0-9]+) days ([0-9]{2}):([0-9]{2}):([0-9]{2})\.([0-9]{1,3})$",RegexOptions.CultureInvariant);
 private const string Started="The program last started on: ";
 public static async Task<ProcessorProgramUptimeSnapshot> ReadAsync(string host,NetworkCredential credential,string sshFingerprint,
  ProcessorProgramIdentity expectedProgram,TimeSpan timeout,CancellationToken token=default)
 {
  ArgumentException.ThrowIfNullOrWhiteSpace(host);ArgumentNullException.ThrowIfNull(credential);
  ArgumentException.ThrowIfNullOrWhiteSpace(sshFingerprint);Validate(expectedProgram,timeout);
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timeout);
  using var client=new SshClient(host,credential.UserName,credential.Password);client.ConnectionInfo.Timeout=timeout;
  client.HostKeyReceived+=(_,e)=>e.CanTrust=string.Equals(e.FingerPrintSHA256,sshFingerprint,StringComparison.Ordinal);
  await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
  using var shell=client.CreateShellStream("xterm",100,24,800,600,65536);
  return await ReadCoreAsync(new Session(client,shell),expectedProgram,timeout,deadline.Token).ConfigureAwait(false);
 }
 private static void Validate(ProcessorProgramIdentity expected,TimeSpan timeout) {
  ArgumentNullException.ThrowIfNull(expected);
  if(new[]{expected.BootDirectory,expected.ApplicationName,expected.ProgramFile}.Any(s=>string.IsNullOrWhiteSpace(s) || s.Length>256 || s.Any(char.IsControl)) ||
   timeout<=TimeSpan.Zero || timeout>TimeSpan.FromMinutes(2))throw new ArgumentException("Specify the exact expected program identity and a bounded read timeout.");
 }
 internal static async Task<ProcessorProgramUptimeSnapshot> ReadCoreAsync(IUptimeSession session,ProcessorProgramIdentity expected,
  TimeSpan timeout,CancellationToken token)
 {
  Validate(expected,timeout);using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(timeout);
  async Task<string> ReadPrompt() {
   var buffer=new StringBuilder();
   while(true) {
    deadline.Token.ThrowIfCancellationRequested();if(!session.IsConnected)throw new IOException("Program information connection ended before verification.");
    buffer.Append(session.ReadAvailable());if(buffer.Length>65536)throw new InvalidDataException("Program information response exceeded its bound.");
    string text=buffer.ToString();if(Prompt.IsMatch(text))return text;
    await Task.Delay(25,deadline.Token).ConfigureAwait(false);
   }
  }
  await ReadPrompt().ConfigureAwait(false); // discard greeting and all pre-request output
  deadline.Token.ThrowIfCancellationRequested();session.WriteLine("progcomments");
  var before=ParseIdentity(await ReadPrompt().ConfigureAwait(false));
  if(before!=expected)throw new InvalidDataException("Default program identity differs from the approved program.");
  deadline.Token.ThrowIfCancellationRequested();var sent=DateTimeOffset.UtcNow;session.WriteLine("proguptime");
  string raw=await ReadPrompt().ConfigureAwait(false);var observed=DateTimeOffset.UtcNow;
  var result=ParseUptime(before,raw,sent,observed);
  deadline.Token.ThrowIfCancellationRequested();session.WriteLine("progcomments");
  if(ParseIdentity(await ReadPrompt().ConfigureAwait(false))!=before)throw new InvalidDataException("Default program identity changed during the uptime query.");
  return result;
 }
 internal static ProcessorProgramIdentity ParseIdentity(string response) {
  string Value(string label) {
   var pattern=new Regex("^"+Regex.Escape(label)+@"\s*:\s*(.+?)\s*$",RegexOptions.CultureInvariant);
   var values=response.Split('\n')[..^1].Select(s=>pattern.Match(s.Trim())).Where(m=>m.Success).Select(m=>m.Groups[1].Value).ToArray();
   if(values.Length!=1 || values[0].Length>256)throw new InvalidDataException("Program identity is missing or ambiguous.");return values[0];
  }
  return new(Value("Program Boot Directory"),Value("Application Name"),Value("Program File"));
 }
 internal static ProcessorProgramUptimeSnapshot ParseUptime(ProcessorProgramIdentity identity,string response,DateTimeOffset sent,DateTimeOffset observed) {
  if(sent==default || observed<sent)throw new InvalidDataException("Invalid program-uptime observation clock.");
  var lines=response.Split('\n')[..^1].Select(s=>s.Trim()).ToArray();
  var durations=lines.Where(l=>l.StartsWith("The program has been running for ",StringComparison.Ordinal)).ToArray();
  var starts=lines.Where(l=>l.StartsWith(Started,StringComparison.Ordinal)).ToArray();
  if(durations.Length!=1 || starts.Length!=1 || starts[0].Length<=Started.Length || starts[0].Length>256)
   throw new InvalidDataException("Program uptime response is missing or ambiguous.");
  var match=Duration.Match(durations[0]);
  if(!match.Success || !int.TryParse(match.Groups[1].Value,CultureInfo.InvariantCulture,out int days) || days>100000)
   throw new InvalidDataException("Unrecognized program uptime duration.");
  int hours=int.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture),minutes=int.Parse(match.Groups[3].Value,CultureInfo.InvariantCulture),
   seconds=int.Parse(match.Groups[4].Value,CultureInfo.InvariantCulture),milliseconds=int.Parse(match.Groups[5].Value,CultureInfo.InvariantCulture);
  if(hours>23 || minutes>59 || seconds>59)throw new InvalidDataException("Invalid program uptime duration.");
  var uptime=new TimeSpan(days,hours,minutes,seconds,milliseconds);
  if(uptime+TimeSpan.FromMilliseconds(1)>sent-DateTimeOffset.MinValue)throw new InvalidDataException("Program uptime lies outside the observation calendar.");
  return new(identity,uptime,starts[0][Started.Length..],sent,observed,response);
 }
 private sealed class Session(SshClient client,ShellStream shell):IUptimeSession {
  public bool IsConnected=>client.IsConnected;
  public string ReadAvailable()=>shell.DataAvailable?shell.Read():"";
  public void WriteLine(string command)=>shell.WriteLine(command);
 }
}
