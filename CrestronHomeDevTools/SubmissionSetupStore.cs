// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrestronHomeDevTools;

[SupportedOSPlatform ("windows")]
public sealed partial class DevToolsPrivateStore
{
 private sealed record SetupEnvelope<T> (int SchemaVersion, string Kind, SubmissionSetupProfile<T> Profile);
 private static readonly byte[] SetupEntropy = Encoding.UTF8.GetBytes ("CrestronHomeDevTools/submission-setup/v1");
 private static string SetupKind<T> () => typeof (T) == typeof (SubmissionDeveloperProfile) ? "developer" :
  typeof (T) == typeof (SubmissionDriverProfile) ? "driver" : typeof (T) == typeof (SubmissionRunProfile) ? "run" :
  typeof (T) == typeof (SubmissionSetupSnapshot) ? "snapshot" : throw new ArgumentException ("Unsupported setup profile type.");
 private string SetupPath<T> (string name)
 {
  _ = GetPath (name); // Same safe-name contract as credential entries.
  return Path.Combine (DirectoryPath, SetupKind<T> () + "-" + name + ".setup");
 }
 public IReadOnlyList<string> ListSetupProfiles<T> ()
 {
  string prefix = SetupKind<T> () + "-";
  return Directory.EnumerateFiles (DirectoryPath, prefix + "*.setup")
   .Select (p => Path.GetFileNameWithoutExtension (p)[prefix.Length..]).Order (StringComparer.Ordinal).ToArray ();
 }
 public SubmissionSetupProfile<T> LoadSetupProfile<T> (string name)
 {
  string path = SetupPath<T> (name);
  if (new FileInfo (path).Length > 4194304) throw new InvalidDataException ("Setup profile exceeds its size limit.");
  byte[] plain = ProtectedData.Unprotect (File.ReadAllBytes (path), SetupEntropy,
   _settings.MachineScope ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser);
  try
  {
   var envelope = JsonSerializer.Deserialize<SetupEnvelope<T>> (plain) ?? throw new InvalidDataException ("Empty setup profile.");
   if (envelope.SchemaVersion != 1 || envelope.Kind != SetupKind<T> () || envelope.Profile.Name != name || envelope.Profile.Revision < 1 || envelope.Profile.Value == null)
    throw new InvalidDataException ("Unsupported or mismatched setup profile.");
   return envelope.Profile;
  }
  finally { CryptographicOperations.ZeroMemory (plain); }
 }
 /// <summary>Save a draft or edit. expectedRevision=0 creates; stale edits are rejected. Snapshots cannot be replaced.</summary>
 public SubmissionSetupProfile<T> SaveSetupProfile<T> (string name, T value, int expectedRevision = 0)
 {
  ArgumentNullException.ThrowIfNull (value);
  string path = SetupPath<T> (name);
  // Cross-process exclusion: another editor must reload instead of silently losing an edit.
  using var gate = new FileStream (Path.Combine (DirectoryPath, "setup-write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
  int revision = File.Exists (path) ? LoadSetupProfile<T> (name).Revision : 0;
  if (expectedRevision != revision || expectedRevision < 0) throw new InvalidOperationException ("This profile changed in another window. Reload before saving.");
  if (typeof (T) == typeof (SubmissionSetupSnapshot) && revision != 0) throw new InvalidOperationException ("Submission snapshots cannot be edited. Create a new attempt.");
  var saved = new SubmissionSetupProfile<T> (name, checked (revision + 1), DateTimeOffset.UtcNow, value);
  byte[] plain = JsonSerializer.SerializeToUtf8Bytes (new SetupEnvelope<T> (1, SetupKind<T> (), saved));
  byte[] encrypted;
  try
  {
   if (plain.Length > 2097152) throw new ArgumentException ("Setup profile exceeds its size limit.");
   encrypted = ProtectedData.Protect (plain, SetupEntropy, _settings.MachineScope ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser);
  }
  finally { CryptographicOperations.ZeroMemory (plain); }
  string temporary = path + "." + Guid.NewGuid ().ToString ("N") + ".tmp";
  try
  {
   using (var stream = new FileStream (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write (encrypted); stream.Flush (true); }
   SubmissionJournalFile.Replace (temporary, path, overwrite: revision != 0);
  }
  finally { if (File.Exists (temporary)) File.Delete (temporary); }
  return LoadSetupProfile<T> (name); // Fresh detached copy, never a live reference to the editor.
 }
 /// <summary>Resolve all reusable inputs and verify named entries before a workflow starts. No external operation is performed.</summary>
 public IReadOnlyList<string> CheckSubmissionSetup (string runName)
 {
  var run = LoadSetupProfile<SubmissionRunProfile> (runName);
  var errors = SubmissionSetupValidation.Check (run.Value).Select (e => "Submission: " + e).ToList ();
  SubmissionDeveloperProfile? developer = null;
  try { developer = LoadSetupProfile<SubmissionDeveloperProfile> (run.Value.DeveloperProfile).Value; errors.AddRange (SubmissionSetupValidation.Check (developer).Select (e => "Developer: " + e)); }
  catch (Exception e) when (e is ArgumentException or IOException or CryptographicException) { errors.Add ("Developer profile is missing or unreadable."); }
  try { var driver = LoadSetupProfile<SubmissionDriverProfile> (run.Value.DriverProfile).Value; errors.AddRange (SubmissionSetupValidation.Check (driver).Select (e => "Driver: " + e)); }
  catch (Exception e) when (e is ArgumentException or IOException or CryptographicException) { errors.Add ("Driver profile is missing or unreadable."); }
  if (developer != null)
  {
   try
   {
    var mail = LoadCredential (developer.SmtpCredential, DevToolsCredentialPurpose.Smtp, developer.SmtpHost);
    if (!int.TryParse (developer.SmtpPort, out int port) || mail.Port != port || !string.Equals (mail.Sender, developer.SenderEmail, StringComparison.OrdinalIgnoreCase)) errors.Add ("Saved mail credential does not match the configured sender/port.");
   }
   catch (Exception e) when (e is ArgumentException or IOException or CryptographicException or InvalidOperationException) { errors.Add ("Saved mail credential is missing, unreadable or bound to another endpoint."); }
   try { _ = LoadCredential (developer.UploaderCredential, DevToolsCredentialPurpose.Uploader, "uploader.crestron.com"); }
   catch (Exception e) when (e is ArgumentException or IOException or CryptographicException or InvalidOperationException) { errors.Add ("Saved uploader credential is missing, unreadable or bound to another endpoint."); }
   try { var signature = LoadSignature (developer.SignatureEntry); CryptographicOperations.ZeroMemory (signature.Image); }
   catch (Exception e) when (e is ArgumentException or IOException or CryptographicException or InvalidOperationException) { errors.Add ("Saved signature is missing or unreadable."); }
  }
  try { var processor = LoadCredential (run.Value.ProcessorCredential, DevToolsCredentialPurpose.Processor, run.Value.ProcessorHost);
   if (string.IsNullOrWhiteSpace (processor.CertificateSha256) || string.IsNullOrWhiteSpace (processor.SshFingerprint)) errors.Add ("Saved processor credential needs independently verified HTTPS and SSH trust pins."); }
  catch (Exception e) when (e is ArgumentException or IOException or CryptographicException or InvalidOperationException) { errors.Add ("Saved processor credential is missing, unreadable or bound to another endpoint."); }
  if (!string.IsNullOrWhiteSpace (run.Value.WindowsCredential))
  {
   if (string.IsNullOrWhiteSpace (run.Value.WindowsHost)) errors.Add ("Remote worker address is required when selecting a Windows credential.");
   else
    try { _ = LoadCredential (run.Value.WindowsCredential, DevToolsCredentialPurpose.Windows, run.Value.WindowsHost); }
    catch (Exception e) when (e is ArgumentException or IOException or CryptographicException or InvalidOperationException) { errors.Add ("Saved Windows credential is missing, unreadable or bound to another endpoint."); }
  }
  return errors;
 }
 /// <summary>Freeze private inputs after the input-readiness check. This is not approval, evidence or acceptance.</summary>
 public SubmissionSetupProfile<SubmissionSetupSnapshot> CreateSubmissionSetupSnapshot (string runName, string snapshotName)
 {
  var errors = CheckSubmissionSetup (runName);
  if (errors.Count != 0) throw new InvalidOperationException ("Complete the input-readiness check before creating a snapshot.");
  var run = LoadSetupProfile<SubmissionRunProfile> (runName);
  return SaveSetupProfile (snapshotName, new SubmissionSetupSnapshot (
   LoadSetupProfile<SubmissionDeveloperProfile> (run.Value.DeveloperProfile),
   LoadSetupProfile<SubmissionDriverProfile> (run.Value.DriverProfile), run));
 }
 /// <summary>Build bindings for existing public processor, signing and delivery APIs; no passwords are returned.</summary>
 public DevToolsCredentialBindings GetSubmissionSetupBindings (string snapshotName)
 {
  var snapshot = LoadSetupProfile<SubmissionSetupSnapshot> (snapshotName).Value;
  return new (DirectoryPath, snapshot.Developer.Value.SmtpCredential, snapshot.Run.Value.WindowsCredential,
   snapshot.Developer.Value.UploaderCredential, snapshot.Run.Value.ProcessorCredential, snapshot.Developer.Value.SignatureEntry);
 }
}
