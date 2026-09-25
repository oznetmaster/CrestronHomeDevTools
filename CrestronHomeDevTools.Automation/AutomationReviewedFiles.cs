// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text.Json;

namespace CrestronHomeDevTools.Automation;

// Shared retention boundary for explicitly reviewed, non-executable inputs.
internal static class AutomationReviewedFiles
{
 private sealed record Receipt(string IdentitySha256,SubmissionEvidenceFile[] Files);
 internal static Action Retain(string root,string prefix,object identity,string sourceDirectory,
  SubmissionEvidenceFile[] files,CancellationToken token) {
  if(!Path.IsPathFullyQualified(sourceDirectory) || files is null || files.Length is <1 or >4096 ||
   files.Any(f=>f==null || f.Sha256?.Length!=64 || !f.Sha256.All(char.IsAsciiHexDigit) ||
    string.IsNullOrWhiteSpace(f.RelativePath) || f.RelativePath.Contains('\\')) ||
   files.Select(f=>f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=files.Length)
   throw new InvalidDataException("Reviewed evidence requires a bounded, unique pinned file inventory.");
  string output=Path.Combine(root,prefix.TrimEnd('/')),receiptPath=Path.Combine(root,prefix.TrimEnd('/')+"-receipt.json");
  if(Path.GetFullPath(sourceDirectory).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase) ||
   Path.GetFullPath(sourceDirectory).StartsWith(Path.GetFullPath(output)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
   throw new InvalidDataException("Original evidence must be outside the retained destination.");
  string binding=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{identity,sourceDirectory,files},AutomationFiles.Json)));
  bool retained=File.Exists(receiptPath);
  if(retained && AutomationFiles.Read<Receipt>(receiptPath).IdentitySha256!=binding)
   throw new InvalidDataException("Retained prior-evidence binding changed.");
  if(!retained)Directory.CreateDirectory(output);
  if(!Directory.Exists(output) || (File.GetAttributes(output)&FileAttributes.ReparsePoint)!=0)
   throw new InvalidDataException("Reviewed evidence output is missing or redirected.");
  long bytes=0;
  foreach(var file in files) {
   token.ThrowIfCancellationRequested();
   string target=Path.Combine(output,file.RelativePath.Replace('/',Path.DirectorySeparatorChar));
   if(!retained) {
    if(!SubmissionEvidence.SafeEvidencePath(sourceDirectory,file.RelativePath,out var source))
     throw new InvalidDataException("Invalid reviewed evidence source path.");
    // SafeEvidencePath has checked traversal and links before any destination is created.
    string parent=output;
    foreach(string part in file.RelativePath.Split('/')[..^1]) {
     parent=Path.Combine(parent,part);Directory.CreateDirectory(parent);
     if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Redirected reviewed evidence output.");
    }
    using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
    if(input.Length>512L*1024*1024 || bytes+input.Length>512L*1024*1024)throw new InvalidDataException("Reviewed evidence exceeds 512 MiB.");
    if(!Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(input)).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))
     throw new InvalidDataException("Pinned original evidence changed.");
    if(!File.Exists(target)) {input.Position=0;using var copy=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);input.CopyTo(copy);copy.Flush(true);}
   }
   if(!SubmissionEvidence.SafeEvidencePath(root,prefix+file.RelativePath,out target) ||
    !AutomationFiles.Hash(target).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Retained original evidence changed or is missing.");
   bytes+=new FileInfo(target).Length;
   if(bytes>512L*1024*1024)throw new InvalidDataException("Reviewed evidence exceeds 512 MiB.");
  }
  var pending=new Queue<string>();pending.Enqueue(output);int count=0,entries=0;
  while(pending.TryDequeue(out var directory))foreach(string entry in Directory.EnumerateFileSystemEntries(directory)) {
   var attributes=File.GetAttributes(entry);
   if(++entries>8192 || (attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Unexpected or redirected reviewed evidence tree.");
   if((attributes&FileAttributes.Directory)!=0)pending.Enqueue(entry);else count++;
  }
  if(count!=files.Length)
   throw new InvalidDataException("Reviewed evidence contains unreviewed extra files.");
  return ()=>AutomationFiles.Write(receiptPath,new Receipt(binding,files));
 }
}
