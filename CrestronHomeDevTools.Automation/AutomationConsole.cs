// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Diagnostics;
using System.Security.Cryptography;

namespace CrestronHomeDevTools.Automation;

/// <summary>Runs only bundled public document-preparation commands. No developer-installed Python or arbitrary shell command.</summary>
internal static class AutomationConsole
{
 internal static async Task<int> Run(SubmissionAutomationConsole console,string[] args,string logDirectory,CancellationToken token)
 {
  if(args.Length<2 || args[0]!="submission" || args[1] is not ("prepare-review" or "prepare-signed-review" or "prepare-delivery"))throw new InvalidDataException("Unsupported document operation.");
  using var pins=new ToolPins(console);
  Directory.CreateDirectory(logDirectory);
  var start=new ProcessStartInfo(Path.Combine(console.Directory,"CrestronHomeDevTools.Console.exe")) {
   WorkingDirectory=console.Directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true };
  foreach(string arg in args)start.ArgumentList.Add(arg);
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromMinutes(10));
  using var process=Process.Start(start)??throw new IOException("Document worker could not start.");
  process.StandardInput.Close();
  async Task Save(Stream input,string name) {
   await using var output=new FileStream(Path.Combine(logDirectory,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
   byte[] buffer=new byte[8192];long size=0;int count;
   while((count=await input.ReadAsync(buffer,deadline.Token))>0) {
    if((size+=count)>8*1024*1024) {deadline.Cancel();throw new InvalidDataException("Document diagnostic exceeded its limit.");}
    await output.WriteAsync(buffer.AsMemory(0,count),deadline.Token);
   }
  }
  Task stdout=Save(process.StandardOutput.BaseStream,"stdout.json"),stderr=Save(process.StandardError.BaseStream,"stderr.txt");
  try {
   await Task.WhenAll(process.WaitForExitAsync(deadline.Token),stdout,stderr);
   return process.ExitCode;
  } finally {
   if(!process.HasExited) {process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None);}
  }
 }
 private sealed class ToolPins:IDisposable {
  private readonly List<FileStream> handles=[];
  internal ToolPins(SubmissionAutomationConsole console) {
   try {
    if(!Path.IsPathFullyQualified(console.Directory) || console.Files.Length is 0 or >4096 ||
     console.Files.Select(f=>f.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=console.Files.Length)
     throw new InvalidDataException("Select a complete pinned console installation.");
    var actual=new HashSet<string>(StringComparer.Ordinal);var pending=new Stack<string>();pending.Push(console.Directory);int entries=0;
    while(pending.TryPop(out string? dir)) {
     if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked tool directory.");
     foreach(string path in Directory.EnumerateFileSystemEntries(dir)) {
      var attributes=File.GetAttributes(path);
      if((attributes&FileAttributes.ReparsePoint)!=0 || ++entries>4096)throw new InvalidDataException("Unsafe tool inventory.");
      if((attributes&FileAttributes.Directory)!=0)pending.Push(path);else actual.Add(Path.GetRelativePath(console.Directory,path).Replace('\\','/'));
     }
    }
    if(!actual.SetEquals(console.Files.Select(f=>f.RelativePath)))throw new InvalidDataException("Missing or uninventoried console files.");
    foreach(string required in new[]{"CrestronHomeDevTools.Console.exe","CrestronHomeDevTools.Console.dll","CrestronHomeDevTools.dll","submission-tools/manifest.json","submission-tools/runtime/python.exe"})
     if(!actual.Contains(required))throw new InvalidDataException("The complete Windows console download is required.");
    foreach(var pin in console.Files) {
     if(!SubmissionEvidence.SafeEvidencePath(console.Directory,pin.RelativePath,out string path))throw new InvalidDataException("Invalid tool path.");
     var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);handles.Add(file);
     if(Convert.ToHexStringLower(SHA256.HashData(file))!=pin.Sha256)throw new InvalidDataException("Pinned console changed.");
    }
   } catch {Dispose();throw;}
  }
  public void Dispose(){foreach(var file in handles)file.Dispose();}
 }
}
