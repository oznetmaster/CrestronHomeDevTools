// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
using CrestronHomeDevTools.Automation;
using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;
public sealed partial class AutomationInstalledAppTests
{
 [Test]public async Task PlacementPreparationPreservesApprovedHomeSelectionAndProcessorBinding() {
  await ConfigureInitialPlacement();
  var plan=AutomationRemoval.Resolve(context,settings);
  plan=plan with{App=plan.App with{Profile=plan.App.Profile with{AllowedStartingHomes=["Recovery244"]}}};
  int verified=0,opened=0;
  await DriverRemovalWorkflow.PrepareAppAsync(plan,"owner",Guid.NewGuid().ToString(),Path.Combine(context.RunDirectory,"opening"),
   _=>{verified++;return Task.CompletedTask;},default,(c,t)=>{
    opened++;Assert.That(c.Profile.AllowedStartingHomes,Is.EqualTo(new[]{"Recovery244"}));
    Assert.That(c.Profile.ExpectedHomeText,Is.EqualTo(plan.App.Profile.ExpectedHomeText));
    Assert.That(c.ProcessorAddress,Is.EqualTo(plan.Host));Assert.That(c.InstalledDriverId,Is.EqualTo(plan.Target.DeviceId));
    Assert.That(c.RunId,Is.EqualTo("owner"));return Task.CompletedTask;
   });
  Assert.That(opened,Is.EqualTo(1));Assert.That(verified,Is.EqualTo(2));
 }
 private void AuthorizePlacementRecovery(bool released=true,int attempt=1) {
  string folder=Path.Combine(context.RunDirectory,"placement");
  string review=attempt==1?"reviewed-restoration.json":$"reviewed-restoration-{attempt:000}.json";
  AutomationFiles.Write(Path.Combine(folder,review),new {InputSha256=context.Checkpoint.InputSha256,RestorationConfirmed=true,ReservationsReleased=released,OriginalFailurePreserved=true,StartingHome="Recovery244",Owner=Guid.NewGuid().ToString("N"),PhysicalActions=0,Utc=DateTimeOffset.UtcNow,Payload=new{Matched=true}});
  var files=Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Select(p=>new SubmissionWorkflowReceipt(Path.GetRelativePath(context.RunDirectory,p).Replace('\\','/'),AutomationFiles.Hash(p))).OrderBy(f=>f.RelativePath,StringComparer.Ordinal).ToArray();
  AutomationFiles.Write(Path.Combine(folder,attempt==1?"recovery-request.json":$"recovery-{attempt:000}-request.json"),new AutomationPlacement.RecoveryRequest(context.Checkpoint.InputSha256,files,files.Single(f=>f.RelativePath.EndsWith(review)),"Explicit repair after verified restoration",attempt));
 }
 [Test]public async Task ReviewedPlacementRecoveryPreservesOriginalAndRunsOnlyOnce() {
  await ConfigureInitialPlacement();await Assert.ThrowsAsync<IOException>(async()=>await Place(interrupt:true));
  string original=File.ReadAllText(Path.Combine(context.RunDirectory,"placement","intent.json"));
  AuthorizePlacementRecovery();Assert.That((await Place()).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  await Place();Assert.That(calls,Is.EqualTo(2));
  Assert.That(File.ReadAllText(Path.Combine(context.RunDirectory,"placement","intent.json")),Is.EqualTo(original));
  AutomationPlacement.VerifyRetained(context);
 }
 [TestCase(true)][TestCase(false)]public async Task PlacementRecoveryRejectsChangedEvidenceOrUnreleasedReservations(bool tamper) {
  await ConfigureInitialPlacement();await Assert.ThrowsAsync<IOException>(async()=>await Place(interrupt:true));
  AuthorizePlacementRecovery(released:tamper);
  if(tamper)File.AppendAllText(Path.Combine(context.RunDirectory,"placement","intent.json")," ");
  await Assert.ThrowsAsync<InvalidDataException>(async()=>await Place());Assert.That(calls,Is.EqualTo(1));
 }
 [Test]public async Task InterruptedReviewedPlacementIsNeverReplayed() {
  await ConfigureInitialPlacement();await Assert.ThrowsAsync<IOException>(async()=>await Place(interrupt:true));
  AuthorizePlacementRecovery();await Assert.ThrowsAsync<IOException>(async()=>await Place(interrupt:true));
  Assert.That((await Place()).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));Assert.That(calls,Is.EqualTo(2));
 }
 [Test]public async Task SubsequentPlacementRepairRequiresItsOwnReviewAndPreservesAllPriorAttempts() {
  await ConfigureInitialPlacement();await Assert.ThrowsAsync<IOException>(async()=>await Place(interrupt:true));
  AuthorizePlacementRecovery();await Assert.ThrowsAsync<IOException>(async()=>await Place(interrupt:true));
  Assert.That((await Place()).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));
  AuthorizePlacementRecovery(attempt:2);Assert.That((await Place()).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(calls,Is.EqualTo(3));Assert.That(Directory.Exists(Path.Combine(context.RunDirectory,"placement","recovery")),Is.True);
  AutomationPlacement.VerifyRetained(context);
 }
 private async Task ConfigureInitialPlacement() {
  await ConfigurePlacement();
  File.WriteAllText(Path.Combine(context.RunDirectory,"app-tests.json"),"synthetic completed app stage");
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.AppTests]=new("app-tests.json",AutomationFiles.Hash(Path.Combine(context.RunDirectory,"app-tests.json")));
  context.Checkpoint.CompletedStages.Remove(SubmissionWorkflowStage.Endurance);
  context=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.Endurance}};
 }
 private Task<SubmissionWorkflowStepResult> Place(bool pass=true,bool restore=true,bool interrupt=false,bool removal=false)=>
  AutomationPlacement.Advance(context,settings,_=>new(),default,(plan,credential,folder,token)=>{
   calls++;Assert.That(plan.Target.DeviceId,Is.EqualTo(167));
   Assert.That(plan.Target.CatalogueId,Is.EqualTo("observed.catalogue.1.0.000.0000"));
   if(interrupt)throw new IOException("Synthetic interruption");
   Directory.CreateDirectory(folder);
   var result=new DriverRemovalWorkflowResult(removal,true,true,null,new(pass,restore));
   File.WriteAllBytes(Path.Combine(folder,"result.json"),JsonSerializer.SerializeToUtf8Bytes(result,AutomationFiles.Json));
   File.WriteAllText(Path.Combine(folder,"trace.xml"),"synthetic observed app placement");
   return Task.FromResult(result);
  });
 [Test]public async Task InitialPlacementCompletesBeforeEnduranceAndDoesNotReplay() {
  await ConfigureInitialPlacement();
  var first=await Place();Assert.That(first.Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(context.Checkpoint.CompletedStages.ContainsKey(SubmissionWorkflowStage.Endurance),Is.False);
  Assert.That((await Place()).Receipt,Is.EqualTo(first.Receipt));Assert.That(calls,Is.EqualTo(1));
  var file=AutomationPlacement.VerifyRetained(context);
  var observation=AutomationFiles.Read<SubmissionEvidenceDocument>(Path.Combine(context.RunDirectory,file.RelativePath)).Observations.Single();
  Assert.That(observation.RequirementId,Is.EqualTo("ui.placement"));
  Assert.That(observation.Execution!.Target,Is.EqualTo("selected.membership"));
  File.AppendAllText(Path.Combine(context.RunDirectory,"placement","operation","trace.xml"),"changed");
  Assert.Throws<InvalidDataException>(()=>AutomationPlacement.VerifyRetained(context));
 }
 [Test]public async Task InterruptedPlacementCannotReplayOrBecomeEvidence() {
  await ConfigureInitialPlacement();
  await Assert.ThrowsAsync<IOException>(async()=>await Place(interrupt:true));
  Assert.That((await Place()).Status,Is.EqualTo(SubmissionWorkflowStatus.OutcomeUnknown));
  Assert.That(calls,Is.EqualTo(1));Assert.That(File.Exists(Path.Combine(context.RunDirectory,AutomationPlacement.ReceiptName)),Is.False);
 }
 [TestCase(false,true,false)][TestCase(true,false,false)][TestCase(true,true,true)]
 public async Task InvalidPlacementCannotSupplyInitialCoverage(bool pass,bool restored,bool removal) {
  await ConfigureInitialPlacement();
  Assert.That((await Place(pass,restored,removal:removal)).Status,Is.Not.EqualTo(SubmissionWorkflowStatus.Completed));
  Assert.That(File.Exists(Path.Combine(context.RunDirectory,AutomationPlacement.ReceiptName)),Is.False);
  await Place();Assert.That(calls,Is.EqualTo(1));
 }
 [Test]public async Task FinalRemovalDoesNotDuplicatePreviouslyVerifiedPlacement() {
  await ConfigureInitialPlacement();await Place();
  context.Checkpoint.CompletedStages[SubmissionWorkflowStage.Endurance]=new("endurance-result.json",AutomationFiles.Hash(Path.Combine(context.RunDirectory,"endurance-result.json")));
  context=context with{Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.PrepareReview}};
  Assert.That((await Remove()).Status,Is.EqualTo(SubmissionWorkflowStatus.Completed));
  var removal=AutomationFiles.Read<SubmissionEvidenceDocument>(Path.Combine(context.RunDirectory,AutomationRemoval.ObservationPath));
  Assert.That(removal.Observations.Select(o=>o.RequirementId),Is.EqualTo(new[]{"system.removal"}));
  AutomationPlacement.VerifyRetained(context);
 }
}
