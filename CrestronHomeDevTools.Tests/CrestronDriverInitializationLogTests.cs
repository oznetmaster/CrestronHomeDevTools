using NUnit.Framework;
namespace CrestronHomeDevTools.Tests;
[TestFixture]
public sealed class CrestronDriverInitializationLogTests
{
 private static readonly DateTimeOffset Epoch=new(2026,10,3,13,44,49,TimeSpan.Zero);
 private static readonly ProcessorProgramUptimeSnapshot Program=new(new("/simpl/app00","Crestron.Seawolf","Crestron.Seawolf.dll"),TimeSpan.FromSeconds(300),
  "Saturday, 03 October 2026 at 14:44:49",Epoch.AddSeconds(300),Epoch.AddMilliseconds(300050),"synthetic");
 private static readonly CrestronDriverInitializationBinding Binding=new(@"Drivers\KasaTapoPlatform\52677",
  @"Devices\Adapters\UniversalDeviceWrapper\TP-Link\KasaTapoPlatform\52677|Controller",["UserName","Password"]);
 private static string Line(string time,string message)=>$"L:01 [{time}]: NotUserVisible|Information|System|NA|GeneralMessage|[ 22] [Main ] [INFO]    {message}\n";
 private static string Home=>Line("14:44:51","ControlSystem: Starting Crestron Home")+Line("14:46:37",@"System\Runner: * Loaded System: 106059 ms (total)");
 private static string Created=>Line("14:45:35",Binding.DriverLogPath+": Create driver instance");
 private static string Config(string time="14:45:42",string items="UserName, Password")=>Line(time,Binding.ConfigurationLogPath+": Apply configuration items: "+items);
 private static CrestronDriverInitializationObservation? Parse(string log,CrestronDriverInitializationBinding? binding=null)=>
  CrestronDriverInitializationLog.Parse(new Dictionary<DateOnly,string>{{new(2026,10,3),log}},Program,Epoch.AddSeconds(301),binding??Binding);
 [Test] public void WaitsForHomeWhenDriverAlreadyHasItsConfiguration() {
  var result=Parse(Home+Created+Config())!;
  Assert.That(result.EarliestOpportunityUtc,Is.EqualTo(result.HomeLoaded.EarliestLoadedUtc));
  Assert.That(result.DispatchMayPrecedeReceipt,Is.True);
 }
 [Test] public void LateHomeConfigurationDelaysOpportunityButNotUntilDriverReady() {
  var result=Parse(Home+Created+Config("14:46:50")+Line("14:48:00",Binding.DriverLogPath+@"\Capabilities\ReadyIndicator: Device is ready."))!;
  Assert.That(result.EarliestOpportunityUtc,Is.EqualTo(Epoch.AddSeconds(120).AddMilliseconds(-1)));
 }
 [Test] public void ReadyAndRunningCannotReplaceConfiguration()=>Assert.That(Parse(Home+Created+
  Line("14:46:50",Binding.DriverLogPath+": Status changed: Running")+
  Line("14:47:00",Binding.DriverLogPath+@"\Capabilities\ReadyIndicator: Device is ready.")),Is.Null);
 [Test] public void MissingRequiredSavedItemRemainsUnproven()=>Assert.That(Parse(Home+Created+Config(items:"UserName")),Is.Null);
 [Test] public void MissingHomeCompletionRemainsUnproven()=>Assert.That(Parse(Line("14:44:51","ControlSystem: Starting Crestron Home")+Created+Config()),Is.Null);
 [Test] public void AnotherInstanceCannotSupplyConfiguration()=>Assert.That(Parse(Home+Created+Config().Replace("52677|","52797|")),Is.Null);
 [Test] public void DriverCreationIsRequired()=>Assert.That(Parse(Home+Config()),Is.Null);
 [Test] public void ReconfigurationCannotMoveTheClockLater()=>Assert.Throws<InvalidDataException>(()=>Parse(Home+Created+Config()+Config("14:47:00")));
 [Test] public void ReturnOrCompletionDoesNotReplaceDispatch()=>Assert.That(Parse(Home+Created+Config().Replace("Apply configuration items:","Configuration completed:")),Is.Null);
 [Test] public void MismatchedBindingRejected()=>Assert.Throws<InvalidDataException>(()=>Parse(Home,Binding with{ConfigurationLogPath=Binding.ConfigurationLogPath.Replace("52677|","52797|")}));
 [Test] public void OldConfigurationCannotSupplyCurrentBoot()=>Assert.That(Parse(Home+Created+Config("11:45:42")),Is.Null);
 [Test] public void ConfigurationBeforeCreationRejected()=>Assert.Throws<InvalidDataException>(()=>Parse(Home+Created+Config("14:45:00")));
}
