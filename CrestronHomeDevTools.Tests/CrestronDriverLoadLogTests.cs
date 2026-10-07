using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class CrestronDriverLoadLogTests
{
 private static readonly DateTimeOffset Epoch=new(2026,10,3,13,44,49,TimeSpan.Zero);
 private static readonly ProcessorProgramUptimeSnapshot Program=new(new("/simpl/app00","Crestron.Seawolf","Crestron.Seawolf.dll"),
  TimeSpan.FromSeconds(300),"Saturday, 03 October 2026 at 14:44:49",Epoch.AddSeconds(300),Epoch.AddMilliseconds(300050),"synthetic");
 private const string Root=@"Drivers\KasaTapoPlatform\52677", Child=Root+@"\KP115(UK)\52797";
 private static string Line(string time,string message)=>$"L:01 [{time}]: NotUserVisible|Information|System|NA|GeneralMessage|[ 22] [Main ] [INFO]    {message}\n";
 private static string Home=>Line("14:44:51","ControlSystem: Starting Crestron Home")+
  Line("14:46:37",@"System\Runner: * Loaded System: 106059 ms (total)");
 private static string Events(string path,string create,string run)=>Line(create,path+": Create driver instance")+Line(run,path+": Status changed: Running");
 private static CrestronDriverLoadObservation? Parse(string text,string path=Root)=>CrestronDriverLoadLog.Parse(
  new Dictionary<DateOnly,string>{{new(2026,10,3),text}},Program,Epoch.AddSeconds(301),path);
 [Test] public void RootAndChildUseTheirOwnRunningEventsNotGlobalHomeLoad() {
  var text=Home+Events(Root,"14:45:35","14:45:46")+Events(Child,"14:46:38","14:46:39");
  var root=Parse(text)!;var child=Parse(text,Child)!;
  Assert.That(root.EarliestLoadedUtc,Is.EqualTo(Epoch.AddSeconds(56).AddMilliseconds(-1)));
  Assert.That(child.EarliestLoadedUtc-root.EarliestLoadedUtc,Is.EqualTo(TimeSpan.FromSeconds(53)));
  Assert.That(root.RunningLine,Does.Contain("14:45:46"));
 }
 [Test] public void ReadinessAloneNeverSubstitutesForLoad()=>Assert.That(Parse(Home+
  Line("14:45:35",Root+": Create driver instance")+
  Line("14:46:00",Root+@"\Capabilities\ReadyIndicator: Device is ready.")),Is.Null);
 [Test] public void DifferentDriverCannotSupplyTheMissingEvent()=>Assert.That(Parse(Home+Events(Child,"14:46:38","14:46:39")),Is.Null);
 [Test] public void OldEpochCannotSupplyTheMissingEvent()=>Assert.That(Parse(Home+Events(Root,"11:45:35","11:45:46")),Is.Null);
 [Test] public void NestedLoggerTextCannotImpersonateLifecycleEvent()=>Assert.That(Parse(Home+
  Events(@"Drivers\HostManager\LoggerManager: "+Root,"14:45:35","14:45:46")),Is.Null);
 [Test] public void ReloadIsRejected()=>Assert.Throws<InvalidDataException>(()=>Parse(Home+
  Events(Root,"14:45:35","14:45:46")+Events(Root,"14:47:00","14:47:01")));
 [Test] public void RunningBeforeCreationIsRejected()=>Assert.Throws<InvalidDataException>(()=>Parse(Home+Events(Root,"14:45:46","14:45:35")));
 [Test] public void MissingHomeEpochNeverGuessesDriverClock()=>Assert.That(Parse(Events(Root,"14:45:35","14:45:46")),Is.Null);
 [Test] public void MissingCreationNeverGuessesDriverIdentity()=>Assert.That(Parse(Home+Line("14:45:46",Root+": Status changed: Running")),Is.Null);
 [Test] public void InvalidBindingIsRejected()=>Assert.Throws<ArgumentException>(()=>Parse(Home,"52677"));
 [Test] public void DriverLoadDoesNotWaitForHomeToFinishLoading() {
  var text=Line("14:44:51","ControlSystem: Starting Crestron Home")+Events(Root,"14:45:35","14:45:46");
  Assert.That(Parse(text)!.RunningLine,Does.Contain("14:45:46"));
 }
 [Test] public void SubsequentHomeRestartInvalidatesTheEpoch()=>Assert.Throws<InvalidDataException>(()=>Parse(
  Home+Events(Root,"14:45:35","14:45:46")+Line("14:47:00","ControlSystem: Starting Crestron Home")));
 [Test] public void WrongLogDateCannotBindToProgram()=>Assert.Throws<InvalidDataException>(()=>
  CrestronDriverLoadLog.Parse(new Dictionary<DateOnly,string>{{new(2026,10,4),Home}},Program,Epoch.AddSeconds(301),Root));
 [Test] public void FutureEventCannotAdvanceObservation()=>Assert.Throws<InvalidDataException>(()=>Parse(Home+Events(Root,"14:55:35","14:55:46")));
 [Test] public void MidnightKeepsDatedEventsAndUptimeBounds() {
  var program=Program with {LocalStartedAtDiagnostic="Saturday, 03 October 2026 at 23:59:49"};
  var logs=new Dictionary<DateOnly,string>{
   {new(2026,10,3),Line("23:59:51","ControlSystem: Starting Crestron Home")+Line("23:59:59",Root+": Create driver instance")},
   {new(2026,10,4),Line("00:00:03",Root+": Status changed: Running")+Line("00:01:37",@"System\Runner: * Loaded System: 106059 ms (total)")}};
  var value=CrestronDriverLoadLog.Parse(logs,program,Epoch.AddSeconds(301),Root)!;
  Assert.That(value.EarliestLoadedUtc,Is.EqualTo(Epoch.AddSeconds(13).AddMilliseconds(-1)));
 }
}
