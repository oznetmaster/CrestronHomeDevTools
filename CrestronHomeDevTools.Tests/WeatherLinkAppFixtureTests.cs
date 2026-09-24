// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CrestronHomeNUnit.Android;
using NUnit.Framework;
using WeatherLinkAndroidTests;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class WeatherLinkAppFixtureTests
{
 private const string App="com.crestron.phoenix.app";
 private static XElement Node(string id,string text,string bounds="[10,20][300,60]")=>new("node",
  new XAttribute("package",App),new XAttribute("resource-id",CrestronHomePages.ResourcePrefix+id),
  new XAttribute("text",text),new XAttribute("bounds",bounds),new XAttribute("enabled","true"));
 private static AndroidHierarchy Page(params XElement[] children) {
  var container=Node("customdevices_componentRecyclerView","","[0,0][500,500]");container.Add(children);
  return new(new XElement("hierarchy",container).ToString(),App);
 }
 private static XElement Row(string title,string text,string bounds="[10,70][300,110]")=>new("node",
  Node("customdevice_textdisplay_title",title),Node("customdevice_textdisplay_firstlinetext",text,bounds));
 [Test] public void TextMustBeInCorrectRowAndFullyInsideVisibleViewport() {
  var values=new Dictionary<string,string>{{"forecastDay1Title","Today"},{"forecastDay1","Light rain"}};
  Assert.That(DisplayMatching.Matches(Page(Row("Today","LIGHT RAIN")),"forecastDay1",values),Is.True);
  Assert.That(DisplayMatching.Matches(Page(Row("Tomorrow","LIGHT RAIN")),"forecastDay1",values),Is.False);
  Assert.That(DisplayMatching.Matches(Page(Row("Today","LIGHT RAIN","[10,490][300,550]")),"forecastDay1",values),Is.False);
  Assert.That(DisplayMatching.Matches(Page(Row("Today","LIGHT RAIN"),Row("Today","LIGHT RAIN")),"forecastDay1",values),Is.False);
 }
 [Test] public void EmptySourceDetailRequiresObservedSourceRow() {
  var values=new Dictionary<string,string>{{"sourceDetailSummary",""}};
  Assert.That(DisplayMatching.Matches(Page(Row("Source","LOCAL")),"sourceDetailSummary",values),Is.True);
  Assert.That(DisplayMatching.Matches(Page(Row("Rain","LOCAL")),"sourceDetailSummary",values),Is.False);
 }
 [Test] public async Task UncertainNavigationInputIsNotRepeatedByRecovery() {
  var transport=new FakeTransport();var navigation=new ObservedNavigation(new(transport,App),transport,()=>{});
  var selector=new AndroidSelector(AndroidSelectorKind.Text,"Before");
  void Before(AndroidHierarchy h)=>h.RequireUnique(selector);
  void After(AndroidHierarchy h)=>h.RequireUnique(new(AndroidSelectorKind.Text,"After"));
  Assert.ThrowsAsync<IOException>(async()=>await navigation.TapAsync(h=>h.RequireUnique(selector),Before,After,default));
  using(var deadline=new CancellationTokenSource(100))
   Assert.CatchAsync<OperationCanceledException>(async()=>await navigation.ResolveAsync(deadline.Token));
  Assert.That(transport.Taps,Is.EqualTo(1));
  transport.After=true;await navigation.ResolveAsync(default);Assert.That(transport.Taps,Is.EqualTo(1));
 }
 [Test] public void CandidateMismatchIsRejectedBeforeOpeningCredentialStore() {
  var context=new AndroidRunContext(1,"run","machine",1,1,"processor",2,Guid.NewGuid().ToString(),"1.0.0.0",new('a',64),new('b',64),
   new("unused","emulator","app","Home","unused"),"unused"){ReleaseSourceCommit=new('c',40)};
  var settings=new FixtureSettings("processor",2,"Station",Path.GetFullPath("bindings.json"),new(new('a',64),new('c',40),new('d',64),new('e',64)));
  Assert.DoesNotThrow(()=>settings.Validate(context));
  Assert.Throws<InvalidDataException>(()=>(settings with{DeviceId=3}).Validate(context));
  Assert.Throws<InvalidDataException>(()=>settings.Validate(context with{ReleaseSourceCommit=new('f',40)}));
 }
 [TestCase("nunit",0,true)][TestCase("nunit",2,true)]
 [TestCase("installed-app",2,true)][TestCase("installed-app",0,false)]
 [TestCase("nunit",3,false)][TestCase("unrelated",0,false)]
 public void DeploymentUsesCoordinatorInstanceAndExistingRouteStillRequiresExactId(string stage,int configuredId,bool succeeds) {
  string root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"app-input-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  try {
   var context=new AndroidRunContext(1,"run","machine",1,1,"processor",2,Guid.NewGuid().ToString(),"1.0.0.0",new('a',64),new('b',64),
    new("unused","emulator","app","Home","unused"),Path.Combine(root,stage,"AndroidUI")){ReleaseSourceCommit=new('c',40)};
   var settings=new FixtureSettings("processor",configuredId,"Station",Path.GetFullPath("bindings.json"),new(new('a',64),new('c',40),new('d',64),new('e',64)));
   File.WriteAllText(Path.Combine(root,"app-fixture-settings.json"),JsonSerializer.Serialize(settings,FixtureSettings.Json));
   if(succeeds)Assert.That(FixtureSettings.Read(context).DeviceId,Is.EqualTo(2));
   else Assert.Throws<InvalidDataException>(()=>FixtureSettings.Read(context));
  } finally {Directory.Delete(root,true);}
 }
 private sealed class FakeTransport:IAndroidCommandTransport {
  public int Taps;public bool After;
  public Task<byte[]> ExecuteAsync(IReadOnlyList<string> args,CancellationToken token) {
   token.ThrowIfCancellationRequested();
   if(args.Contains("tap")){Taps++;throw new IOException("Synthetic uncertain input; no retry.");}
   if(args.Contains("dump"))return Task.FromResult(Encoding.UTF8.GetBytes("UI hierarchy dumped to: test"));
   if(args.Contains("cat"))return Task.FromResult(Encoding.UTF8.GetBytes(Page(Node("title",After?"After":"Before")).MaskedXml));
   return Task.FromResult(Array.Empty<byte>());
  }
 }
}
