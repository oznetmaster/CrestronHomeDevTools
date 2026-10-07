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
  try {AutomationOperatorBindings.Validate(settings,releaseTemplate);}
  catch(InvalidDataException e) {missing.Add(e.Message);}
  if(string.IsNullOrWhiteSpace(settings.CredentialBindings))missing.Add("CredentialBindings");
  if(settings.PreEnduranceSeparateProcessor && (string.IsNullOrWhiteSpace(settings.PreEnduranceCredentialBindings) ||
   !Path.IsPathFullyQualified(settings.PreEnduranceCredentialBindings)))missing.Add("PreEnduranceCredentialBindings for separate processor");
  if(!settings.PreEnduranceSeparateProcessor && settings.PreEnduranceCredentialBindings!=null)
   missing.Add("PreEnduranceSeparateProcessor for PreEnduranceCredentialBindings");
  if(settings.NUnit.LocalTests.Length==0)missing.Add("NUnit.LocalTests");
  if(settings.NUnit.ProcessorSuites.Length==0)missing.Add("NUnit.ProcessorSuites");
  if(settings.InstalledAppTests==null && (settings.NUnit.AndroidTests==null || settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null))
   missing.Add("InstalledAppTests or NUnit.AndroidTests with ActualDriver and ReleaseCandidate");
  if(settings.Endurance==null)missing.Add("Endurance");
  else if(!(releaseTemplate && settings.Endurance.Plan.ReservationId=="${reservationId}") && !Guid.TryParseExact(settings.Endurance.Plan.ReservationId,"N",out _))missing.Add("Endurance.Plan.ReservationId (GUID in N format)");
  if(settings.EnduranceFromDeployment && (settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null || settings.EnduranceProbeSettingsTemplate==null))
   missing.Add("EnduranceFromDeployment requires NUnit.ActualDriver, NUnit.ReleaseCandidate and EnduranceProbeSettingsTemplate");
  if(settings.Review==null)missing.Add("Review");
  if(settings.PreEnduranceTests!=null && (settings.InstalledAppTests==null || settings.Endurance==null || settings.Review==null))
   missing.Add("PreEnduranceTests requires InstalledAppTests, Endurance and Review");
  if(settings.PreEnduranceFixtureSettings!=null && settings.PreEnduranceTests==null)
   missing.Add("PreEnduranceTests for PreEnduranceFixtureSettings");
  if(settings.PreEnduranceSeparateProcessor && (settings.PreEnduranceTests==null || settings.PreEnduranceFixtureSettings==null ||
   settings.PreEnduranceFromDeployment || string.Equals(settings.PreEnduranceTests.Host,settings.NUnit.Host,StringComparison.OrdinalIgnoreCase)))
   missing.Add("Separate-processor initial tests require a distinct explicit target and fixture inputs without main-target deployment binding");
  if(settings.PreEnduranceFromDeployment && (settings.PreEnduranceTests==null || settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null))
   missing.Add("PreEnduranceTests and actual candidate deployment for PreEnduranceFromDeployment");
  if(settings.ResponseComparison!=null && (settings.PostEnduranceTests==null || settings.Endurance==null || settings.Review==null))
   missing.Add("ResponseComparison requires Endurance, PostEnduranceTests and Review");
  if(settings.PostEnduranceFixtureSettings!=null && settings.PostEnduranceTests==null)
   missing.Add("PostEnduranceTests for PostEnduranceFixtureSettings");
  if(settings.PostEnduranceFromDeployment && (settings.PostEnduranceTests==null || settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null))
   missing.Add("PostEnduranceTests and actual candidate deployment for PostEnduranceFromDeployment");
  if(settings.Removal!=null && (settings.PostEnduranceTests==null || !settings.PostEnduranceFromDeployment || settings.Review==null ||
   settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null || string.IsNullOrWhiteSpace(settings.Removal.RequirementId)))
   missing.Add("Removal requires deployment-bound PostEnduranceTests, Review and a removal RequirementId");
  // A declared initial omission is already known before equipment is reserved.
  // Report it here as well as enforcing real observations at the endurance gate.
  // Only the operations intrinsically following the interval may remain planned gaps.
  if(settings.Review?.PlannedGaps is {} gaps) {
   var later=new HashSet<string>(StringComparer.Ordinal);
   if(settings.Endurance!=null)later.Add(settings.Endurance.Plan.Requirement.Id);
   if(settings.Removal!=null)later.Add(settings.Removal.RequirementId);
   if(settings.ResponseComparison!=null)later.Add(settings.ResponseComparison.RequirementId);
   foreach(var gap in gaps) {
    if(gap==null || string.IsNullOrWhiteSpace(gap.RequirementId))missing.Add("Review.PlannedGaps requires an explicit requirement ID");
    else if(!later.Contains(gap.RequirementId))missing.Add($"Resolve declared pre-endurance gap: {gap.RequirementId}");
   }
  }
  {
   try {AutomationDelivery.ValidateMode(settings);}
   catch(InvalidDataException e){missing.Add(e.Message);}
   if(settings.Mode==SubmissionAutomationMode.Rehearsal && settings.Protected?.Delivery?.RehearsalRecipient==null)
    missing.Add("Protected.Delivery.RehearsalRecipient");
   if(settings.Protected==null)missing.Add("Protected");
   else {
    if(string.IsNullOrWhiteSpace(settings.Protected.CredentialBindings))missing.Add("Protected.CredentialBindings");
    if(string.IsNullOrWhiteSpace(settings.Protected.SigningApproval.DocumentPath) || string.IsNullOrWhiteSpace(settings.Protected.SigningApproval.PinPath))missing.Add("Protected.SigningApproval");
    if(string.IsNullOrWhiteSpace(settings.Protected.DeliveryApproval.DocumentPath) || string.IsNullOrWhiteSpace(settings.Protected.DeliveryApproval.PinPath))missing.Add("Protected.DeliveryApproval");
    if(settings.Protected.Delivery==null)missing.Add("Protected.Delivery");
   }
  }
  return new(missing.Count==0,missing.Distinct(StringComparer.Ordinal).ToArray());
 }
}
