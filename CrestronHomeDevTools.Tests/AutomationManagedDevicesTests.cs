// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Android;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class AutomationManagedDevicesTests
{
 private string root=null!;
 private AutomationManagedDevices.Operation operation=null!;
 private SubmissionWorkflowStepContext context=null!;
 private int calls;
 [SetUp]public void Setup() {
  root=Path.Combine(TestContext.CurrentContext.WorkDirectory,"managed-automation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  operation=new("processor.example",new('a',64),"pin",new(71,"Platform","1.0.0.0","Installed"),new([
   new("sensor","physical-sensor","Demo Sensor","Sensor",6,new(){["ActivationMarker"]="true"},["extension:doCommand"]),
   new("light","physical-light","Demo Light","Lamp",6,[],["lightDimmer:setLevel"])]));
  var release=new SubmissionWorkflowRelease("example/driver",1,"v1",new('a',40),new('b',64),new('c',64),new('d',64));
  context=new(root,new(1,new('e',64),release,SubmissionWorkflowStage.AppTests,SubmissionWorkflowStatus.Running,"operation-1",null,[],DateTimeOffset.UtcNow));calls=0;
 }
 [TearDown]public void Cleanup()=>Directory.Delete(root,true);
 private SubmissionManagedChildBinding[] Bindings()=>operation.Plan.Children.Select((c,i)=>new SubmissionManagedChildBinding(c.Alias,
  new(operation.Parent.DeviceId,operation.Parent.Model,operation.Parent.Version,c.ManagedDeviceId,c.Name,c.Model,c.LocationId),101+i,i==1?103:null)).ToArray();
 private Task<AutomationManagedDevices.Outcome> Producer(AutomationManagedDevices.Operation p,NetworkCredential cred,string path,CancellationToken t) {
  calls++;Directory.CreateDirectory(path);var result=new AutomationManagedDevices.Outcome(true,true,Bindings());
  AutomationFiles.Write(Path.Combine(path,"result.json"),result);File.WriteAllText(Path.Combine(path,"raw.json"),"synthetic producer evidence");return Task.FromResult(result);
 }
 private Task<SubmissionWorkflowStepResult> Advance(Func<AutomationManagedDevices.Operation,NetworkCredential,string,CancellationToken,Task<AutomationManagedDevices.Outcome>>? run=null)=>
  AutomationManagedDevices.AdvanceCore(context,operation,_=>new(),run??Producer,default);

 [Test]public async Task CompletedSetupIsRetainedWithoutRepeatingCreation() {
  var result=await Advance();Assert.That(result.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That((await Advance()).Receipt,Is.EqualTo(result.Receipt));Assert.That(calls,Is.EqualTo(1));
  Assert.That(AutomationManagedDevices.VerifyRetained(context),Is.EqualTo(Bindings()));
  File.AppendAllText(Path.Combine(root,"managed-devices","operation","raw.json"),"tampered");
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Advance());Assert.That(calls,Is.EqualTo(1));
 }
 [Test]public async Task UncertainCreationCannotBeRepeated() {
  Task<AutomationManagedDevices.Outcome> Crash(AutomationManagedDevices.Operation p,NetworkCredential c,string f,CancellationToken t) {calls++;throw new IOException("synthetic connection interruption");}
  Assert.ThrowsAsync<IOException>(async()=>await Advance(Crash));
  Assert.That((await Advance()).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));Assert.That(calls,Is.EqualTo(1));
 }
 [Test]public async Task ChangedPlanCannotRecoverAnEarlierAttempt() {
  await Advance();operation=operation with{Parent=operation.Parent with{DeviceId=99}};
  Assert.ThrowsAsync<InvalidDataException>(async()=>await Advance());Assert.That(calls,Is.EqualTo(1));
 }
 [TestCase(false,true,SubmissionWorkflowStatus.Failed)]
 [TestCase(true,false,SubmissionWorkflowStatus.OutcomeUnknown)]
 public async Task ReadinessAndReservationReleaseAreBothRequired(bool ready,bool released,SubmissionWorkflowStatus status) {
  Task<AutomationManagedDevices.Outcome> Incomplete(AutomationManagedDevices.Operation p,NetworkCredential c,string f,CancellationToken t) {
   calls++;Directory.CreateDirectory(f);var outcome=new AutomationManagedDevices.Outcome(ready,released,Bindings());AutomationFiles.Write(Path.Combine(f,"result.json"),outcome);return Task.FromResult(outcome);
  }
  Assert.That((await Advance(Incomplete)).Status,Is.EqualTo(status));
  Assert.That((await Advance()).Status,Is.EqualTo(status));Assert.That(calls,Is.EqualTo(1));
  Assert.That(File.Exists(Path.Combine(root,AutomationManagedDevices.ReceiptName)),Is.False);
 }
 [TestCase("parent")][TestCase("native")][TestCase("alias")]
 public void BindingsCannotSubstituteOtherDevices(string variant) {
  var bindings=Bindings();bindings[1]=variant switch {
   "parent"=>bindings[1] with{DeviceId=71},"native"=>bindings[1] with{NativeLoadId=101},_=>bindings[1] with{Alias="unreviewed"}};
  Assert.Throws<InvalidDataException>(()=>AutomationManagedDevices.ValidateBindings(operation,bindings));
 }
 [Test]public async Task InitialConfigurationAndNativeMigrationAreRetainedWithoutCleanup() {
  var events=new List<string>();int ordinal=0;
  var result=await AutomationManagedDevices.Setup(operation,root,_=>Task.CompletedTask,
   (request,journal,ct)=>{events.Add("create:"+request.ManagedDeviceId);ordinal++;return Task.FromResult(new ManagedDeviceResult(100+ordinal,ordinal==1?"ConfigurationRequired":"Ready"){NativeLoadId=ordinal==2?103:null});},
   (binding,input,ct)=>{events.Add("configure:"+binding.Alias);using var values=JsonDocument.Parse(File.ReadAllText(input));Assert.That(values.RootElement.GetProperty("ActivationMarker").GetString(),Is.EqualTo("true"));return Task.FromResult(new DriverConfigurationResult(binding.DeviceId,true,true));},
   (journal,commands,ct)=>{bool light=Path.GetFileName(journal)=="light";events.Add("observe:"+Path.GetFileName(journal));return Task.FromResult((new ManagedDeviceResult(light?102:101,"Ready"){NativeLoadId=light?103:null},true));},default);
  Assert.That(result.Ready,Is.True);Assert.That(result.ReservationReleased,Is.False);Assert.That(result.Bindings,Is.EqualTo(Bindings()));
  Assert.That(events,Is.EqualTo(new[]{"create:physical-sensor","configure:sensor","observe:sensor","create:physical-light","observe:light"}));
  Assert.That(File.Exists(Path.Combine(root,"sensor-configuration-result.json")),Is.True);
 }
 [Test]public async Task OnlineChildWithMissingControlsStopsBeforeCreatingLaterChildren() {
  int created=0;
  var outcome=await AutomationManagedDevices.Setup(operation,root,_=>Task.CompletedTask,
   (request,journal,t)=>{created++;return Task.FromResult(new ManagedDeviceResult(101,"ConfigurationRequired"));},
   (b,i,t)=>Task.FromResult(new DriverConfigurationResult(101,true,true)),
   (j,c,t)=>Task.FromResult((new ManagedDeviceResult(101,"Ready"),false)),default);
  Assert.That(outcome.Ready,Is.False);Assert.That(created,Is.EqualTo(1));
 }
 [Test]public async Task OrderedWizardInputsUseTheSamePublicConfigurationContract() {
  var child=operation.Plan.Children[0] with{Configuration=[],ConfigurationSteps=[new("Activation",new(){["ActivationMarker"]="true"})]};
  operation=operation with{Plan=new([child])};AutomationManagedDevices.ValidatePlan(operation.Plan);
  var result=await AutomationManagedDevices.Setup(operation,root,_=>Task.CompletedTask,
   (r,j,t)=>Task.FromResult(new ManagedDeviceResult(101,"ConfigurationRequired")),
   (b,input,t)=>{var parsed=DriverConfiguration.ReadInputs(input);Assert.That(parsed.Steps!.Single().Id,Is.EqualTo("Activation"));
    Assert.That(parsed.Steps!.Single().Values["ActivationMarker"],Is.EqualTo("true"));return Task.FromResult(new DriverConfigurationResult(101,true,true));},
   (j,c,t)=>Task.FromResult((new ManagedDeviceResult(101,"Ready"),true)),default);
  Assert.That(result.Ready,Is.True);
  Assert.Throws<InvalidDataException>(()=>AutomationManagedDevices.ValidatePlan(new([child with{Configuration=new(){["Other"]="value"}}])));
 }
 [Test]public void FixtureReferencesBecomeActualNumericIdsWithoutChangingOtherValues() {
  var fixture=JsonSerializer.SerializeToElement(new{Sensor="${managed:sensor:deviceId}",Lights=new[]{"${managed:light:nativeLoadId}"},Other=555});
  var actual=AutomationManagedDevices.RenderInputs(fixture,Bindings());
  Assert.That(actual.GetProperty("Sensor").GetInt32(),Is.EqualTo(101));Assert.That(actual.GetProperty("Lights")[0].GetInt32(),Is.EqualTo(103));Assert.That(actual.GetProperty("Other").GetInt32(),Is.EqualTo(555));
  Assert.That(fixture.GetProperty("Sensor").ValueKind,Is.EqualTo(JsonValueKind.String));
 }
 [TestCase("${managed:unknown:deviceId}")][TestCase("${managed:sensor:nativeLoadId}")]
 [TestCase("prefix ${managed:sensor:deviceId}")][TestCase("${managed:sensor:deviceId} trailing")]
 public void InvalidOrUnavailableBindingHasNoFallback(string placeholder)=>Assert.Throws<InvalidDataException>(()=>AutomationManagedDevices.RenderInputs(JsonSerializer.SerializeToElement(new{Id=placeholder}),Bindings()));
 [TestCase("alias")][TestCase("physical")][TestCase("room")]
 public void DuplicateSelectionsFailBeforeAProducerRuns(string variant) {
  var first=operation.Plan.Children[0];var second=operation.Plan.Children[1];
  second=variant switch{"alias"=>second with{Alias=first.Alias.ToUpperInvariant()},"physical"=>second with{ManagedDeviceId=first.ManagedDeviceId},_=>second with{Name=first.Name}};
  Assert.Throws<InvalidDataException>(()=>AutomationManagedDevices.ValidatePlan(new([first,second])));
 }
 [Test]public void RemovalBindsTheActualNativeLoadAndItsExplicitNonvisualWrapper() {
  var app=new DriverRemovalAppPlan(new("adb","emulator","com.crestron.phoenix.app","home","lock"),
   [new(0,"Demo Light",6,"Office",false){NativeLight=true,ManagedAlias="light"}],[])
   {NonvisualManagedAliases=["light"],IncludeDeployedPlatformAsNonvisual=true};
  var resolved=AutomationRemoval.BindManagedApp(app,71,Bindings());
  Assert.That(resolved.Tiles.Single().DeviceId,Is.EqualTo(103));Assert.That(resolved.Tiles.Single().ManagedAlias,Is.Null);
  Assert.That(resolved.NonvisualDeviceIds,Is.EqualTo(new[]{102,71}));Assert.That(app.Tiles.Single().DeviceId,Is.Zero);
  Assert.Throws<InvalidDataException>(()=>AutomationRemoval.BindManagedApp(app with{Tiles=[app.Tiles[0] with{Name="Different"}]},71,Bindings()));
 }
 [TestCase("true",false,true)][TestCase("false",false,false)][TestCase("true",true,false)]
 public void CachedSettingsCannotSilentlyOverrideReviewedInputs(string value,bool masked,bool matches) {
  var snapshot=new DriverConfigurationSnapshot(101,"Demo Sensor","Sensor","1.0.0.0",true,true,true,
   [new("ActivationMarker",null,"bool",true,false,masked,true,JsonSerializer.SerializeToElement(value))]);
  Assert.That(AutomationManagedDevices.ConfigurationMatches(new(){["ActivationMarker"]="true"},snapshot),Is.EqualTo(matches));
 }
}
