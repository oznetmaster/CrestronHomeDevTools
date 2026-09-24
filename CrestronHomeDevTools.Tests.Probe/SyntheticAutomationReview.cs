// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Security.Cryptography;
using System.Text.Json;
using CrestronHomeDevTools;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;

// Synthetic document integration only. No hardware stages, real signature or delivery authority.
internal static class SyntheticAutomationReview
{
 internal static async Task<int> Run(string root,string consoleDirectory,string? phase=null) {
  string P(string name)=>Path.Combine(root,name);
  string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
  SubmissionAutomationInput Input(string name)=>new(P(name),Hash(P(name)));
  var release=new SubmissionWorkflowRelease("synthetic/never-submitted",1,"synthetic",new('a',40),Hash(P("candidate.pkg")),new('b',64),new('c',64));
  var tools=new SubmissionAutomationConsole(consoleDirectory,Directory.GetFiles(consoleDirectory,"*",SearchOption.AllDirectories)
   .Select(p=>new SubmissionEvidenceFile(Path.GetRelativePath(consoleDirectory,p).Replace('\\','/'),Hash(p))).ToArray());
  var review=new SubmissionAutomationReviewPlan(Input("policy.json"),Input("template.pdf"),Input("inventory.json"),Input("mapping.json"),tools,
   "SYNTHETIC AUTOMATION REHEARSAL - NEVER SUBMITTED","Synthetic Developer",["nunit/observations.json"]);
  var nunit=new WorkflowPlan {Host="synthetic.invalid",CertificateSha256=new('d',64),SshFingerprint="not-used",SourceRoots=[root],LocalTests=[],
   TestPackage=new("unused.csproj","unused.pkg","Synthetic",1),ProcessorSuites=[]};
  var settings=new SubmissionAutomationSettings(1,root,release,root,new("75b3457d-70f3-4c02-940e-22fbd970ef94","1.0.000.0000",PortalSubmissionKind.NewDriver,"Example","support@example.org"),
   "no-credentials-in-this-test",nunit,Review:review);
  if(phase!=null) {
   string authority=root+"-authority";
   settings=settings with{Mode=SubmissionAutomationMode.Submit,Protected=new(Path.Combine(authority,"bindings.json"),
    new(Path.Combine(authority,"sign.json"),Path.Combine(authority,"sign.sha256")),new(Path.Combine(authority,"deliver.json"),Path.Combine(authority,"deliver.sha256")),
    new("fixture@example.org","smtp.example.invalid",587,new('a',64),new('b',64)))};
  }
  var context=new SubmissionWorkflowStepContext(root,new(1,new('e',64),release,SubmissionWorkflowStage.PrepareReview,SubmissionWorkflowStatus.Running,"synthetic-document-operation",null,[],DateTimeOffset.UtcNow));
  var stages=new SubmissionAutomationStages(settings,new('f',64));
  if(phase is "sign" or "deliver") {
   context=context with{Checkpoint=context.Checkpoint with{Stage=phase=="sign"?SubmissionWorkflowStage.SignReview:SubmissionWorkflowStage.Deliver,OperationId="synthetic-"+phase}};
   if(phase=="sign") {
    string installedPath=Path.Combine(root+"-authority","protected-worker.json");
    AutomationFiles.Write(installedPath,new SubmissionAutomationProtectedWorker(1,[root],[release.Repository],tools,settings.Protected!));
    var protectedStages=SubmissionAutomationStages.CreateProtected(settings,new('f',64),installedPath,Hash(installedPath));
    var signed=File.Exists(P("signing-intent.json"))?await protectedStages.RecoverAsync(context,default):await protectedStages.ExecuteAsync(context,default);
    Console.WriteLine(JsonSerializer.Serialize(signed));return signed.Status==SubmissionWorkflowStatus.Completed?0:signed.Status==SubmissionWorkflowStatus.Waiting?4:3;
   }
   var delivered=await AutomationDelivery.Advance(context,settings,File.Exists(P("delivery-operation.json")),default,dispatch:async(op,t)=> {
    var plan=op.Complete??throw new InvalidDataException("Synthetic test requires complete mode.");
    return await SubmissionDelivery.ExecuteAuthorizedAsync(P("delivery-journal"),plan,P("delivery-prepared/delivery/"+plan.PackageFileName),P("delivery-prepared/delivery/"+plan.SignedFormFileName),
     new FakeTransport(P("synthetic-provider-calls.txt")),(step,ct)=>SubmissionDeliveryRevalidation.CheckAsync(op.Revalidation!,plan,step,ct),cancellationToken:t);
   });
   if(delivered.Status==SubmissionWorkflowStatus.Completed)AutomationDelivery.Retain(context);
   Console.WriteLine(JsonSerializer.Serialize(delivered));return delivered.Status==SubmissionWorkflowStatus.Completed?0:delivered.Status==SubmissionWorkflowStatus.Waiting?4:3;
  }
  var result=await stages.ExecuteAsync(context,default);
  if(result.Status!=SubmissionWorkflowStatus.Completed)throw new InvalidDataException("Synthetic automation review did not finish: "+result.ReasonCode);
  var recovered=await stages.RecoverAsync(context,default);
  if(result!=recovered)throw new InvalidDataException("Recovery changed the retained document receipt.");
  if(phase=="review") {Console.WriteLine("{\"SyntheticReviewPrepared\":true}");return 0;}
  var stop=await stages.ExecuteAsync(context with {Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.SignReview,
   CompletedStages=new(){[SubmissionWorkflowStage.PrepareReview]=result.Receipt!}}},default);
  if(stop.ReasonCode!="rehearsal-ready-for-review")throw new InvalidDataException("Rehearsal did not stop before signing.");
  Console.WriteLine(JsonSerializer.Serialize(new{ReviewPrepared=true,RecoveryPreserved=true,RehearsalStopped=true,SyntheticOnly=true}));return 0;
 }
 private sealed class FakeTransport(string record):ISubmissionDeliveryTransport {
  public async Task<SubmissionUploadReceipt> UploadAsync(Stream stream,string name,CancellationToken t) {
   _=await SHA256.HashDataAsync(stream,t);File.AppendAllText(record,"upload\n");return new("https://uploader.crestron.com/download.php?file="+new string('a',32),"SYNTHETIC-UPLOAD");
  }
  public async Task<SubmissionMailReceipt> SendAsync(SubmissionDeliveryPlan plan,SubmissionUploadReceipt upload,Stream form,string id,CancellationToken t) {
   if(!File.ReadAllText(record).StartsWith("upload\n",StringComparison.Ordinal)||!upload.DownloadUrl.Contains("download.php"))throw new InvalidDataException("Email must follow confirmed upload.");
   _=await SHA256.HashDataAsync(form,t);File.AppendAllText(record,"email\n");return new("SYNTHETIC-EMAIL");
  }
 }
}
