// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;
using NUnit.Framework;

namespace WeatherLinkAndroidTests;

[TestFixture,NonParallelizable]
public sealed class WeatherPagesTests
{
 private static readonly string[] CurrentProperties=["currentTemperatureDisplay","humiditySummary","pressureSummary",
  "windSummary","windDirectionSummary","windGustSummary","rainRateSummary","rainLast24HoursSummary","rainChanceSummary",
  "sourceSummary","sourceDetailSummary","tileStatus"];
 private static readonly string[] ForecastProperties=["forecastSummary",..Enumerable.Range(1,7).SelectMany(i=>new[]{"forecastDay"+i+"Title","forecastDay"+i}),
  "forecastUpdatedSummary","forecastAttributionLine1","forecastAttributionLine2"];

 [Test]
 public async Task HomeCurrentConditionsForecastActionAndRestoration() {
  if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AndroidWorkflowSession.CONTEXT_VARIABLE)))
   Assert.Ignore("Opt-in hardware fixture: invoke through InstalledAppTests in the public automation controller.");
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Protected processor bindings require Windows.");
  using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(12));var token=deadline.Token;
  var session=await AndroidWorkflowSession.OpenFromEnvironmentAsync(token);
  bool restored=false;ObservedNavigation? navigation=null;string[]? titles=null;
  void Home(AndroidHierarchy h)=>CrestronHomePages.RequireHome(h,session.Context.Profile.ExpectedHomeText);
  try {
   var settings=FixtureSettings.Read(session.Context);
   var binding=DevToolsCredentialBindings.Read(settings.CredentialBindings).Resolve(DevToolsCredentialPurpose.Processor,settings.ProcessorHost);
   if(string.IsNullOrWhiteSpace(binding.CertificateSha256))throw new InvalidDataException("Pinned HTTPS certificate required.");
   await using var api=await ConfigurationClient.ConnectAsync(new(){Host=settings.ProcessorHost,CertificateSha256=binding.CertificateSha256},
    new NetworkCredential(binding.UserName,binding.Password),token);
   async Task<Dictionary<string,string>> Read(string[] keys) {
    var d=await api.GetDeviceAsync(settings.DeviceId,token)??throw new InvalidDataException("Selected WeatherLink instance missing.");
    return keys.ToDictionary(k=>k,k=>d.PropertyValues.TryGetValue(k,out var v)&&v.ValueKind==JsonValueKind.String&&(k=="sourceDetailSummary"||!string.IsNullOrWhiteSpace(v.GetString()))
     ?v.GetString()!:throw new InvalidDataException("Required display property unavailable: "+k),StringComparer.Ordinal);
   }
   var pageTitles=await Read(["currentConditionsTitle","weeklyForecastTitle"]);
   titles=["Current Conditions for "+pageTitles["currentConditionsTitle"],"Weekly Forecast for "+pageTitles["weeklyForecastTitle"]];
   var p=session.Context.Profile;
   navigation=new(session.Device,new AdbCommandTransport(p.AdbExecutable,p.DeviceSerial,TimeSpan.FromSeconds(25)),()=>AndroidWorkflowSession.VerifyContext(session.Context));
   void Root(AndroidHierarchy h)=>CrestronHomeExtensionPages.RequirePage(h,[titles[0]]);
   void Forecast(AndroidHierarchy h)=>CrestronHomeExtensionPages.RequirePage(h,titles);
   var tile=new AndroidSelector(AndroidSelectorKind.Text,settings.TileName){AncestorResourceId=CrestronHomePages.ResourcePrefix+"fragmentHomeContainer"};
   var started=DateTimeOffset.UtcNow;
   await session.CaptureAsync("weather-home-before",h=>{Home(h);if(h.RequireUnique(tile).ResourceId!=CrestronHomePages.ResourcePrefix+"titleSubtitle_title")throw new InvalidOperationException("Expected Home tile missing.");},token);
   var original=DateTimeOffset.UtcNow;var action=DateTimeOffset.UtcNow;
   await navigation.TapAsync(h=>h.RequireUnique(tile),Home,Root,token);
   await InspectAll("weather-current",[titles[0]],CurrentProperties,Read,session,navigation,token);
   // Start at the current scroll position, finding the actual button within a bounded number of views.
   var button=new AndroidSelector(AndroidSelectorKind.Text,"Next Week Forecast");
   // Inspection finishes below the button. Return to Home and reopen for a deterministic top viewport.
   await CloseCurrent(session,navigation,[titles[0]],Home,token);
   await navigation.TapAsync(h=>h.RequireUnique(tile),Home,Root,token);
   for(int i=0;;i++) {
    var h=await session.Device.CaptureAsync(token);var scoped=CrestronHomeExtensionPages.RequirePage(h,[titles[0]]);
    if(Texts(scoped).Contains(button.Value))break;
    if(i>=6)throw new InvalidOperationException("Forecast navigation button was not visible.");
    await navigation.ScrollAsync(h=>CrestronHomeExtensionPages.RequirePage(h,[titles[0]]).RequireUnique(CrestronHomePages.Resource("customdevices_componentRecyclerView")),Root,token);
   }
   await session.CaptureAsync("weather-forecast-trigger",h=>CrestronHomeExtensionPages.RequirePage(h,[titles[0]]).RequireUnique(button),token);
   await navigation.TapAsync(h=>CrestronHomeExtensionPages.RequirePage(h,[titles[0]]).RequireUnique(button),Root,Forecast,token);
   await InspectAll("weather-forecast",titles,ForecastProperties,Read,session,navigation,token);
   await CloseCurrent(session,navigation,titles,Root,token);
   await session.CaptureAsync("weather-current-restored",Root,token);
   await CloseCurrent(session,navigation,[titles[0]],Home,token);
   var restoredAt=DateTimeOffset.UtcNow;
   await session.CaptureAsync("weather-home-restored",Home,token);restored=true;
   var finished=DateTimeOffset.UtcNow;
   var files=Directory.EnumerateFiles(session.Context.EvidenceDirectory,"*",SearchOption.AllDirectories)
    .Where(f=>Path.GetFileName(Path.GetDirectoryName(f)!).StartsWith("weather-",StringComparison.Ordinal))
    .Select(f=>new SubmissionEvidenceFile("installed-app/AndroidUI/"+Path.GetRelativePath(session.Context.EvidenceDirectory,f).Replace('\\','/'),Hash(f))).ToArray();
   var restoration=new SubmissionRestorationObservation(original,action,restoredAt,finished,true,
    "installed-app/AndroidUI/weather-home-before/observation.json","installed-app/AndroidUI/weather-home-restored/observation.json");
   // Only these navigation/page requirements are asserted. Outage, changed-setting feedback,
   // configuration, power, multiple instances and endurance need their own producers.
   var requirements=new List<(string Id,string Target)> {
    ("extension.views.03.navigation.weather.tile","weather/tile"),
    ("extension.views.04.close.weather.layout.CurrentConditionsPage","weather/layout/CurrentConditionsPage"),
    ("extension.views.04.close.weather.layout.WeeklyForecastPage","weather/layout/WeeklyForecastPage"),
    ("extension.controls.07.forecast-button.weather.CurrentConditionsPage.ForecastNavigationButton","weather/CurrentConditionsPage/ForecastNavigationButton"),
    ("extension.controls.18.forecast-page.weather.layout.WeeklyForecastPage","weather/layout/WeeklyForecastPage")};
   foreach(string part in (string[])["ForecastSummary",..Enumerable.Range(1,7).Select(i=>"ForecastDay"+i),"ForecastUpdated","ForecastAttribution"])
    requirements.Add(("extension.controls.18.forecast-page.weather.WeeklyForecastPage."+part,"weather/WeeklyForecastPage/"+part));
   var observations=requirements.Select(r=>new SubmissionObservation(r.Id,settings.Identity,SubmissionEvidenceOutcome.Passed,started,finished,files,
    "Observed the Home tile, current and forecast display values, forecast navigation action, and both close transitions. API display values are correlated with bounded UI captures; no weather-service accuracy claim.",
    new SubmissionExecutionObservation(r.Target,"android",Restoration:restoration))).ToArray();
   using var output=new FileStream(Path.Combine(session.Context.EvidenceDirectory,"weather-observations.json"),FileMode.CreateNew);
   JsonSerializer.Serialize(output,new SubmissionEvidenceDocument(1,observations),FixtureSettings.Json);
  } finally {
   try {
    if(!restored) {
     using var cleanup=new CancellationTokenSource(TimeSpan.FromMinutes(2));
     if(navigation!=null)await navigation.ResolveAsync(cleanup.Token);
     for(int i=0;i<3;i++) {
      var h=await session.Device.CaptureAsync(cleanup.Token);
      try {Home(h);restored=true;break;}catch(InvalidOperationException) { }
      if(navigation==null||titles==null)break;
      bool nested;
      try {CrestronHomeExtensionPages.RequirePage(h,titles);nested=true;}catch(InvalidOperationException){nested=false;}
      if(nested)await CloseCurrent(session,navigation,titles,h=>CrestronHomeExtensionPages.RequirePage(h,[titles[0]]),cleanup.Token);
      else await CloseCurrent(session,navigation,[titles[0]],Home,cleanup.Token);
     }
    }
   } finally {session.Complete(restored);}
  }
 }

 private static Task CloseCurrent(AndroidWorkflowSession s,ObservedNavigation n,string[] titles,Action<AndroidHierarchy> after,CancellationToken t)=>
  n.TapAsync(h=>CrestronHomeExtensionPages.RequirePage(h,titles).RequireUnique(CrestronHomePages.Resource("customdevices_toolbarClose")),
   h=>CrestronHomeExtensionPages.RequirePage(h,titles),after,t);

 private static async Task InspectAll(string prefix,string[] titles,string[] properties,Func<string[],Task<Dictionary<string,string>>> read,
  AndroidWorkflowSession session,ObservedNavigation navigation,CancellationToken token) {
  var remaining=properties.ToHashSet(StringComparer.Ordinal);
  for(int viewport=0;viewport<16;viewport++) {
   var before=await read(properties);AndroidHierarchy? visible=null;
   string check=prefix+"-"+viewport;
   await session.CaptureAsync(check,h=>{visible=CrestronHomeExtensionPages.RequirePage(h,titles);},token);
   var after=await read(properties);
   var matched=remaining.Where(k=>DisplayMatching.Matches(visible!,k,before)||DisplayMatching.Matches(visible!,k,after)).ToArray();
   foreach(var key in matched)remaining.Remove(key);
   using(var output=new FileStream(Path.Combine(session.Context.EvidenceDirectory,check,"display-comparison.json"),FileMode.CreateNew))
    JsonSerializer.Serialize(output,new{Before=before,After=after,MatchedProperties=matched,RemainingProperties=remaining.Order().ToArray()},FixtureSettings.Json);
   if(remaining.Count==0)return;
   if(viewport==15)break;
   await navigation.ScrollAsync(h=>CrestronHomeExtensionPages.RequirePage(h,titles).RequireUnique(CrestronHomePages.Resource("customdevices_componentRecyclerView")),
    h=>CrestronHomeExtensionPages.RequirePage(h,titles),token);
  }
  throw new InvalidOperationException("UI did not show all expected display properties: "+string.Join(", ",remaining));
 }
 private static HashSet<string> Texts(AndroidHierarchy h)=>XDocument.Parse(h.MaskedXml).Descendants("node")
  .Where(n=>(string?)n.Attribute("package")=="com.crestron.phoenix.app"&&(string?)n.Attribute("password")!="true")
  .Select(n=>(string?)n.Attribute("text")??"").Where(s=>!string.IsNullOrWhiteSpace(s)).ToHashSet(StringComparer.Ordinal);
 private static string Hash(string path){using var input=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(input));}
}
