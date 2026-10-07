// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;
namespace CrestronHomeDevTools.Automation;

// Collects retained test evidence without document templates, forms, signing or delivery.
internal static class AutomationTestEvidence
{
 private sealed record EnduranceEnvelope(string EvidenceDirectory,SubmissionObservation Observation);
 internal static SubmissionEvidenceCompositionReport Collect(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,
  SubmissionAutomationReviewPlan plan,string relativeFolder,CancellationToken token,bool includeRemoval=true) {
  if(relativeFolder is not ("test-assessment" or "test-assessment-before-removal"))throw new InvalidDataException("Invalid test evidence destination.");
  string root=c.RunDirectory,folder=Path.Combine(root,relativeFolder);Directory.CreateDirectory(folder);
  if(AutomationFiles.Hash(plan.Policy.Path)!=plan.Policy.Sha256)throw new InvalidDataException("Test policy changed.");
  AutomationReview.WriteBytes(Path.Combine(folder,"policy.json"),File.ReadAllBytes(plan.Policy.Path),plan.Policy.Sha256);
  // Preserve the exact policy bytes: serialization could change its pinned identity.
  if(AutomationFiles.Hash(Path.Combine(folder,"policy.json"))!=plan.Policy.Sha256)
   throw new InvalidDataException("Test policy must retain its exact pinned representation.");
  var identity=new SubmissionEvidenceIdentity(settings.Release.PackageSha256,settings.Release.SourceCommit,plan.Policy.Sha256,plan.Template.Sha256);
  // Sources must have been retained by a completed test producer, not dropped into the directory later.
  using var producer=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"windows-tests.json")));
  var retained=producer.RootElement.GetProperty("Files").EnumerateArray().ToDictionary(x=>x.GetProperty("RelativePath").GetString()!.Replace('\\','/'),x=>x.GetProperty("Sha256").GetString()!,StringComparer.Ordinal);
  // Installed-app tests run after the Windows/processor producer has closed its
  // inventory. Accept their independent receipt only after that stage completed.
  if(c.Checkpoint.CompletedStages.TryGetValue(SubmissionWorkflowStage.AppTests,out var app) &&
   app.RelativePath is "installed-app-tests.json" or AutomationInitialAdditionalTests.ReceiptName) {
   string receipt=Path.Combine(root,app.RelativePath);
   if(AutomationFiles.Hash(receipt)!=app.Sha256)throw new InvalidDataException("Completed app receipt changed.");
   AutomationInstalledApp.VerifyRetained(root);
   using var installed=JsonDocument.Parse(File.ReadAllBytes(receipt));
   if(installed.RootElement.GetProperty("InputSha256").GetString()!=c.Checkpoint.InputSha256)
    throw new InvalidDataException("App observations belong to another workflow.");
   foreach(var file in installed.RootElement.GetProperty("Files").EnumerateArray())
    if(!retained.TryAdd(file.GetProperty("RelativePath").GetString()!.Replace('\\','/'),file.GetProperty("Sha256").GetString()!))
     throw new InvalidDataException("Producer evidence paths overlap.");
  }
  var sources=new List<SubmissionEvidenceFile>();
  if(settings.PreEnduranceTests!=null)
   foreach(var file in AutomationInitialAdditionalTests.RetainedFiles(c))
    if(!retained.TryAdd(file.RelativePath,file.Sha256) && retained[file.RelativePath]!=file.Sha256)
     throw new InvalidDataException("Additional initial evidence paths overlap.");
  if(settings.ResponseComparison!=null)sources.Add(AutomationResponseComparison.VerifyRetained(c));
  if(includeRemoval && settings.Removal!=null)sources.Add(AutomationRemoval.VerifyRetained(c));
  if(File.Exists(Path.Combine(root,AutomationPlacement.ReceiptName)))sources.Add(AutomationPlacement.VerifyRetained(c));
  if(settings.PostEnduranceTests!=null)
   foreach(var file in AutomationPostEndurance.RetainedFiles(c))
    if(!retained.TryAdd(file.RelativePath,file.Sha256))throw new InvalidDataException("Post-endurance evidence paths overlap.");
  if(plan.SourceApplicability is not null)
   sources.Add(AutomationSourceApplicability.Prepare(root,settings,token));
  if(plan.Applicability is not null)
   sources.Add(AutomationApplicability.Prepare(root,identity,plan,token));
  if(plan.Qualifications is not null)
   sources.Add(AutomationQualifications.Prepare(root,identity,plan,token));
  if(plan.PriorEvidence is not null) {
   var prior=AutomationPriorEvidence.Prepare(root,identity,plan,token);
   string priorPath=Path.Combine(folder,"prior-observations.json");AutomationReview.WriteDocument(priorPath,prior);
   sources.Add(new(relativeFolder+"/prior-observations.json",AutomationFiles.Hash(priorPath)));
  }
  if(plan.ObservationSources.Distinct(StringComparer.Ordinal).Count()!=plan.ObservationSources.Length)throw new InvalidDataException("Duplicate observation source.");
  foreach(string requested in plan.ObservationSources) {
   string relative=AutomationReview.ResolveObservationSource(requested,retained,root);
   if(!retained.TryGetValue(relative,out var hash) || !SubmissionEvidence.SafeEvidencePath(root,relative,out var path) || AutomationFiles.Hash(path)!=hash)
    throw new InvalidDataException("Observation source is not retained verified producer output.");
   string? phase=AutomationReview.ObservationBase(relative);
   if(phase!=null) {
    var document=AutomationFiles.Read<SubmissionEvidenceDocument>(path);
    if(document.SchemaVersion!=1)throw new InvalidDataException("Unsupported additional-test observation document.");
    string rebased=relativeFolder+"/"+phase.Split('/')[0]+"-"+sources.Count.ToString("D3",System.Globalization.CultureInfo.InvariantCulture)+".json";
    AutomationReview.WriteDocument(Path.Combine(root,rebased),new SubmissionEvidenceDocument(1,document.Observations.Select(o=>AutomationReview.Rebase(o,phase)).ToArray()));
    sources.Add(new(rebased,AutomationFiles.Hash(Path.Combine(root,rebased))));
   } else sources.Add(new(relative,hash));
  }
  if(File.Exists(Path.Combine(root,"endurance-evidence.json"))) {
   var envelope=AutomationFiles.Read<EnduranceEnvelope>(Path.Combine(root,"endurance-evidence.json"));
   if(envelope.EvidenceDirectory!=AutomationEnduranceSelection.EvidenceDirectory(c) || envelope.Observation.Identity!=identity)
    throw new InvalidDataException("Endurance must use the configured review policy and candidate.");
   var observation=AutomationReview.Rebase(envelope.Observation,envelope.EvidenceDirectory);
   string path=Path.Combine(folder,"endurance.json");AutomationReview.WriteDocument(path,new SubmissionEvidenceDocument(1,[observation]));
   sources.Add(new(relativeFolder+"/endurance.json",AutomationFiles.Hash(path)));
  }
  if(sources.Count==0)throw new InvalidDataException("No retained observations were supplied.");
  string composition=Path.Combine(folder,"composition.json");AutomationReview.WriteDocument(composition,new SubmissionEvidenceCompositionPlan(1,identity,sources));
  var report=SubmissionEvidenceComposition.CombineFiles(root,relativeFolder+"/composition.json",AutomationFiles.Hash(composition),relativeFolder+"/policy.json",DateTimeOffset.UtcNow,token);
  AutomationReview.WriteDocument(Path.Combine(folder,"composition-report.json"),report);
  // Preserve original outcomes and source lineage; the phase-two gate owns eligibility.
  string observations=Path.Combine(folder,"observations.json");AutomationReview.WriteDocument(observations,report.Observations);

  return report;

 }
}
