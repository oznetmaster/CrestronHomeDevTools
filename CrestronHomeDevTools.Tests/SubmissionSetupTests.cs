// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

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
 private void Ready ()
 {
  _store.SaveCredential ("mail", new (DevToolsCredentialPurpose.Smtp, "smtp.example.invalid", "user", "SYNTHETIC-SECRET", 587, "sender@example.invalid"));
  _store.SaveCredential ("uploader", new (DevToolsCredentialPurpose.Uploader, "uploader.crestron.com", "user", "SYNTHETIC-SECRET"));
  _store.SaveCredential ("processor", new (DevToolsCredentialPurpose.Processor, "processor.invalid", "user", "SYNTHETIC-SECRET", CertificateSha256: new string ('A', 64), SshFingerprint: "SHA256:synthetic-test-fingerprint"));
  _store.SaveSignature ("signature", [1, 2, 3], ".png");
  _store.SaveSetupProfile ("developer", new SubmissionDeveloperProfile { DeveloperName = "Test Person", ContactEmail = "sender@example.invalid", SupportWebsite = "https://example.invalid/support/", SmtpHost = "smtp.example.invalid", SenderEmail = "sender@example.invalid", SmtpCredential = "mail", UploaderCredential = "uploader", SignatureEntry = "signature" });
  _store.SaveSetupProfile ("driver", new SubmissionDriverProfile { DriverName = "Test Driver", RepositoryUrl = "https://example.invalid/repo", Manufacturer = "Test", Models = "Test model", DeviceCategory = "Test", Connection = "IP", Description = "Description", Installation = "Installation", Configuration = "Settings", Usage = "Usage", Limitations = "None known", Troubleshooting = "Help", TestDeviceModels = "Test fixture", RealUseRestrictions = "Test only", PermittedTestChanges = "Reversible with restoration" });
  _store.SaveSetupProfile ("run", new SubmissionRunProfile { DeveloperProfile = "developer", DriverProfile = "driver", Version = "1.0.0", ManifestVersion = "1.0.0.0", SourceReference = "test-commit", PrivateWorkspace = _path, ProcessorResource = "test", ProcessorHost = "processor.invalid", ProcessorCredential = "processor", WindowsResource = "local", AndroidTarget = "test-emulator" });
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
