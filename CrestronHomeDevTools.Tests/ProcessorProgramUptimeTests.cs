// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ProcessorProgramUptimeTests
{
 private static readonly ProcessorProgramIdentity Identity=new("/simpl/app00","Crestron.Seawolf","Crestron.Seawolf.dll");
 private const string Comments="Program Boot Directory: /simpl/app00\r\nApplication Name          : Crestron.Seawolf\r\nProgram File              : Crestron.Seawolf.dll\r\n";
 private const string Uptime="The program has been running for 0 days 11:38:58.927 \r\nThe program last started on: Monday, 28 September 2026 at 12:03:04\r\n";
 [Test] public void UsesElapsedProgramTimeAndRequestBoundsWithoutAssumingProcessorTimezone() {
  var sent=DateTimeOffset.Parse("2026-09-28T22:42:02Z");var result=ProcessorProgramUptime.ParseUptime(Identity,Uptime,sent,sent.AddMilliseconds(150));
  Assert.That(result.Uptime,Is.EqualTo(new TimeSpan(0,11,38,58,927)));
  Assert.That(result.EarliestStartUtc,Is.EqualTo(sent-result.Uptime-TimeSpan.FromMilliseconds(1)));
  Assert.That(result.LatestStartUtc-result.EarliestStartUtc,Is.EqualTo(TimeSpan.FromMilliseconds(152)));
  Assert.That(result.LocalStartedAtDiagnostic,Does.Contain("12:03:04"));
 }
 [TestCase("0",0)][TestCase("7",7)][TestCase("47",47)][TestCase("047",47)][TestCase("927",927)]
 public void FirmwareMillisecondsAreAnIntegerComponent(string text,int milliseconds) {
  var result=ProcessorProgramUptime.ParseUptime(Identity,Uptime.Replace(".927","."+text),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
  Assert.That(result.Uptime.Milliseconds,Is.EqualTo(milliseconds));
 }
 [Test] public void CapturedUnpaddedMillisecondSequenceRemainsMonotonic() {
  string[] values=["11:51:50.968","11:51:51.47","11:51:51.106","11:51:51.999","11:51:52.61"];
  var durations=values.Select(v=>ProcessorProgramUptime.ParseUptime(Identity,Uptime.Replace("11:38:58.927",v),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow).Uptime).ToArray();
  Assert.That(durations.Zip(durations.Skip(1),(a,b)=>(b-a).TotalMilliseconds),Is.All.InRange(59d,893d));
  Assert.That((durations[1]-durations[0]).TotalMilliseconds,Is.EqualTo(79));
  Assert.That((durations[4]-durations[3]).TotalMilliseconds,Is.EqualTo(62));
 }
 [TestCase("24:38:58.927")][TestCase("11:60:58.927")][TestCase("11:38:60.927")][TestCase("11:38:58.9270")]
 public void BadDurationIsRejected(string replacement)=>Assert.Throws<InvalidDataException>(()=>
  ProcessorProgramUptime.ParseUptime(Identity,Uptime.Replace("11:38:58.927",replacement),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));
 [Test] public void SystemUptimeCannotStandInForProgramUptime()=>Assert.Throws<InvalidDataException>(()=>
  ProcessorProgramUptime.ParseUptime(Identity,Uptime.Replace("program","system"),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));
 [Test] public void DuplicateAndTruncatedRepliesAreRejected() {
  Assert.Throws<InvalidDataException>(()=>ProcessorProgramUptime.ParseUptime(Identity,Uptime+Uptime,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));
  Assert.Throws<InvalidDataException>(()=>ProcessorProgramUptime.ParseUptime(Identity,Uptime.TrimEnd(),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));
  Assert.Throws<InvalidDataException>(()=>ProcessorProgramUptime.ParseIdentity(Comments+Comments));
 }
 [Test] public void EmbeddedLogMessagesCannotSupplyIdentityOrUptime() {
  Assert.Throws<InvalidDataException>(()=>ProcessorProgramUptime.ParseIdentity("[INFO] "+Comments.Replace("\r\n","\r\n[INFO] ")));
  Assert.Throws<InvalidDataException>(()=>ProcessorProgramUptime.ParseUptime(Identity,"[INFO] "+Uptime.Replace("\r\n","\r\n[INFO] "),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));
 }
 [Test] public async Task ChecksProgramIdentityBeforeAndAfterTheFreshUptimeRead() {
  var session=new Session();var result=await ProcessorProgramUptime.ReadCoreAsync(session,Identity,TimeSpan.FromSeconds(2),default);
  Assert.That(session.Commands,Is.EqualTo(new[]{"progcomments","proguptime","progcomments"}));
  Assert.That(result.Program,Is.EqualTo(Identity));Assert.That(result.RawResponse,Does.Contain("11:38:58.927"));
 }
 [Test] public async Task WrongDefaultProgramStopsBeforeUptime() {
  var session=new Session{First=Comments.Replace("Crestron.Seawolf","OtherProgram")};
  await Assert.ThrowsAsync<InvalidDataException>(()=>ProcessorProgramUptime.ReadCoreAsync(session,Identity,TimeSpan.FromSeconds(2),default));
  Assert.That(session.Commands,Is.EqualTo(new[]{"progcomments"}));
 }
 [Test] public async Task ProgramChangeDuringQueryCannotPass() {
  var session=new Session{Last=Comments.Replace("/simpl/app00","/simpl/app01")};
  await Assert.ThrowsAsync<InvalidDataException>(()=>ProcessorProgramUptime.ReadCoreAsync(session,Identity,TimeSpan.FromSeconds(2),default));
 }
 [Test] public async Task GreetingCannotSupplyStaleUptime() {
  var session=new Session{Greeting=Comments+Uptime+"MC4-R>",Duration="ERROR: unavailable\r\n"};
  await Assert.ThrowsAsync<InvalidDataException>(()=>ProcessorProgramUptime.ReadCoreAsync(session,Identity,TimeSpan.FromSeconds(2),default));
 }
 [Test] public async Task MissingPromptNeverSendsCommands() {
  var session=new Session{Greeting="no prompt"};
  await Assert.CatchAsync<OperationCanceledException>(()=>ProcessorProgramUptime.ReadCoreAsync(session,Identity,TimeSpan.FromMilliseconds(150),default));
  Assert.That(session.Commands,Is.Empty);
 }
 private sealed class Session:IUptimeSession {
  public string First=Comments,Last=Comments,Duration=Uptime,Greeting="MC4-R>background log\r\n";
  private readonly Queue<string> _chunks=new();private bool _greeted;
  public List<string> Commands=[];public bool IsConnected=>true;
  public string ReadAvailable() {if(!_greeted){_greeted=true;return Greeting;}return _chunks.Count>0?_chunks.Dequeue():"";}
  public void WriteLine(string command) {
   Commands.Add(command);string text=Commands.Count switch{1=>First,2=>Duration,_=>Last};
   int split=text.Length/2;_chunks.Enqueue(text[..split]);_chunks.Enqueue(text[split..]);_chunks.Enqueue("MC4-R>background log\r\n");
  }
 }
}
