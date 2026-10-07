// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

// Phase three snapshots already-completed test evidence for portable document preparation.
// Publication never executes equipment tests or replaces partial/failed evidence.
internal static class AutomationReviewInputs
{
 internal const string Folder="review-snapshot";
 internal const string ReceiptName="review-snapshot.json";
 internal sealed record Receipt(string InputSha256,string InputsSha256,AutomationReview.Prepared Prepared,string? DocumentToolingSha256=null);
 private sealed record Intent(string InputSha256,string PreparedSha256,string SettingsSha256,string? DocumentToolingSha256=null);
 internal static async Task<SubmissionWorkflowStepResult> Seal(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,
  CancellationToken token,Func<SubmissionAutomationConsole,string[],string,CancellationToken,Task<int>>? execute=null) {
  if(settings.Review is not {} plan)return new(SubmissionWorkflowStatus.NeedsInput,ReasonCode:"review-plan-required");
  string root=c.RunDirectory,output=Path.Combine(root,Folder),intentPath=Path.Combine(root,"review-snapshot-intent.json");
  if(File.Exists(Path.Combine(root,ReceiptName))) {Verify(c);return Completed(c);}
  string? toolingPin=ToolingPin(root);
  AutomationReview.Prepared prepared;
  if(File.Exists(intentPath)) {
   var intent=AutomationFiles.Read<Intent>(intentPath);
   string path=Path.Combine(root,"review-inputs","prepared.json");
   if(intent.InputSha256!=c.Checkpoint.InputSha256 || intent.DocumentToolingSha256!=toolingPin || AutomationFiles.Hash(path)!=intent.PreparedSha256)
    throw new InvalidDataException("Review input publication identity changed.");
   prepared=AutomationFiles.Read<AutomationReview.Prepared>(path);
   if(AutomationFiles.Hash(prepared.SettingsPath)!=intent.SettingsSha256)throw new InvalidDataException("Prepared review inputs changed.");
   // A complete publication can be reconciled without running a test or copying mutable producers again.
   if(!File.Exists(Path.Combine(output,"COMPLETE")))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-test-input-publication");
  } else {
   if(Directory.Exists(output))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-existing-review-snapshot");
   prepared=AutomationReview.PrepareInputs(c,settings,plan,token);
   AutomationFiles.Write(intentPath,new Intent(c.Checkpoint.InputSha256,
    AutomationFiles.Hash(Path.Combine(root,"review-inputs","prepared.json")),AutomationFiles.Hash(prepared.SettingsPath),toolingPin));
   var args=new List<string>{"submission","freeze-review-inputs","--settings",prepared.SettingsPath,"--output",output,
    "--candidate-sha256",prepared.CandidateSha256,"--inventory-sha256",prepared.InventorySha256,"--mapping-sha256",prepared.MappingSha256,
    "--source-commit",settings.Release.SourceCommit};
   if(prepared.DeclarationsPath!=null)args.AddRange(["--review-mode","declared-gaps","--declarations",prepared.DeclarationsPath,"--declarations-sha256",prepared.DeclarationsSha256!]);
   if(prepared.AndroidPinsPath!=null)args.AddRange(["--android-pins",prepared.AndroidPinsPath,"--android-pins-sha256",prepared.AndroidPinsSha256!]);
   int result=await(execute??AutomationConsole.Run)(plan.Console,args.ToArray(),Path.Combine(root,"review-snapshot-process"),token);
   if(result!=0)return new(SubmissionWorkflowStatus.Failed,ReasonCode:"test-input-publication-failed");
  }
  var receipt=new Receipt(c.Checkpoint.InputSha256,AutomationFiles.Hash(Path.Combine(output,"review-inputs.json")),prepared,toolingPin);
  Verify(c,receipt);
  return AutomationFiles.Complete(c,ReceiptName,receipt);
 }
 private static SubmissionWorkflowStepResult Completed(SubmissionWorkflowStepContext c)=>new(SubmissionWorkflowStatus.Completed,
  new(ReceiptName,AutomationFiles.Hash(Path.Combine(c.RunDirectory,ReceiptName))));
 internal static Receipt Verify(SubmissionWorkflowStepContext c) {
  var receipt=AutomationFiles.Read<Receipt>(Path.Combine(c.RunDirectory,ReceiptName));Verify(c,receipt);return receipt;
 }
 private static string? ToolingPin(string root)=>File.Exists(Path.Combine(root,AutomationReviewTooling.FileName))?AutomationFiles.Hash(Path.Combine(root,AutomationReviewTooling.FileName)):null;
 private static void Verify(SubmissionWorkflowStepContext c,Receipt receipt) {
  if(receipt.DocumentToolingSha256!=ToolingPin(c.RunDirectory))throw new InvalidDataException("Document tool binding changed after input publication.");
  if(receipt.InputSha256!=c.Checkpoint.InputSha256)throw new InvalidDataException("Portable review inputs belong to another run.");
  string root=Path.Combine(c.RunDirectory,Folder);
  string Safe(string name) {
   if(!SubmissionEvidence.SafeEvidencePath(root,name,out var full))throw new InvalidDataException("Unsafe portable review input.");return full;
  }
  string manifest=Safe("review-inputs.json");
  if(AutomationFiles.Hash(manifest)!=receipt.InputsSha256 || File.ReadAllText(Safe("COMPLETE")).Trim()!=receipt.InputsSha256)
   throw new InvalidDataException("Portable review input publication changed or is incomplete.");
  using var doc=JsonDocument.Parse(File.ReadAllBytes(manifest));var data=doc.RootElement;
  if(data.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidDataException("Unsupported portable review inputs.");
  var options=data.GetProperty("options");
  void Match(string key,string expected) {if(options.GetProperty(key).GetString()!=expected)throw new InvalidDataException("Portable review input identity changed.");}
  Match("sourceCommit",c.Checkpoint.Release.SourceCommit);Match("candidateSha256",receipt.Prepared.CandidateSha256);
  Match("inventorySha256",receipt.Prepared.InventorySha256);Match("mappingSha256",receipt.Prepared.MappingSha256);
  Match("reviewMode",receipt.Prepared.DeclarationsSha256==null?"complete":"declared-gaps");
  foreach(var pair in new[]{("declarationsSha256",receipt.Prepared.DeclarationsSha256),("androidPinsSha256",receipt.Prepared.AndroidPinsSha256)}) {
   if(pair.Item2==null) {if(options.TryGetProperty(pair.Item1,out _))throw new InvalidDataException("Unexpected portable review option.");}
   else Match(pair.Item1,pair.Item2);
  }
  var files=data.GetProperty("files");if(files.GetArrayLength() is 0 or >16384)throw new InvalidDataException("Invalid portable review inventory.");
  var expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"review-inputs.json","COMPLETE"};long total=0;
  foreach(var file in files.EnumerateArray()) {
   string name=file.GetProperty("relativePath").GetString()!;
   if(!expected.Add(name))throw new InvalidDataException("Duplicate portable review file.");
   string path=Safe(name);long size=new FileInfo(path).Length;
   if(size>64*1024*1024 || (total+=size)>1024L*1024*1024 || AutomationFiles.Hash(path)!=file.GetProperty("sha256").GetString())
    throw new InvalidDataException("Portable test evidence changed.");
  }
  var pending=new Stack<string>();pending.Push(root);var actual=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int entries=0;
  while(pending.TryPop(out var directory)) {
   if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked portable input directory.");
   foreach(string path in Directory.EnumerateFileSystemEntries(directory)) {
    var attributes=File.GetAttributes(path);
    if(++entries>16384*3 || (attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Unsafe portable input tree.");
    if((attributes&FileAttributes.Directory)!=0)pending.Push(path);else actual.Add(Path.GetRelativePath(root,path).Replace('\\','/'));
   }
  }
  if(!actual.SetEquals(expected))throw new InvalidDataException("Unexpected or missing portable review files.");
 }
}
