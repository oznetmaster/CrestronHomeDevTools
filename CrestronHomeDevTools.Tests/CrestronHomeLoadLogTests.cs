using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class CrestronHomeLoadLogTests
{
 private static readonly DateTimeOffset Epoch=new(2026,9,30,12,57,13,TimeSpan.Zero);
 private static readonly ProcessorProgramUptimeSnapshot Program=new(new("/simpl/app00","Crestron.Seawolf","Crestron.Seawolf.dll"),
  TimeSpan.FromSeconds(300),"Wednesday, 30 September 2026 at 13:57:13",Epoch.AddSeconds(300),Epoch.AddMilliseconds(300050),"synthetic");
 private static string Line(string time,string message)=>$"L:01 [{time}]: NotUserVisible|Information|System|NA|GeneralMessage|[ 22] [Main        ] [INFO]    {message}\r\n";
 private static string Start=>Line("13:57:15","ControlSystem: Starting Crestron Home");
 private static string End=>Line("13:59:01",@"System\Runner: * Loaded System: 106248 ms (total)");
 private static CrestronHomeLoadObservation? Parse(string text,ProcessorProgramUptimeSnapshot? program=null)=>
  CrestronHomeLoadLog.Parse(text,new(2026,9,30),program??Program,Epoch.AddSeconds(301));
 [Test] public void AnchorsGlobalLoadToUptimeWithQuantizationBounds() {
  var result=Parse(Start+End)!;
  Assert.That(result.EarliestLoadedUtc,Is.EqualTo(Epoch.AddSeconds(107).AddMilliseconds(-1)));
  Assert.That(result.LatestLoadedUtc,Is.EqualTo(Epoch.AddMilliseconds(109051)));
  Assert.That(result.LoadMilliseconds,Is.EqualTo(106248));
 }
 [Test] public void MissingCompletionDoesNotInferItFromOtherReadyMessages()=>Assert.That(Parse(Start+
  Line("13:58:59",@"Devices\Subsystem: Subsystem fully ready")),Is.Null);
 [Test] public void DriverLogCannotImpersonateGlobalLoad()=>Assert.That(Parse(Start+
  Line("13:59:01",@"Drivers\HostManager: System\Runner: * Loaded System: 106248 ms (total)")),Is.Null);
 [Test] public void OldBootAndOldCompletionAreIgnored()=>Assert.That(Parse(
  Line("07:54:20","ControlSystem: Starting Crestron Home")+
  Line("07:56:06",@"System\Runner: * Loaded System: 106248 ms (total)")),Is.Null);
 [Test] public void PriorBootDoesNotPreventUniqueCurrentBoot()=>Assert.That(Parse(
  Line("07:54:20","ControlSystem: Starting Crestron Home")+
  Line("07:56:06",@"System\Runner: * Loaded System: 106248 ms (total)")+Start+End),Is.Not.Null);
 [TestCase("duplicate-start")][TestCase("duplicate-load")][TestCase("later-boot")][TestCase("clock-jump")][TestCase("invalid-time")]
 public void AmbiguityAndClockDiscontinuityFailClosed(string kind) {
  var text=kind switch {
   "duplicate-start"=>Start+Start+End,
   "duplicate-load"=>Start+End+End,
   "later-boot"=>Start+End+Line("14:00:00","ControlSystem: Starting Crestron Home"),
   "clock-jump"=>Start+Line("14:00:01",@"System\Runner: * Loaded System: 106248 ms (total)"),
   _=>Start+Line("25:59:01",@"System\Runner: * Loaded System: 106248 ms (total)")};
  Assert.Throws<InvalidDataException>(()=>Parse(text));
 }
 [Test] public void FutureEventFailsClosed()=>Assert.Throws<InvalidDataException>(()=>
  CrestronHomeLoadLog.Parse(Start+End,new(2026,9,30),Program with {Uptime=TimeSpan.FromSeconds(30),RequestSentUtc=Epoch.AddSeconds(30),ObservedUtc=Epoch.AddMilliseconds(30050)},Epoch.AddSeconds(31)));
 [Test] public void FreshLoadWaitsForUncertaintyWindowWithoutMovingRecoveryAnchor() {
  var program=Program with {Uptime=TimeSpan.FromSeconds(30),RequestSentUtc=Epoch.AddSeconds(30),ObservedUtc=Epoch.AddMilliseconds(30050)};
  Assert.That(CrestronHomeLoadLog.Parse(Start+End,new(2026,9,30),program,Epoch.AddSeconds(108)),Is.Null);
  var later=CrestronHomeLoadLog.Parse(Start+End,new(2026,9,30),program,Epoch.AddSeconds(110))!;
  Assert.That(later.EarliestLoadedUtc,Is.EqualTo(Epoch.AddSeconds(107).AddMilliseconds(-1)));
  Assert.That(later.LatestLoadedUtc,Is.EqualTo(Epoch.AddMilliseconds(109051)));
 }
 [Test] public void OctoberStartupTraceAtFirstLiveReadWaitsRatherThanFailing() {
  var program=new ProcessorProgramUptimeSnapshot(Program.Program,TimeSpan.FromMilliseconds(32596),
   "Friday, 02 October 2026 at 11:52:49",DateTimeOffset.Parse("2026-10-02T10:53:23.0728494Z"),
   DateTimeOffset.Parse("2026-10-02T10:53:23.1337093Z"),"retained timing replay");
  var log=Line("11:52:51","ControlSystem: Starting Crestron Home")+
   Line("11:54:38",@"System\Runner: * Loaded System: 106757 ms (total)");
  Assert.That(CrestronHomeLoadLog.Parse(log,new(2026,10,2),program,DateTimeOffset.Parse("2026-10-02T10:54:39Z")),Is.Null);
  var result=CrestronHomeLoadLog.Parse(log,new(2026,10,2),program,DateTimeOffset.Parse("2026-10-02T10:54:41Z"))!;
  Assert.That(result.LatestLoadedUtc,Is.EqualTo(DateTimeOffset.Parse("2026-10-02T10:54:40.5387093Z")));
 }
 [Test] public void DifferentProgramFailsClosed()=>Assert.Throws<InvalidDataException>(()=>Parse(Start+End,
  Program with {Program=new("/simpl/app01","Other","Other.dll")}));
 [Test] public void WrongDateFailsClosed()=>Assert.Throws<InvalidDataException>(()=>
  CrestronHomeLoadLog.Parse(Start+End,new(2026,10,1),Program,Epoch.AddSeconds(301)));
 [Test] public void MidnightUsesBothDatedLogs() {
  var epoch=new DateTimeOffset(2026,9,30,22,59,13,TimeSpan.Zero);
  var program=Program with {LocalStartedAtDiagnostic="Wednesday, 30 September 2026 at 23:59:13",
   RequestSentUtc=epoch.AddSeconds(300),ObservedUtc=epoch.AddMilliseconds(300050)};
  var logs=new Dictionary<DateOnly,string> {
   {new(2026,9,30),Line("23:59:15","ControlSystem: Starting Crestron Home")},
   {new(2026,10,1),Line("00:01:01",@"System\Runner: * Loaded System: 106248 ms (total)")}};
  var result=CrestronHomeLoadLog.Parse(logs,program,epoch.AddSeconds(301))!;
  Assert.That(result.EarliestLoadedUtc,Is.EqualTo(epoch.AddSeconds(107).AddMilliseconds(-1)));
 }
}
