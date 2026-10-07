// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;
namespace CrestronHomeDevTools.Automation;

/// <summary>Installed outside build-writable storage. This pins the protected worker's executable tools,
/// permitted run roots/repositories, credential store and approval locations independently of build output.</summary>
public sealed record SubmissionAutomationProtectedWorker(int SchemaVersion,string[] AllowedPrivateRoots,string[] Repositories,
 SubmissionAutomationConsole Console,SubmissionAutomationProtectedPlan Plan)
{
 /// <summary>Independent rehearsal bindings and approvals, selected only for rehearsal runs.</summary>
 public SubmissionAutomationProtectedPlan? RehearsalPlan { get; init; }
}

internal sealed record AutomationProtectedWorker(SubmissionAutomationProtectedWorker Settings,string Sha256)
{
 internal static AutomationProtectedWorker Load(string path,string digest) {
  if(!Path.IsPathFullyQualified(path) || digest.Length!=64 || digest.Any(c=>!(char.IsAsciiDigit(c)||c is >= 'a' and <= 'f')) || new FileInfo(path).Length>16*1024*1024)
   throw new InvalidDataException("Supply the independently pinned protected-worker configuration.");
  byte[] bytes=File.ReadAllBytes(path);
  if(Convert.ToHexStringLower(SHA256.HashData(bytes))!=digest)throw new InvalidDataException("Protected-worker configuration changed.");
  var settings=JsonSerializer.Deserialize<SubmissionAutomationProtectedWorker>(bytes,AutomationFiles.Json)??throw new InvalidDataException("Empty protected-worker configuration.");
  if(settings.SchemaVersion!=1 || settings.AllowedPrivateRoots.Length==0 || settings.Repositories.Length==0 ||
   settings.AllowedPrivateRoots.Any(p=>!Path.IsPathFullyQualified(p)) || !Path.IsPathFullyQualified(settings.Console.Directory) || !Path.IsPathFullyQualified(settings.Plan.CredentialBindings))
   throw new InvalidDataException("Invalid protected-worker scope.");
  // Build output must never select or replace a protected executable, credential binding or approval.
  foreach(var plan in new[]{settings.Plan,settings.RehearsalPlan}.OfType<SubmissionAutomationProtectedPlan>()) {
   foreach(string value in new[]{path,settings.Console.Directory,plan.CredentialBindings,plan.SigningApproval.DocumentPath,
    plan.SigningApproval.PinPath,plan.DeliveryApproval.DocumentPath,plan.DeliveryApproval.PinPath}) {
    if(!Path.IsPathFullyQualified(value))throw new InvalidDataException("Protected paths must be absolute.");
    if(settings.AllowedPrivateRoots.Any(root=>Within(root,value)))throw new InvalidDataException("Protected settings/tools/authority must be outside the build-writable run roots.");
   }
  }
  if(settings.Plan.Environment!=SubmissionDeliveryEnvironment.Production ||
   settings.RehearsalPlan is {} rehearsal && rehearsal.Environment!=SubmissionDeliveryEnvironment.Rehearsal)
   throw new InvalidDataException("Protected worker plans must name their correct delivery environments.");
  return new(settings,digest);
 }
 internal SubmissionAutomationSettings Bind(SubmissionAutomationSettings supplied) {
  if(!Settings.Repositories.Contains(supplied.Release.Repository,StringComparer.OrdinalIgnoreCase) ||
   !Settings.AllowedPrivateRoots.Any(r=>string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)),Path.TrimEndingDirectorySeparator(Path.GetFullPath(supplied.PrivateRoot)),StringComparison.OrdinalIgnoreCase)))
   throw new InvalidDataException("Run is outside this protected worker's installed scope.");
  string key=SubmissionWorkflow.RunKey(supplied.Release);
  SubmissionAutomationApprovalChannel Channel(SubmissionAutomationApprovalChannel channel)=>new(
   channel.DocumentPath.Replace("${runKey}",key,StringComparison.Ordinal),channel.PinPath.Replace("${runKey}",key,StringComparison.Ordinal));
  var selected=supplied.Mode switch {
   SubmissionAutomationMode.Submit=>Settings.Plan,
   SubmissionAutomationMode.Rehearsal=>Settings.RehearsalPlan??throw new InvalidDataException("Install an independently approved rehearsal protected plan."),
   _=>throw new InvalidDataException("Unknown submission mode.")};
  var plan=selected with{SigningApproval=Channel(selected.SigningApproval),DeliveryApproval=Channel(selected.DeliveryApproval)};
  if(supplied.Review==null)throw new InvalidDataException("Run has no retained review configuration.");
  var bound=supplied with{Protected=plan,Review=supplied.Review with{Console=Settings.Console}};
  AutomationDelivery.ValidateMode(bound);
  return bound;
 }
 private static bool Within(string root,string path) {
  string relative=Path.GetRelativePath(Path.GetFullPath(root),Path.GetFullPath(path));
  return relative=="." || (!Path.IsPathFullyQualified(relative) && relative!=".." && !relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal));
 }
}
