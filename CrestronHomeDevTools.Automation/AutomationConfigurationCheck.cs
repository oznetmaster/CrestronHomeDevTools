// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeDevTools.Automation;

public sealed record SubmissionAutomationConfigurationReport(bool AllStageBindingsPresent,string[] MissingBindings);

/// <summary>Read-only completeness check before reserving equipment or running tests.
/// Does not connect to providers, decrypt credentials, validate evidence or grant approval.</summary>
public static class SubmissionAutomationConfiguration
{
 public static SubmissionAutomationConfigurationReport Check(SubmissionAutomationSettings settings)=>Check(settings,false);
 public static SubmissionAutomationConfigurationReport Check(SubmissionAutomationSettings settings, bool releaseTemplate) {
  ArgumentNullException.ThrowIfNull(settings);
  var missing=new List<string>();
  if(string.IsNullOrWhiteSpace(settings.CredentialBindings))missing.Add("CredentialBindings");
  if(settings.NUnit.LocalTests.Length==0)missing.Add("NUnit.LocalTests");
  if(settings.NUnit.ProcessorSuites.Length==0)missing.Add("NUnit.ProcessorSuites");
  if(settings.InstalledAppTests==null && (settings.NUnit.AndroidTests==null || settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null))
   missing.Add("InstalledAppTests or NUnit.AndroidTests with ActualDriver and ReleaseCandidate");
  if(settings.Endurance==null)missing.Add("Endurance");
  else if(!(releaseTemplate && settings.Endurance.Plan.ReservationId=="${reservationId}") && !Guid.TryParseExact(settings.Endurance.Plan.ReservationId,"N",out _))missing.Add("Endurance.Plan.ReservationId (GUID in N format)");
  if(settings.EnduranceFromDeployment && (settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null || settings.EnduranceProbeSettingsTemplate==null))
   missing.Add("EnduranceFromDeployment requires NUnit.ActualDriver, NUnit.ReleaseCandidate and EnduranceProbeSettingsTemplate");
  if(settings.Review==null)missing.Add("Review");
  if(settings.PostEnduranceFixtureSettings!=null && settings.PostEnduranceTests==null)
   missing.Add("PostEnduranceTests for PostEnduranceFixtureSettings");
  if(settings.PostEnduranceFromDeployment && (settings.PostEnduranceTests==null || settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null))
   missing.Add("PostEnduranceTests and actual candidate deployment for PostEnduranceFromDeployment");
  if(settings.Removal!=null && (settings.PostEnduranceTests==null || !settings.PostEnduranceFromDeployment || settings.Review==null ||
   settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null || string.IsNullOrWhiteSpace(settings.Removal.RequirementId)))
   missing.Add("Removal requires deployment-bound PostEnduranceTests, Review and a removal RequirementId");
  if(settings.Mode==SubmissionAutomationMode.Submit) {
   if(settings.Protected==null)missing.Add("Protected");
   else {
    if(string.IsNullOrWhiteSpace(settings.Protected.CredentialBindings))missing.Add("Protected.CredentialBindings");
    if(string.IsNullOrWhiteSpace(settings.Protected.SigningApproval.DocumentPath) || string.IsNullOrWhiteSpace(settings.Protected.SigningApproval.PinPath))missing.Add("Protected.SigningApproval");
    if(string.IsNullOrWhiteSpace(settings.Protected.DeliveryApproval.DocumentPath) || string.IsNullOrWhiteSpace(settings.Protected.DeliveryApproval.PinPath))missing.Add("Protected.DeliveryApproval");
    if(settings.Protected.Delivery==null)missing.Add("Protected.Delivery");
   }
  }
  return new(missing.Count==0,missing.ToArray());
 }
}
