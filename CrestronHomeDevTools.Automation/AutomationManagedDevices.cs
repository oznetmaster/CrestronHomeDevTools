// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Automation;

/// <summary>Exact physical managed identity and installer inputs; keep concrete plans private.</summary>
public sealed record SubmissionManagedChild(string Alias, string ManagedDeviceId, string Name, string Model,
 int LocationId, Dictionary<string,string> Configuration, string[] RequiredCommands)
{
 public SubmissionManagedConfigurationStep[]? ConfigurationSteps { get; init; }
}
public sealed record SubmissionManagedConfigurationStep(string Id,Dictionary<string,string> Values);
public sealed record SubmissionManagedDevicesPlan(SubmissionManagedChild[] Children, int TimeoutSeconds = 120);
public sealed record SubmissionManagedChildBinding(string Alias, ManagedDeviceRequest Request, int DeviceId, int? NativeLoadId);

internal static class AutomationManagedDevices
{
 internal sealed record Operation(string Host,string CertificateSha256,string SshFingerprint,
  DriverInstanceReady Parent,SubmissionManagedDevicesPlan Plan);
 internal sealed record Outcome(bool Ready,bool ReservationReleased,SubmissionManagedChildBinding[] Bindings);
 private sealed record Intent(string InputSha256,string OperationId,Operation Request);
 private sealed record Receipt(string InputSha256,SubmissionWorkflowReceipt[] Files);
 internal const string ReceiptName="managed-devices.json";

 internal static void Validate(SubmissionAutomationSettings settings) {
  if(settings.ManagedDevices is not {} plan)return;
  if(settings.NUnit.ActualDriver==null || settings.NUnit.ReleaseCandidate==null || settings.InstalledAppTests==null || settings.NUnit.AndroidTests!=null)
   throw new InvalidDataException("Persistent managed children require release deployment and separate installed-app tests.");
  ValidatePlan(plan);
  foreach(var fixture in new[]{settings.InstalledAppFixtureSettings,settings.PostEnduranceFixtureSettings})
   if(fixture is {} value)ValidateReferences(value,plan);
 }
 internal static bool IsReference(string text)=>System.Text.RegularExpressions.Regex.IsMatch(text,@"\A\$\{managed:[A-Za-z0-9_-]{1,64}:(deviceId|nativeLoadId)\}\z");
 internal static void ValidateReferences(JsonElement value,SubmissionManagedDevicesPlan plan) {
  if(value.ValueKind==JsonValueKind.String && value.GetString() is {} text && text.Contains("${managed:",StringComparison.Ordinal) &&
   (!IsReference(text) || !plan.Children.Any(c=>c.Alias==text.Split(':')[1])))throw new InvalidDataException("Unknown or malformed managed-child reference.");
  if(value.ValueKind==JsonValueKind.Object)foreach(var property in value.EnumerateObject())ValidateReferences(property.Value,plan);
  if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())ValidateReferences(item,plan);
 }
 internal static void ValidatePlan(SubmissionManagedDevicesPlan plan) {
  if(plan.TimeoutSeconds is < 1 or > 600 || plan.Children is not {Length: >0 and <=32})
   throw new InvalidDataException("Provide one to 32 persistent children and a bounded setup timeout.");
  foreach(var child in plan.Children) {
   if(child==null || string.IsNullOrWhiteSpace(child.Alias) || child.Alias.Length>64 ||
    child.Alias.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='_' && c!='-') || string.IsNullOrWhiteSpace(child.ManagedDeviceId) ||
    string.IsNullOrWhiteSpace(child.Name) || child.Name.Length>32 || string.IsNullOrWhiteSpace(child.Model) || child.LocationId<=0 ||
    child.Configuration==null || child.Configuration.Any(p=>string.IsNullOrWhiteSpace(p.Key) || p.Value==null) ||
    child.RequiredCommands==null || child.RequiredCommands.Any(string.IsNullOrWhiteSpace))
    throw new InvalidDataException("Persistent children require exact identities, simple aliases and explicit configuration/control expectations.");
   if(child.ConfigurationSteps is {} steps && (child.Configuration.Count!=0 || steps.Length is <1 or >16 ||
    steps.Any(s=>s==null || string.IsNullOrWhiteSpace(s.Id) || s.Values==null || s.Values.Any(p=>string.IsNullOrWhiteSpace(p.Key) || p.Value==null)) ||
    steps.Select(s=>s.Id).Distinct(StringComparer.Ordinal).Count()!=steps.Length))
    throw new InvalidDataException("Select flat configuration or ordered unique wizard steps, not both.");
  }
  if(plan.Children.Select(c=>c.Alias).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=plan.Children.Length ||
   plan.Children.Select(c=>c.ManagedDeviceId).Distinct(StringComparer.Ordinal).Count()!=plan.Children.Length ||
   plan.Children.Select(c=>(c.Name,c.LocationId)).Distinct().Count()!=plan.Children.Length)
   throw new InvalidDataException("Persistent managed child selections must be distinct.");
 }

 internal static async Task<SubmissionWorkflowStepResult> Advance(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings,
  Func<string,NetworkCredential> credentials,CancellationToken token,
  Func<Operation,NetworkCredential,string,CancellationToken,Task<Outcome>>? run=null) {
  Validate(settings);
  if(settings.ManagedDevices==null)return new(SubmissionWorkflowStatus.Completed);
  var deployment=AutomationDeploymentEvidence.Read(c,settings,beforeAppTests:true);
  return await AdvanceCore(c,new(settings.NUnit.Host,settings.NUnit.CertificateSha256,settings.NUnit.SshFingerprint,
   deployment.Installed,settings.ManagedDevices),credentials,run??Run,token);
 }

 internal static async Task<SubmissionWorkflowStepResult> AdvanceCore(SubmissionWorkflowStepContext c,Operation operation,
  Func<string,NetworkCredential> credentials,Func<Operation,NetworkCredential,string,CancellationToken,Task<Outcome>> run,CancellationToken token) {
  ValidatePlan(operation.Plan);
  string folder=Path.Combine(c.RunDirectory,"managed-devices"),intentPath=Path.Combine(folder,"intent.json"),
   resultPath=Path.Combine(folder,"operation","result.json");
  var expected=new Intent(c.Checkpoint.InputSha256,c.Checkpoint.OperationId??throw new InvalidDataException("Missing operation identity."),operation);
  if(Directory.Exists(folder)) {
   if(!File.Exists(intentPath))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-managed-child-setup");
   if(JsonSerializer.Serialize(AutomationFiles.Read<Intent>(intentPath))!=JsonSerializer.Serialize(expected))
    throw new InvalidDataException("Managed-child setup belongs to another plan or attempt.");
   if(File.Exists(Path.Combine(c.RunDirectory,ReceiptName)))VerifyRetained(c);
   if(!File.Exists(resultPath))return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-managed-child-setup-and-lease");
  } else {
   Directory.CreateDirectory(folder);AutomationFiles.Write(intentPath,expected);
   var outcome=await run(operation,credentials(operation.Host),Path.Combine(folder,"operation"),token);
   if(!File.Exists(resultPath) || JsonSerializer.Serialize(outcome)!=JsonSerializer.Serialize(AutomationFiles.Read<Outcome>(resultPath)))
    throw new InvalidDataException("Managed-child producer and retained outcome differ.");
  }
  var result=AutomationFiles.Read<Outcome>(resultPath);
  if(!result.ReservationReleased)return new(SubmissionWorkflowStatus.OutcomeUnknown,ReasonCode:"inspect-managed-child-setup-and-lease");
  if(!result.Ready)return new(SubmissionWorkflowStatus.Failed,ReasonCode:"managed-child-configuration-or-controls-unavailable");
  ValidateBindings(operation,result.Bindings);
  return AutomationFiles.Complete(c,ReceiptName,new Receipt(c.Checkpoint.InputSha256,Inventory(c.RunDirectory)));
 }

 internal static void ValidateBindings(Operation operation,SubmissionManagedChildBinding[] bindings) {
  if(bindings.Length!=operation.Plan.Children.Length || bindings.Any(b=>b.DeviceId<=0 || b.NativeLoadId is <=0) ||
   bindings.SelectMany(b=>b.NativeLoadId is {} id?new[]{b.DeviceId,id}:new[]{b.DeviceId}).Distinct().Count()!=bindings.Sum(b=>b.NativeLoadId==null?1:2) ||
   bindings.Any(b=>b.DeviceId==operation.Parent.DeviceId || b.NativeLoadId==operation.Parent.DeviceId))
   throw new InvalidDataException("Managed bindings do not identify distinct created children.");
  for(int i=0;i<bindings.Length;i++) {
   var child=operation.Plan.Children[i];var binding=bindings[i];
   if(binding.Alias!=child.Alias || binding.Request!=Request(operation.Parent,child))
    throw new InvalidDataException("Managed bindings differ from the reviewed selection and actual deployment.");
  }
 }
 private static ManagedDeviceRequest Request(DriverInstanceReady parent,SubmissionManagedChild child)=>
  new(parent.DeviceId,parent.Model,parent.Version,child.ManagedDeviceId,child.Name,child.Model,child.LocationId);

 private static async Task<Outcome> Run(Operation operation,NetworkCredential credential,string folder,CancellationToken token) {
  Directory.CreateDirectory(folder);
  string owner=Guid.NewGuid().ToString("N");AutomationFiles.Write(Path.Combine(folder,"lease-owner.json"),new{Owner=owner,operation.Host});
  using var lease=await ProcessorOperationLease.AcquireAsync(operation.Host,credential,operation.SshFingerprint,owner,token);
  var options=new ProcessorConnectionOptions{Host=operation.Host,CertificateSha256=operation.CertificateSha256};
  async Task Verify(CancellationToken ct)=>await lease.VerifyAfterReconnectAsync(operation.Host,ct);
  async Task<ConfigurationClient> Open(CancellationToken ct)=>await ConfigurationClient.ConnectAsync(options,credential,ct);
  var applied=new HashSet<int>();
  var result=await Setup(operation,folder,Verify,
   async(request,journal,ct)=>{await using var api=await Open(ct);return await ManagedDeviceCommissioning.CommissionAsync(api,request,journal,TimeSpan.FromSeconds(operation.Plan.TimeoutSeconds),ct);},
   async(binding,input,ct)=>{await using var api=await Open(ct);var configured=await DriverConfiguration.ConfigureAsync(api,
    new(binding.DeviceId,binding.Request.ChildModel,binding.Request.ParentVersion,"Installed"),DriverConfiguration.ReadInputs(input),TimeSpan.FromSeconds(operation.Plan.TimeoutSeconds),ct);
    if(configured.Changed && configured.Configured)applied.Add(binding.DeviceId);return configured;},
   async(journal,commands,ct)=>{
    await using var api=await Open(ct);
    var timer=System.Diagnostics.Stopwatch.StartNew();
    while(true) {
     var ready=await ManagedDeviceCommissioning.ObserveCreatedAsync(api,journal,ct);
     var child=await api.GetDeviceAsync(ready.NativeLoadId??ready.DeviceId,ct);
     var specification=operation.Plan.Children.Single(c=>c.Alias==Path.GetFileName(journal));
     var expected=specification.ConfigurationSteps is {} steps ? steps.SelectMany(s=>s.Values).GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Last().Value) : specification.Configuration;
     bool configuration=expected.Count==0 || applied.Contains(ready.DeviceId) ||
      ConfigurationMatches(expected,await DriverConfigurationInspection.GetAsync(api,ready.DeviceId,ct));
     bool controls=configuration && child!=null && commands.All(child.Commands.Contains);
     if(ready.State=="Ready" && controls || timer.Elapsed>=TimeSpan.FromSeconds(operation.Plan.TimeoutSeconds))return (ready,controls);
     await Task.Delay(250,ct);
    }
   },token);
  // A thrown/uncertain operation retains the reservation and original journals. No automatic replay or cleanup.
  using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(45));await Verify(cleanup.Token);await lease.ReleaseAsync(cleanup.Token);
  result=result with{ReservationReleased=true};AutomationFiles.Write(Path.Combine(folder,"result.json"),result);return result;
 }

 internal static async Task<Outcome> Setup(Operation operation,string folder,Func<CancellationToken,Task> verify,
  Func<ManagedDeviceRequest,string,CancellationToken,Task<ManagedDeviceResult>> commission,
  Func<SubmissionManagedChildBinding,string,CancellationToken,Task<DriverConfigurationResult>> configure,
  Func<string,string[],CancellationToken,Task<(ManagedDeviceResult Readiness,bool Controls)>> observe,CancellationToken token) {
  var bindings=new List<SubmissionManagedChildBinding>();
  foreach(var child in operation.Plan.Children) {
   await verify(token);
   var request=Request(operation.Parent,child);string journal=Path.Combine(folder,child.Alias);
   var created=await commission(request,journal,token);
   if(created.DeviceId<=0 || created.DeviceId==operation.Parent.DeviceId || bindings.Any(b=>b.DeviceId==created.DeviceId || b.NativeLoadId==created.DeviceId))
    throw new InvalidDataException("Commissioning returned an invalid or reused child identity.");
   var binding=new SubmissionManagedChildBinding(child.Alias,request,created.DeviceId,created.NativeLoadId);
   AutomationFiles.Write(Path.Combine(folder,child.Alias+"-created.json"),binding);
   if(created.State=="ConfigurationRequired") {
    if(child.Configuration.Count==0 && child.ConfigurationSteps==null)return new(false,false,[..bindings,binding]);
    string input=Path.Combine(folder,child.Alias+"-configuration.json");
    if(child.ConfigurationSteps is {} steps)AutomationFiles.Write(input,new{steps=steps.Select(s=>new{id=s.Id,values=s.Values}).ToArray()});
    else AutomationFiles.Write(input,child.Configuration);
    await verify(token);AutomationFiles.Write(Path.Combine(folder,child.Alias+"-configuration-intent.json"),binding);
    var configured=await configure(binding,input,token);AutomationFiles.Write(Path.Combine(folder,child.Alias+"-configuration-result.json"),configured);
    if(configured.DeviceId!=binding.DeviceId || !configured.Configured)return new(false,false,[..bindings,binding]);
   } else if(created.State!="Ready")return new(false,false,[..bindings,binding]);
   await verify(token);
   var observation=await observe(journal,child.RequiredCommands,token);
   AutomationFiles.Write(Path.Combine(folder,child.Alias+"-readiness.json"),new{Result=observation.Readiness,observation.Controls});
   if(observation.Readiness.DeviceId!=created.DeviceId)throw new InvalidDataException("Created child identity changed during configuration.");
   if(created.NativeLoadId is {} load && observation.Readiness.NativeLoadId!=load)throw new InvalidDataException("Created native load changed during setup.");
   binding=binding with{NativeLoadId=observation.Readiness.NativeLoadId};bindings.Add(binding);
   if(observation.Readiness.State!="Ready" || !observation.Controls)return new(false,false,[..bindings]);
  }
  ValidateBindings(operation,[..bindings]);await verify(token);
  return new(true,false,[..bindings]);
 }

 internal static bool ConfigurationMatches(Dictionary<string,string> expected,DriverConfigurationSnapshot snapshot) {
  foreach(var pair in expected) {
   var items=snapshot.Items.Where(i=>i.Id==pair.Key).ToArray();
   if(items.Length!=1 || items[0].Masked || !items[0].HasCurrentValue || items[0].CurrentValue is not {} value)return false;
   string actual=value.ValueKind==JsonValueKind.String?value.GetString()!:value.GetRawText();
   if(!string.Equals(actual,pair.Value,value.ValueKind is JsonValueKind.True or JsonValueKind.False?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))return false;
  }
  return true;
 }

 private static SubmissionWorkflowReceipt[] Inventory(string root) {
  var pending=new Stack<DirectoryInfo>();pending.Push(new(Path.Combine(root,"managed-devices")));
  var files=new List<SubmissionWorkflowReceipt>();int count=0;
  while(pending.Count>0) {
   var dir=pending.Pop();if((dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Managed evidence contains a link.");
   foreach(var entry in dir.EnumerateFileSystemInfos()) {
    if(++count>4096 || (entry.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Managed evidence exceeds its bound or contains a link.");
    if(entry is DirectoryInfo child)pending.Push(child);
    else files.Add(new(Path.GetRelativePath(root,entry.FullName).Replace('\\','/'),AutomationFiles.Hash(entry.FullName)));
   }
  }
  return files.OrderBy(f=>f.RelativePath,StringComparer.Ordinal).ToArray();
 }
 internal static SubmissionManagedChildBinding[] VerifyRetained(SubmissionWorkflowStepContext c) {
  var receipt=AutomationFiles.Read<Receipt>(Path.Combine(c.RunDirectory,ReceiptName));
  if(receipt.InputSha256!=c.Checkpoint.InputSha256 || !receipt.Files.SequenceEqual(Inventory(c.RunDirectory)))
   throw new InvalidDataException("Retained managed-child setup changed.");
  var result=AutomationFiles.Read<Outcome>(Path.Combine(c.RunDirectory,"managed-devices","operation","result.json"));
  if(!result.Ready || !result.ReservationReleased)throw new InvalidDataException("Managed-child setup is incomplete.");
  var intent=AutomationFiles.Read<Intent>(Path.Combine(c.RunDirectory,"managed-devices","intent.json"));
  ValidateBindings(intent.Request,result.Bindings);return result.Bindings;
 }

 internal static SubmissionAutomationSettings BindApp(SubmissionWorkflowStepContext c,SubmissionAutomationSettings settings) {
  if(settings.ManagedDevices==null)return settings;
  var bindings=VerifyRetained(c);
  var deployment=AutomationDeploymentEvidence.Read(c,settings,beforeAppTests:true);
  var plan=settings.InstalledAppTests!;var actual=settings.NUnit.ActualDriver!;
  if(plan.Target.Name!=actual.InstanceName || plan.Target.LocationId!=actual.LocationId || plan.Target.Model!=deployment.Installed.Model ||
   Version.Parse(plan.Target.Version)!=Version.Parse(deployment.Installed.Version))throw new InvalidDataException("App target differs from deployed platform.");
  return settings with{InstalledAppTests=plan with{Target=plan.Target with{DeviceId=deployment.Installed.DeviceId,CatalogueId=deployment.Imported.CatalogueId}},
   InstalledAppFixtureSettings=settings.InstalledAppFixtureSettings is {} fixture?RenderInputs(fixture,bindings):null};
 }

 // Exact numeric placeholders avoid accidental replacement of unrelated IDs or text.
 internal static JsonElement RenderInputs(JsonElement template,SubmissionManagedChildBinding[] bindings) {
  JsonNode? Replace(JsonNode? node) {
   if(node is JsonValue value && value.TryGetValue<string>(out var text) && text.Contains("${managed:",StringComparison.Ordinal)) {
    var parts=text.Split(':');
    if(parts.Length!=3 || parts[0]!="${managed" || !parts[2].EndsWith('}'))throw new InvalidDataException("Malformed managed-child placeholder.");
    var binding=bindings.SingleOrDefault(b=>b.Alias==parts[1])??throw new InvalidDataException("Unknown managed-child alias.");
    int id=parts[2] switch{"deviceId}"=>binding.DeviceId,"nativeLoadId}"=>binding.NativeLoadId??throw new InvalidDataException("The bound child has no native load."),_=>throw new InvalidDataException("Unknown managed-child identity field.")};
    return JsonValue.Create(id);
   }
   if(node is JsonObject obj)foreach(string key in obj.Select(p=>p.Key).ToArray())obj[key]=Replace(obj[key]?.DeepClone());
   if(node is JsonArray array)for(int i=0;i<array.Count;i++)array[i]=Replace(array[i]?.DeepClone());
   return node;
  }
  return JsonSerializer.SerializeToElement(Replace(JsonNode.Parse(template.GetRawText())));
 }
}
