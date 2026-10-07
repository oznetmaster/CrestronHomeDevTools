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
  bool rehearsal=phase?.StartsWith("rehearsal-",StringComparison.Ordinal)==true;
  if(rehearsal)phase=phase!["rehearsal-".Length..];
  string P(string name)=>Path.Combine(root,name);
  string Hash(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
  SubmissionAutomationInput Input(string name)=>new(P(name),Hash(P(name)));
  var release=new SubmissionWorkflowRelease("synthetic/never-submitted",1,"synthetic",new('a',40),Hash(P("candidate.pkg")),new('b',64),new('c',64));
  var tools=new SubmissionAutomationConsole(consoleDirectory,Directory.GetFiles(consoleDirectory,"*",SearchOption.AllDirectories)
   .Select(p=>new SubmissionEvidenceFile(Path.GetRelativePath(consoleDirectory,p).Replace('\\','/'),Hash(p))).ToArray());
  var review=new SubmissionAutomationReviewPlan(Input("policy.json"),Input("template.pdf"),Input("inventory.json"),Input("mapping.json"),tools,
   "SYNTHETIC AUTOMATION REHEARSAL - NEVER SUBMITTED","Synthetic Developer",["nunit/observations.json"],Declarations:File.Exists(P("declarations.json"))?Input("declarations.json"):null);
  var nunit=new WorkflowPlan {Host="synthetic.invalid",CertificateSha256=new('d',64),SshFingerprint="not-used",SourceRoots=[root],LocalTests=[],
   TestPackage=new("unused.csproj","unused.pkg","Synthetic",1),ProcessorSuites=[]};
  bool android=Directory.Exists(P("nunit/AndroidUI"));
  if(android)nunit=nunit with{AndroidTests=new("unused.csproj","unused.json")};
  var settings=new SubmissionAutomationSettings(1,root,release,root,new("75b3457d-70f3-4c02-940e-22fbd970ef94","1.0.000.0000",PortalSubmissionKind.NewDriver,"Example","support@example.org"),
   "no-credentials-in-this-test",nunit,Review:review);
  if(phase!=null) {
   string authority=root+"-authority";
   settings=settings with{Mode=rehearsal?SubmissionAutomationMode.Rehearsal:SubmissionAutomationMode.Submit,Protected=new(Path.Combine(authority,"bindings.json"),
    new(Path.Combine(authority,"sign.json"),Path.Combine(authority,"sign.sha256")),new(Path.Combine(authority,"deliver.json"),Path.Combine(authority,"deliver.sha256")),
    new("fixture@example.org","smtp.example.invalid",587,new('a',64),new('b',64)){RehearsalRecipient=rehearsal?"rehearsal@example.org":null,GapSummary=review.Declarations!=null?"One-hour rehearsal; the required 24-hour endurance is not completed.":null})
    {Environment=rehearsal?SubmissionDeliveryEnvironment.Rehearsal:SubmissionDeliveryEnvironment.Production}};
  }
  // An explicit synthetic phase-two duration is independent of the observed timestamps.
  // A gap declaration alone must never invent a passing endurance selection.
  if(File.Exists(P("synthetic-endurance-minutes.json"))) {
   int minutes=AutomationFiles.Read<int>(P("synthetic-endurance-minutes.json"));
   if(minutes<=0)throw new InvalidDataException("Synthetic endurance selection must be positive.");
   var identity=new SubmissionEvidenceIdentity(release.PackageSha256,release.SourceCommit,review.Policy.Sha256,review.Template.Sha256);
   var requirement=AutomationFiles.Read<SubmissionEvidencePolicy>(review.Policy.Path).Requirements.Single(r=>r.Id=="second.duration")
    with{MinimumDuration=TimeSpan.FromMinutes(minutes)};
   var plan=new SubmissionEndurancePlan(identity,requirement,"synthetic.invalid","synthetic-installation","synthetic-reservation","synthetic-producer",TimeSpan.FromMinutes(5),TimeSpan.FromSeconds(30));
   settings=settings with{Endurance=new(plan,new("synthetic-never-executed","synthetic-never-read"),new(root,"synthetic-never-executed",[]))};
  }
  var context=new SubmissionWorkflowStepContext(root,new(1,new('e',64),release,SubmissionWorkflowStage.PrepareReview,SubmissionWorkflowStatus.Running,"synthetic-document-operation",null,[],DateTimeOffset.UtcNow));
  if(android)context.Checkpoint.CompletedStages.Add(SubmissionWorkflowStage.WindowsTests,new("windows-tests.json",Hash(P("windows-tests.json"))));
  var stages=new SubmissionAutomationStages(settings,new('f',64));
  if(phase is "sign" or "deliver") {
   context=context with{Checkpoint=context.Checkpoint with{Stage=phase=="sign"?SubmissionWorkflowStage.SignReview:SubmissionWorkflowStage.Deliver,OperationId="synthetic-"+phase}};
   if(phase=="sign") {
    string installedPath=Path.Combine(root+"-authority","protected-worker.json");
    var production=settings.Protected! with{Environment=SubmissionDeliveryEnvironment.Production,Delivery=settings.Protected!.Delivery! with{RehearsalRecipient=null}};
    AutomationFiles.Write(installedPath,new SubmissionAutomationProtectedWorker(1,[root],[release.Repository],tools,production){RehearsalPlan=rehearsal?settings.Protected:null});
    var protectedStages=SubmissionAutomationStages.CreateProtected(settings,new('f',64),installedPath,Hash(installedPath));
    var signed=File.Exists(P("signing-intent.json"))?await protectedStages.RecoverAsync(context,default):await protectedStages.ExecuteAsync(context,default);
    Console.WriteLine(JsonSerializer.Serialize(signed));return signed.Status==SubmissionWorkflowStatus.Completed?0:signed.Status==SubmissionWorkflowStatus.Waiting?4:3;
   }
   var delivered=await AutomationDelivery.Advance(context,settings,File.Exists(P("delivery-operation.json")),default,dispatch:async(op,t)=> {
    if(op.Qualified is {} qualified) {
     if(!rehearsal)throw new InvalidDataException("Qualified probe uses test SMTP only.");
     var archive=Directory.CreateDirectory(P("rehearsal-upload")).FullName;
     var mailer=new SubmissionSmtpMailer("smtp.example.invalid",587,"fixture@example.org",new System.Net.NetworkCredential("synthetic","synthetic"),
      P("mail-receipts"),TimeSpan.FromSeconds(30),()=>new SyntheticSmtp(P("synthetic-provider-calls.txt")));
     return (await SubmissionReviewRequestDelivery.ExecuteAsync(P("signed-review"),qualified,settings.Protected!.DeliveryApproval.DocumentPath,
      qualified.AuthorizationSha256,P("delivery-journal"),P("delivery-attempts"),new SubmissionRehearsalReviewMailTransport(archive,qualified,mailer),cancellationToken:t)).Delivery;
    }
    var plan=op.Complete??throw new InvalidDataException("Synthetic test requires a delivery plan.");
    ISubmissionDeliveryTransport transport=new FakeTransport(P("synthetic-provider-calls.txt"));
    if(rehearsal) {
     var archive=Directory.CreateDirectory(P("rehearsal-upload")).FullName;
     var mailer=new SubmissionSmtpMailer("smtp.example.invalid",587,"fixture@example.org",new System.Net.NetworkCredential("synthetic","synthetic"),
      P("mail-receipts"),TimeSpan.FromSeconds(30),()=>new SyntheticSmtp(P("synthetic-provider-calls.txt")));
     transport=new SubmissionRehearsalMailTransport(archive,plan,mailer);
    }
    return await SubmissionDelivery.ExecuteAuthorizedAsync(P("delivery-journal"),plan,P("delivery-prepared/delivery/"+plan.PackageFileName),P("delivery-prepared/delivery/"+plan.SignedFormFileName),
     transport,(step,ct)=>SubmissionDeliveryRevalidation.CheckAsync(op.Revalidation!,plan,step,ct),cancellationToken:t);
   });
   if(delivered.Status==SubmissionWorkflowStatus.Completed)AutomationDelivery.Retain(context,settings.Mode);
   Console.WriteLine(JsonSerializer.Serialize(delivered));return delivered.Status==SubmissionWorkflowStatus.Completed?0:delivered.Status==SubmissionWorkflowStatus.Waiting?4:3;
  }
  // Exercise the real phase-two finalizer with synthetic completed producer receipts.
  // No equipment callback or credential lookup is permitted by this probe.
  foreach(var stage in new[]{SubmissionWorkflowStage.ValidateCandidate,SubmissionWorkflowStage.WindowsTests,SubmissionWorkflowStage.ProcessorTests,
   SubmissionWorkflowStage.AppTests,SubmissionWorkflowStage.Endurance}) {
   if(context.Checkpoint.CompletedStages.ContainsKey(stage))continue;
   string name="synthetic-"+stage+".json";AutomationFiles.Write(P(name),new{SyntheticOnly=true,Stage=stage});
   context.Checkpoint.CompletedStages.Add(stage,new(name,Hash(P(name))));
  }
  var finalContext=context with{Checkpoint=context.Checkpoint with{SchemaVersion=2,Stage=SubmissionWorkflowStage.FinalizeTests,OperationId="synthetic-final-tests"}};
  var finalized=await AutomationFinalTests.Advance(finalContext,settings,new('f',64),false,
   (_,_,_,_)=>throw new InvalidOperationException("No equipment action expected"),_=>throw new InvalidOperationException("No credentials expected"),default);
  if(finalized.Status!=SubmissionWorkflowStatus.Completed)throw new InvalidDataException("Synthetic final tests did not finish: "+finalized.ReasonCode);
  finalContext.Checkpoint.CompletedStages.Add(SubmissionWorkflowStage.FinalizeTests,finalized.Receipt!);
  AutomationFinalTests.VerifyRetained(finalContext,settings,new('f',64));
  var reused=await AutomationFinalTests.Advance(finalContext,settings,new('f',64),true,
   (_,_,_,_)=>throw new InvalidOperationException("No equipment replay expected"),_=>throw new InvalidOperationException("No credentials expected"),default);
  if(reused.Receipt!=finalized.Receipt)throw new InvalidDataException("Finalized test inputs were recreated.");
  context=finalContext with{Checkpoint=finalContext.Checkpoint with{Stage=SubmissionWorkflowStage.PrepareReview,OperationId="synthetic-document-operation"}};
  // This is phase-three input preparation, after the test boundary completed.
  var published=await AutomationReviewInputs.Seal(context,settings,default);
  if(published.Status!=SubmissionWorkflowStatus.Completed)throw new InvalidDataException("Synthetic review snapshot failed: "+published.ReasonCode);
  if(phase=="portable") {
   Directory.Move(P("nunit"),P("original-nunit"));
   Directory.Move(P("review-inputs"),P("original-preparation"));
  }
  var result=await AutomationReview.Advance(context,settings,false,default);
  if(result.Status!=SubmissionWorkflowStatus.Completed)throw new InvalidDataException("Synthetic automation review did not finish: "+result.ReasonCode);
  var recovered=await AutomationReview.Advance(context,settings,true,default);
  if(result!=recovered)throw new InvalidDataException("Recovery changed the retained document receipt.");
  if(phase=="review") {Console.WriteLine("{\"SyntheticReviewPrepared\":true}");return 0;}
  var stop=await stages.ExecuteAsync(context with {Checkpoint=context.Checkpoint with{Stage=SubmissionWorkflowStage.SignReview,
   CompletedStages=new(){[SubmissionWorkflowStage.PrepareReview]=result.Receipt!}}},default);
  if(stop.ReasonCode!="worker-role-handoff")throw new InvalidDataException("Evidence worker did not hand off signing.");
  Console.WriteLine(JsonSerializer.Serialize(new{ReviewPrepared=true,RecoveryPreserved=true,ProtectedHandoff=true,SyntheticOnly=true}));return 0;
 }
 private sealed class SyntheticSmtp(string record):ISubmissionSmtpSession {
  public Task ConnectAsync(string host,int port,System.Net.NetworkCredential credential,CancellationToken token)=>Task.CompletedTask;
  public Task<string> SendAsync(MimeKit.MimeMessage message,CancellationToken token) {
   if(message.To.Mailboxes.Single().Address!="rehearsal@example.org" || message.Subject!="[REHEARSAL] Driver Submission Package")
    throw new InvalidDataException("Rehearsal correspondence was not isolated.");
   File.AppendAllText(record,"email\n");return Task.FromResult("250 synthetic acceptance; no network used");
  }
  public void Dispose(){}
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
