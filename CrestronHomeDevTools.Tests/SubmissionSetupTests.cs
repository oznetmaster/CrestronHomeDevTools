// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using CrestronHomeDevTools.Automation;
using CrestronHomeNUnit.Workflow;

namespace CrestronHomeDevTools.Tests;

[TestFixture, SupportedOSPlatform ("windows")]
public sealed class SubmissionSetupTests
{
 private string _path = null!;
 private DevToolsPrivateStore _store = null!;
 [SetUp] public void Start ()
 {
  if (!OperatingSystem.IsWindows ()) { Assert.Ignore ("Windows encrypted setup profiles."); return; }
  _path = Path.Combine (TestContext.CurrentContext.WorkDirectory, "setup-test-" + Guid.NewGuid ().ToString ("N"));
  _store = DevToolsPrivateStore.Create (_path);
 }
 [TearDown] public void Finish () { if (_path != null && Directory.Exists (_path)) Directory.Delete (_path, true); }
 [Test] public void DraftEditRoundTripIsEncryptedAndRejectsStaleWriters ()
 {
  var first = _store.SaveSetupProfile ("personal", new SubmissionDeveloperProfile { DeveloperName = "PRIVATE-SENTINEL" });
  string path = Path.Combine (_path, "developer-personal.setup");
  Assert.That (Encoding.UTF8.GetString (File.ReadAllBytes (path)), Does.Not.Contain ("PRIVATE-SENTINEL"));
  first.Value.Company = "Updated company";
  var second = _store.SaveSetupProfile ("personal", first.Value, first.Revision);
  Assert.That (second.Revision, Is.EqualTo (2));
  Assert.That (DevToolsPrivateStore.Open (_path).LoadSetupProfile<SubmissionDeveloperProfile> ("personal").Value.Company, Is.EqualTo ("Updated company"));
  Assert.Throws<InvalidOperationException> (() => _store.SaveSetupProfile ("personal", new SubmissionDeveloperProfile (), first.Revision));
  Assert.That (_store.LoadSetupProfile<SubmissionDeveloperProfile> ("personal").Value.Company, Is.EqualTo ("Updated company"));
 }
 [Test] public void TamperedAndRenamedFilesCannotBeRead ()
 {
  _store.SaveSetupProfile ("one", new SubmissionDriverProfile ());
  string source = Path.Combine (_path, "driver-one.setup");
  File.Copy (source, Path.Combine (_path, "driver-two.setup"));
  Assert.Throws<System.IO.InvalidDataException> (() => _store.LoadSetupProfile<SubmissionDriverProfile> ("two"));
  File.WriteAllBytes (source, [1, 2, 3]);
  Assert.Throws<CryptographicException> (() => _store.LoadSetupProfile<SubmissionDriverProfile> ("one"));
  Assert.Throws<ArgumentException> (() => _store.SaveSetupProfile ("../outside", new SubmissionDriverProfile ()));
 }
 [Test] public void MissingInputsAreListedWithoutSecretsOrNetworkCalls ()
 {
  _store.SaveSetupProfile ("attempt", new SubmissionRunProfile { DeveloperProfile = "missing", DriverProfile = "missing", ProcessorCredential = "missing", ProcessorHost = "private-host.invalid" });
  var errors = _store.CheckSubmissionSetup ("attempt");
  Assert.That (errors, Has.Some.Contains ("Developer profile"));
  Assert.That (string.Join (" ", errors), Does.Not.Contain ("private-host.invalid"));
  Assert.Throws<InvalidOperationException> (() => _store.CreateSubmissionSetupSnapshot ("attempt", "snapshot"));
  Assert.That (_store.ListSetupProfiles<SubmissionSetupSnapshot> (), Is.Empty);
 }
 private void Ready (bool delivery = true)
 {
  if (delivery) {
  _store.SaveCredential ("mail", new (DevToolsCredentialPurpose.Smtp, "smtp.example.invalid", "user", "SYNTHETIC-SECRET", 587, "sender@example.invalid"));
  _store.SaveCredential ("uploader", new (DevToolsCredentialPurpose.Uploader, "uploader.crestron.com", "user", "SYNTHETIC-SECRET"));
  _store.SaveSignature ("signature", [1, 2, 3], ".png");
  }
  _store.SaveCredential ("processor", new (DevToolsCredentialPurpose.Processor, "processor.invalid", "user", "SYNTHETIC-SECRET", CertificateSha256: new string ('A', 64), SshFingerprint: "SHA256:synthetic-test-fingerprint"));
  _store.SaveSetupProfile ("developer", new SubmissionDeveloperProfile { DeveloperName = "Test Person", ContactEmail = "sender@example.invalid", SupportWebsite = "https://example.invalid/support/", SmtpHost = "smtp.example.invalid", SenderEmail = "sender@example.invalid", SmtpCredential = "mail", UploaderCredential = "uploader", SignatureEntry = "signature" });
  _store.SaveSetupProfile ("driver", new SubmissionDriverProfile { DriverName = "Test Driver", RepositoryUrl = "https://example.invalid/repo", Manufacturer = "Test", Models = "Test model", DeviceCategory = "Test", Connection = "IP", Description = "Description", Installation = "Installation", Configuration = "Settings", Usage = "Usage", Limitations = "None known", Troubleshooting = "Help", TestDeviceModels = "Test fixture", RealUseRestrictions = "Test only", PermittedTestChanges = "Reversible with restoration" });
  _store.SaveSetupProfile ("run", new SubmissionRunProfile { DeveloperProfile = "developer", DriverProfile = "driver", Version = "1.0.0", ManifestVersion = "1.0.0.0", SourceReference = "test-commit", PrivateWorkspace = _path, ProcessorResource = "test", ProcessorHost = "processor.invalid", ProcessorCredential = "processor", WindowsResource = "local", AndroidTarget = "test-emulator" });
 }
 [Test] public void RehearsalSnapshotNeedsNoDeliverySecretsAndCannotResolveThemEvenAfterTheyAreAdded()
 {
  Ready(delivery:false);
  var developer=_store.LoadSetupProfile<SubmissionDeveloperProfile>("developer");
  developer.Value.SmtpHost="";developer.Value.SmtpPort="";developer.Value.SenderEmail="";
  _store.SaveSetupProfile("developer",developer.Value,developer.Revision);
  Assert.That(_store.ListNames(),Is.EquivalentTo(new[]{"processor"}));
  Assert.That(_store.CheckSubmissionSetup("run",SubmissionSetupPurpose.Rehearsal),Is.Empty);
  Assert.That(_store.CheckSubmissionSetup("run"),Is.Not.Empty);
  Assert.Throws<InvalidOperationException>(()=>_store.CreateSubmissionSetupSnapshot("run","delivery"));
  var saved=_store.CreateSubmissionSetupSnapshot("run","practice",SubmissionSetupPurpose.Rehearsal);
  Assert.That(saved.Value.Purpose,Is.EqualTo(SubmissionSetupPurpose.Rehearsal));
  _store.SaveCredential("mail",new(DevToolsCredentialPurpose.Smtp,"smtp.example.invalid","user","secret",587,"sender@example.invalid"));
  _store.SaveCredential("uploader",new(DevToolsCredentialPurpose.Uploader,"uploader.crestron.com","user","secret"));
  _store.SaveSignature("signature",[1,2,3],".png");
  var bindings=DevToolsCredentialBindings.Read(_store.GetSubmissionSetupSnapshotPath("practice"));
  Assert.That(bindings.Processor,Is.EqualTo("processor"));
  Assert.That(bindings.Smtp,Is.Null);Assert.That(bindings.Uploader,Is.Null);Assert.That(bindings.Signature,Is.Null);
  Assert.Throws<InvalidOperationException>(()=>bindings.Resolve(DevToolsCredentialPurpose.Smtp,"smtp.example.invalid"));
  var drafts=_store.PrepareSubmissionSetupInputs("practice");
  var defaults=_store.GetSubmissionSetupOperationDefaults("practice");
  Assert.That(defaults.SmtpPort,Is.Zero);Assert.That(defaults.Sender,Is.Empty);
  Assert.That(File.Exists(drafts.HelpContentPath),Is.True);
 }
 [Test] public void RehearsalStillRequiresProcessorCredentialsAndTrustPins()
 {
  Ready(delivery:false);
  _store.SaveCredential("processor",new(DevToolsCredentialPurpose.Processor,"processor.invalid","user","secret"),replace:true);
  Assert.That(_store.CheckSubmissionSetup("run",SubmissionSetupPurpose.Rehearsal),Has.Some.Contains("trust pins"));
  Assert.Throws<InvalidOperationException>(()=>_store.CreateSubmissionSetupSnapshot("run","practice",SubmissionSetupPurpose.Rehearsal));
 }
 [Test] public void ConsoleRehearsalSelectionCreatesRestrictedSnapshotButDefaultSubmissionStillChecksDelivery()
 {
  Ready(delivery:false);using var output=new StringWriter();using var error=new StringWriter();
  Assert.That(SubmissionSetupCommand.Run(["check","--run","run","--purpose","rehearsal","--store",_path],output,error),Is.Zero);
  Assert.That(SubmissionSetupCommand.Run(["snapshot","--run","run","--name","practice","--purpose","rehearsal","--store",_path],output,error),Is.Zero);
  Assert.That(SubmissionSetupCommand.Run(["check","--run","run","--store",_path],output,error),Is.EqualTo(2));
  Assert.That(_store.LoadSetupProfile<SubmissionSetupSnapshot>("practice").Value.Purpose,Is.EqualTo(SubmissionSetupPurpose.Rehearsal));
 }
 private SubmissionAutomationSettings RehearsalTemplate(string host="processor.invalid",bool delivery=true)
 {
  Ready(delivery);
  var driver=_store.LoadSetupProfile<SubmissionDriverProfile>("driver");
  driver.Value.RepositoryUrl="https://github.com/example/driver";
  _store.SaveSetupProfile("driver",driver.Value,driver.Revision);
  var run=_store.LoadSetupProfile<SubmissionRunProfile>("run");
  run.Value.AutomationSettingsTemplate=Path.Combine(_path,"template.json");
  run.Value.AutomationToolingManifest=Path.Combine(_path,"tools.json");
  run.Value.AutomationPackageName="Driver_${version}.pkg";
  run.Value.AutomationNotBeforeUtc="2026-09-24T00:00:00Z";
  _store.SaveSetupProfile("run",run.Value,run.Revision);
  var input=new SubmissionAutomationInput(Path.Combine(_path,"review-input.json"),new('a',64));
  var settings=new SubmissionAutomationSettings(1,_path,new("old/repository",1,"old",new('a',40),new('b',64),new('c',64),new('d',64)),"${source}",
   new SubmissionPackageRequirements(Guid.NewGuid().ToString(),"${version4}",PortalSubmissionKind.NewDriver,"Fixture"){PublicSupportWebsite="https://example.invalid/repo"},Path.Combine(_path,"worker-bindings.json"),
   new WorkflowPlan{Host=host,CertificateSha256=new('e',64),SshFingerprint="fixture",SourceRoots=["${source}"],LocalTests=[],TestPackage=new("${source}/tests.csproj","${run}/tests.pkg","fixture",1),ProcessorSuites=[]},
   Mode:SubmissionAutomationMode.Submit,Review:new(input,input,input,input,new(_path,[]),"Old title","Old author",[]),
   Protected:new("PRIVATE-PROTECTED-STORE",new("sign","sign-pin"),new("send","send-pin")));
  AutomationFiles.Write(run.Value.AutomationSettingsTemplate,settings);
  File.WriteAllText(run.Value.AutomationToolingManifest,"{}");
  _store.CreateSubmissionSetupSnapshot("run","rehearsal",delivery?SubmissionSetupPurpose.Submission:SubmissionSetupPurpose.Rehearsal);
  return settings;
 }
 [Test] public void SavedSetupPreparesPinnedRehearsalConsumedByReleaseExpansionWithoutSecrets()
 {
  var original=RehearsalTemplate();
  var prepared=SubmissionAutomationSetup.PrepareRehearsal(_store,"rehearsal");
  var profile=AutomationFiles.Read<SubmissionAutomationReleaseProfiles>(prepared.ProfilesPath).Profiles.Single();
  string run=Path.Combine(_path,"actual-run"),source=Path.Combine(run,"source");
  var release=original.Release with{Repository="example/driver",Tag="v1.2.3"};
  var expanded=AutomationReleaseDiscovery.Expand(profile,release,run,source,"1.2.3");
  Assert.Multiple(()=> {
   Assert.That(profile.Repository,Is.EqualTo("example/driver"));
   Assert.That(expanded.Mode,Is.EqualTo(SubmissionAutomationMode.Rehearsal));
   Assert.That(expanded.Protected,Is.Null);
   Assert.That(expanded.Review!.Title,Is.EqualTo("Test Driver 1.2.3 - Crestron Home driver"));
   Assert.That(expanded.Review.Author,Is.EqualTo("Test Person"));
   Assert.That(expanded.CredentialBindings,Is.EqualTo(original.CredentialBindings));
   Assert.That(expanded.NUnit.Host,Is.EqualTo(original.NUnit.Host));
   Assert.That(expanded.PackageRequirements.PublicSupportWebsite,Is.EqualTo(original.PackageRequirements.PublicSupportWebsite));
   Assert.That(prepared.Configuration.AllStageBindingsPresent,Is.False);
   Assert.That(prepared.Configuration.MissingBindings,Does.Contain("Endurance"));
   Assert.That(AutomationFiles.Read<SubmissionAutomationRegistry>(prepared.RegistryPath).Entries,Is.Empty);
  });
  string exported=string.Join("\n",Directory.GetFiles(Path.GetDirectoryName(prepared.ProfilesPath)!).Select(File.ReadAllText));
  Assert.That(exported,Does.Not.Contain("SYNTHETIC-SECRET").And.Not.Contain("PRIVATE-PROTECTED-STORE"));
  Assert.That(File.Exists(Path.Combine(_path,"worker-bindings.json")),Is.False,"Preparation does not provision credentials.");
 }
 [Test] public void SavedRehearsalOnlySetupPreparesPublicReleaseProfileWithoutDeliveryProvisioning()
 {
  RehearsalTemplate(delivery:false);
  var prepared=SubmissionAutomationSetup.PrepareRehearsal(_store,"rehearsal");
  var profile=AutomationFiles.Read<SubmissionAutomationReleaseProfiles>(prepared.ProfilesPath).Profiles.Single();
  Assert.That(_store.ListNames(),Is.EquivalentTo(new[]{"processor"}));
  Assert.That(AutomationFiles.Read<SubmissionAutomationSettings>(profile.SettingsTemplate.Path).Protected,Is.Null);
  Assert.That(profile.Mode,Is.EqualTo(SubmissionAutomationMode.Rehearsal));
 }
 [Test] public void PreparedRehearsalRetainsCapturedBytesAndSnapshotFactsAfterEdits()
 {
  RehearsalTemplate();
  var first=SubmissionAutomationSetup.PrepareRehearsal(_store,"rehearsal");
  var profile=AutomationFiles.Read<SubmissionAutomationReleaseProfiles>(first.ProfilesPath).Profiles.Single();
  var developer=_store.LoadSetupProfile<SubmissionDeveloperProfile>("developer");developer.Value.DeveloperName="Edited";
  _store.SaveSetupProfile("developer",developer.Value,developer.Revision);
  File.WriteAllText(Path.Combine(_path,"tools.json"),"{\"changed\":true}");
  var second=SubmissionAutomationSetup.PrepareRehearsal(_store,"rehearsal");
  Assert.That(first.ProfilesPath,Is.Not.EqualTo(second.ProfilesPath));
  Assert.That(AutomationFiles.Hash(profile.ToolingManifest.Path),Is.EqualTo(profile.ToolingManifest.Sha256));
  Assert.That(AutomationFiles.Read<SubmissionAutomationSettings>(profile.SettingsTemplate.Path).Review!.Author,Is.EqualTo("Test Person"));
  Assert.That(File.ReadAllText(profile.ToolingManifest.Path),Is.EqualTo("{}"));
 }
 [Test] public void MismatchedProcessorTemplateRejectsBeforeCreatingRehearsalOutputs()
 {
  RehearsalTemplate("different-processor.invalid");
  Assert.Throws<InvalidDataException>(()=>SubmissionAutomationSetup.PrepareRehearsal(_store,"rehearsal"));
  Assert.That(Directory.GetDirectories(_path,"rehearsal-*"),Is.Empty);
 }
 [Test] public void RehearsalRequiresAnExplicitPublicationCutoff()
 {
  RehearsalTemplate();
  var run=_store.LoadSetupProfile<SubmissionRunProfile>("run");run.Value.AutomationNotBeforeUtc="2026-09-24";
  _store.SaveSetupProfile("run",run.Value,run.Revision);_store.CreateSubmissionSetupSnapshot("run","no-timezone");
  Assert.Throws<InvalidDataException>(()=>SubmissionAutomationSetup.PrepareRehearsal(_store,"no-timezone"));
  Assert.That(Directory.GetDirectories(_path,"rehearsal-*"),Is.Empty);
 }
 [Test] public void SnapshotRemainsUnchangedAfterEditingAllSourceProfiles ()
 {
  Ready ();
  Assert.That (_store.CheckSubmissionSetup ("run"), Is.Empty);
  var saved = _store.CreateSubmissionSetupSnapshot ("run", "first");
  string snapshotPath = Path.Combine (_path, "snapshot-first.setup");
  byte[] before = File.ReadAllBytes (snapshotPath);
  var developer = _store.LoadSetupProfile<SubmissionDeveloperProfile> ("developer"); developer.Value.SupportWebsite = "https://example.invalid/changed";
  _store.SaveSetupProfile ("developer", developer.Value, developer.Revision);
  var driver = _store.LoadSetupProfile<SubmissionDriverProfile> ("driver"); driver.Value.DriverName = "Changed";
  _store.SaveSetupProfile ("driver", driver.Value, driver.Revision);
  var run = _store.LoadSetupProfile<SubmissionRunProfile> ("run"); run.Value.Version = "2.0.0";
  _store.SaveSetupProfile ("run", run.Value, run.Revision);
  var retained = _store.LoadSetupProfile<SubmissionSetupSnapshot> ("first");
  Assert.That (retained.Value.SupportWebsite, Is.EqualTo ("https://example.invalid/support/"));
  Assert.That (retained.Value.Run.Value.Version, Is.EqualTo ("1.0.0"));
  Assert.That (retained.Value.Driver.Value.DriverName, Is.EqualTo ("Test Driver"));
  Assert.That (File.ReadAllBytes (snapshotPath), Is.EqualTo (before));
  Assert.Throws<InvalidOperationException> (() => _store.SaveSetupProfile ("first", saved.Value, saved.Revision));
  Assert.That (_store.GetSubmissionSetupBindings ("first").Smtp, Is.EqualTo ("mail"));
 }
 [Test] public void SavedCredentialEndpointMismatchIsCaughtBeforeReadiness ()
 {
  Ready ();
  _store.SaveCredential ("mail", new (DevToolsCredentialPurpose.Smtp, "different.invalid", "user", "PRIVATE-PASSWORD", 587, "sender@example.invalid"), true);
  string errors = string.Join (" ", _store.CheckSubmissionSetup ("run"));
  Assert.That (errors, Does.Contain ("mail credential").And.Not.Contain ("PRIVATE-PASSWORD"));
 }
 [Test] public void DriverSupportOverrideAndProfileNamesStaySeparate ()
 {
  Ready ();
  var driver = _store.LoadSetupProfile<SubmissionDriverProfile> ("driver"); driver.Value.SupportWebsite = "https://example.invalid/driver-support/";
  _store.SaveSetupProfile ("driver", driver.Value, driver.Revision);
  Assert.That (_store.CreateSubmissionSetupSnapshot ("run", "first").Value.SupportWebsite, Is.EqualTo (driver.Value.SupportWebsite));
  Assert.That (_store.ListSetupProfiles<SubmissionDeveloperProfile> (), Is.EqualTo (new[] { "developer" }));
  Assert.That (_store.ListNames (), Does.Not.Contain ("developer"));
 }

 [Test] public void ConsoleReadinessAndSnapshotUseSameEncryptedProfilesWithoutPrintingValues ()
 {
  Ready ();
  var output = new StringWriter (); var error = new StringWriter ();
  Assert.That (SubmissionSetupCommand.Run (["check", "--run", "run", "--store", _path], output, error), Is.Zero);
  Assert.That (output.ToString (), Does.Contain ("InputsReady").And.Not.Contain ("sender@example.invalid").And.Not.Contain ("SYNTHETIC-SECRET"));
  output.GetStringBuilder ().Clear ();
  Assert.That (SubmissionSetupCommand.Run (["snapshot", "--run", "run", "--name", "cli-attempt", "--store", _path], output, error), Is.Zero);
  Assert.That (_store.LoadSetupProfile<SubmissionSetupSnapshot> ("cli-attempt").Value.Run.Value.Version, Is.EqualTo ("1.0.0"));
  Assert.That (SubmissionSetupCommand.Run (["snapshot", "--run", "run", "--name", "cli-attempt", "--store", _path], output, error), Is.EqualTo (2));
 }

 [Test] public void ExistingProcessorSigningAndDeliveryConsumersReadSnapshotWithoutExportingSecrets ()
 {
  Ready (); _store.CreateSubmissionSetupSnapshot ("run", "first");
  string path = _store.GetSubmissionSetupSnapshotPath ("first");
  var processor = ProcessorCredentialSettings.Resolve (path, "processor.invalid", new ());
  Assert.That (processor.Password, Is.EqualTo ("SYNTHETIC-SECRET"));
  Assert.Throws<InvalidOperationException> (() => ProcessorCredentialSettings.Resolve (path, "wrong.invalid", new ()));
  var delivery = SubmissionDispatchCommand.ResolveCredentials (path, "smtp.example.invalid", 587, "sender@example.invalid");
  Assert.That (delivery.UploadPassword, Is.EqualTo ("SYNTHETIC-SECRET"));
  Assert.Throws<InvalidOperationException> (() => SubmissionDispatchCommand.ResolveCredentials (path, "smtp.example.invalid", 587, "wrong@example.invalid"));
  using var input = SubmissionSignatureInput.Prepare (["sign-self-test-form", "--authorization", "exact-approval.json", "--credentials", path]);
  Assert.That (input.Arguments, Is.EqualTo (new[] { "sign-self-test-form", "--authorization", "exact-approval.json", "--signature-stdin" }));
  Assert.That (input.Image, Is.EqualTo (new byte[] { 1, 2, 3 }));
  input.Dispose (); Assert.That (input.Image, Is.All.Zero);
  Assert.That (Directory.GetFiles (_path, "*.json").Select (Path.GetFileName), Is.EqualTo (new[] { "store.json" }));
  Assert.Throws<ArgumentException> (() => DevToolsCredentialBindings.Read (Path.Combine (_path, "developer-developer.setup")));
 }

 [Test] public void PreparedDraftsUseFrozenPublicFactsAndExcludePrivateContactsAndRestrictions ()
 {
  Ready ();
  var developer = _store.LoadSetupProfile<SubmissionDeveloperProfile> ("developer");
  developer.Value.ContactEmail = "PRIVATE-CONTACT@example.invalid"; developer.Value.PostalAddress = "PRIVATE-ADDRESS";
  _store.SaveSetupProfile ("developer", developer.Value, developer.Revision);
  var driver = _store.LoadSetupProfile<SubmissionDriverProfile> ("driver");
  driver.Value.RealUseRestrictions = "PRIVATE-RESTRICTIONS"; driver.Value.SupportWebsite = "https://example.invalid/driver-support/";
  _store.SaveSetupProfile ("driver", driver.Value, driver.Revision);
  _store.CreateSubmissionSetupSnapshot ("run", "first");
  driver.Value.DriverName = "New unsnapshotted name"; _store.SaveSetupProfile ("driver", driver.Value, driver.Revision + 1);
  var prepared = _store.PrepareSubmissionSetupInputs ("first");
  string help = File.ReadAllText (prepared.HelpContentPath); string notes = File.ReadAllText (prepared.ReleaseNotesPath);
  using var json = JsonDocument.Parse (help);
  Assert.That (json.RootElement.GetProperty ("version").GetString (), Is.EqualTo ("1.0.0.0"));
  Assert.That (json.RootElement.GetProperty ("pending").GetArrayLength (), Is.GreaterThan (0));
  Assert.That (json.RootElement.GetProperty ("sections").EnumerateObject ().Count (), Is.EqualTo (12));
  foreach (string publicText in new[] { help, notes })
  {
   Assert.That (publicText, Does.Contain ("Test Driver").And.Contain ("https://example.invalid/driver-support/").And.Contain ("https://example.invalid/repo"));
   foreach (string excluded in new[] { "PRIVATE-CONTACT", "PRIVATE-ADDRESS", "PRIVATE-RESTRICTIONS", "SYNTHETIC-SECRET", "sender@example.invalid", "New unsnapshotted name" })
    Assert.That (publicText, Does.Not.Contain (excluded));
  }
  var defaults = JsonSerializer.Deserialize<SubmissionSetupOperationDefaults> (File.ReadAllText (prepared.OperationDefaultsPath))!;
  Assert.That (defaults.Title, Is.EqualTo ("Test Driver 1.0.0 - Crestron Home driver"));
  Assert.That (defaults.Sender, Is.EqualTo ("sender@example.invalid"));
  Assert.That (File.ReadAllText (prepared.OperationDefaultsPath), Does.Not.Contain ("SYNTHETIC-SECRET"));
  var next = _store.PrepareSubmissionSetupInputs ("first");
  Assert.That (next.HelpContentPath, Is.Not.EqualTo (prepared.HelpContentPath));
  Assert.That (File.ReadAllText (prepared.HelpContentPath), Is.EqualTo (help));
  // Retain a synthetic public draft for the separate real help-builder contract check.
  string artifact = Path.Combine (TestContext.CurrentContext.WorkDirectory, "setup-help-contract.review.json");
  File.WriteAllText (artifact, help); TestContext.AddTestAttachment (artifact);
 }

 [Test] public void ConsolePreparationReturnsOnlyPathsAndLeavesSignatureEncrypted ()
 {
  Ready (); _store.CreateSubmissionSetupSnapshot ("run", "first");
  var output = new StringWriter (); var error = new StringWriter ();
  Assert.That (SubmissionSetupCommand.Run (["prepare", "--snapshot", "first", "--store", _path], output, error), Is.Zero);
  var prepared = JsonSerializer.Deserialize<SubmissionSetupPreparedInputs> (output.ToString ())!;
  Assert.That (File.Exists (prepared.HelpContentPath), Is.True);
  Assert.That (output.ToString (), Does.Not.Contain ("SYNTHETIC-SECRET").And.Not.Contain ("sender@example.invalid"));
  Assert.That (Directory.GetFiles (_path, "*.png", SearchOption.AllDirectories), Is.Empty);
 }

 [Test] public void OptionalRemoteCredentialMustMatchTheWorkerAndIsAvailableToExistingBindings ()
 {
  Ready ();
  _store.SaveCredential ("remote", new (DevToolsCredentialPurpose.Windows, "worker.invalid", "test", "SYNTHETIC-WINDOWS-SECRET", 22));
  var run = _store.LoadSetupProfile<SubmissionRunProfile> ("run");
  run.Value.WindowsCredential = "remote"; run.Value.WindowsHost = "wrong.invalid";
  var changed = _store.SaveSetupProfile ("run", run.Value, run.Revision);
  Assert.That (_store.CheckSubmissionSetup ("run"), Has.Some.Contains ("Saved Windows credential"));
  changed.Value.WindowsHost = "worker.invalid";
  _store.SaveSetupProfile ("run", changed.Value, changed.Revision);
  Assert.That (_store.CheckSubmissionSetup ("run"), Is.Empty);
  _store.CreateSubmissionSetupSnapshot ("run", "remote-attempt");
  var bindings = DevToolsCredentialBindings.Read (_store.GetSubmissionSetupSnapshotPath ("remote-attempt"));
  Assert.That (bindings.Resolve (DevToolsCredentialPurpose.Windows, "worker.invalid", 22).Password, Is.EqualTo ("SYNTHETIC-WINDOWS-SECRET"));
 }
}
