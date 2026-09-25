// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace CrestronHomeDevTools;

/// <summary>Facts for existing preparation commands. No credentials, evidence or authorizations.</summary>
public sealed record SubmissionSetupOperationDefaults (string Title, string Author, string Sender,
 string SmtpHost, int SmtpPort, string ProcessorHost, string AndroidTarget, string CredentialsPath)
{
 public override string ToString () => "Private submission operation defaults (values hidden)";
}

/// <summary>Private review drafts. These paths must not be published as submission evidence.</summary>
public sealed record SubmissionSetupPreparedInputs (string HelpContentPath, string ReleaseNotesPath,
 string OperationDefaultsPath, string CredentialsPath);

[SupportedOSPlatform ("windows")]
public sealed partial class DevToolsPrivateStore
{
 /// <summary>Existing console commands accept this encrypted file directly as --credentials.</summary>
 public string GetSubmissionSetupSnapshotPath (string snapshotName)
 {
  _ = LoadSetupProfile<SubmissionSetupSnapshot> (snapshotName);
  return SetupPath<SubmissionSetupSnapshot> (snapshotName);
 }

 /// <summary>Read saved factual defaults for form preparation, processor commands and delivery settings.</summary>
 public SubmissionSetupOperationDefaults GetSubmissionSetupOperationDefaults (string snapshotName)
 {
  var snapshot = LoadSetupProfile<SubmissionSetupSnapshot> (snapshotName).Value;
  var d = snapshot.Developer.Value; var r = snapshot.Run.Value;
  if (!Enum.IsDefined(snapshot.Purpose)) throw new InvalidDataException("Unknown setup snapshot purpose.");
  bool rehearsal = snapshot.Purpose == SubmissionSetupPurpose.Rehearsal;
  return new ($"{snapshot.Driver.Value.DriverName} {r.Version} - Crestron Home driver", d.DeveloperName,
   rehearsal ? "" : d.SenderEmail, rehearsal ? "" : d.SmtpHost, rehearsal ? 0 : int.Parse (d.SmtpPort, System.Globalization.CultureInfo.InvariantCulture),
   r.ProcessorHost, r.AndroidTarget, SetupPath<SubmissionSetupSnapshot> (snapshotName));
 }

 /// <summary>Generate help-builder content from saved public facts. Always requires review before final use.</summary>
 public string CreateSubmissionSetupHelpDraft (string snapshotName)
 {
  var s = LoadSetupProfile<SubmissionSetupSnapshot> (snapshotName).Value;
  var d = s.Driver.Value; var r = s.Run.Value;
  if (SubmissionSetupValidation.Check (r).Count != 0)
   throw new InvalidOperationException ("Complete the submission fields, including the exact manifest version, before preparing help.");
  var pending = new List<string> {
   "Verify saved product facts, supported models and public support details against this candidate.",
   "Declare all app pages and add reviewed screenshots to uiPages and experience.",
   "Replace the planned test equipment with the environment actually used and verified."
  };
  string Needed (string value, string label)
  {
   if (!string.IsNullOrWhiteSpace (value)) return value;
   pending.Add ("Complete " + label + "."); return "REVIEW REQUIRED: " + label + ".";
  }
  object[] Paragraphs (params string[] values) => values.Where (v => !string.IsNullOrWhiteSpace (v))
   .Select (v => (object)new { kind = "paragraph", text = v }).ToArray ();
  var sections = new Dictionary<string, object[]> {
   ["driver"] = Paragraphs (d.DriverName, "Version " + r.Version, d.Description),
   ["notes"] = Paragraphs (d.Troubleshooting),
   ["requirements"] = Paragraphs (Needed (d.Requirements, "system requirements and dependencies")),
   ["installation"] = Paragraphs (d.Installation, "Configuration", d.Configuration),
   ["experience"] = Paragraphs (d.Usage),
   ["limitations"] = Paragraphs (d.Limitations),
   ["features"] = Paragraphs (d.Description),
   ["environment"] = Paragraphs ("Planned test equipment — verify against retained test evidence:", d.TestDeviceModels),
   ["models"] = Paragraphs (d.Manufacturer, d.Models, "Category: " + d.DeviceCategory, "Connection: " + d.Connection),
   ["contact"] = Paragraphs (PublicContacts (s)),
   ["history"] = Paragraphs ("Version " + r.Version, Needed (r.ReleaseNotes, "release notes for this version")),
   ["license"] = Paragraphs (Needed (d.Licensing, "licensing and acknowledgements"))
  };
  return JsonSerializer.Serialize (new { schemaVersion = 1, title = d.DriverName + " - Driver Help",
   author = s.Developer.Value.DeveloperName, version = r.ManifestVersion, pending,
   uiPages = Array.Empty<string> (), sections }, new JsonSerializerOptions { WriteIndented = true });
 }

 private static string[] PublicContacts (SubmissionSetupSnapshot s) => new[] {
  string.IsNullOrWhiteSpace (s.SupportWebsite) ? "" : "Support: " + s.SupportWebsite,
  string.IsNullOrWhiteSpace (s.SupportEmail) ? "" : "Support email: " + s.SupportEmail,
  string.IsNullOrWhiteSpace (s.SupportPhone) ? "" : "Support telephone: " + s.SupportPhone,
  "Project repository: " + s.Driver.Value.RepositoryUrl
 };

 /// <summary>Prepare review drafts in a fresh, access-restricted child of the store. No secret bytes are exported.</summary>
 public SubmissionSetupPreparedInputs PrepareSubmissionSetupInputs (string snapshotName)
 {
  var s = LoadSetupProfile<SubmissionSetupSnapshot> (snapshotName).Value;
  string help = CreateSubmissionSetupHelpDraft (snapshotName);
  var defaults = GetSubmissionSetupOperationDefaults (snapshotName);
  // Inherits the private store's NTFS protection. Each attempt has fresh outputs;
  // never overwrite the user's reviewed or edited documents.
  string directory = Path.Combine (DirectoryPath, "prepared-" + snapshotName + "-" + Guid.NewGuid ().ToString ("N"));
  Directory.CreateDirectory (directory);
  string helpPath = Path.Combine (directory, "help-content.review.json");
  string notesPath = Path.Combine (directory, "release-notes.review.md");
  string defaultsPath = Path.Combine (directory, "operation-defaults.json");
  File.WriteAllText (helpPath, help, new UTF8Encoding (false));
  string changes = string.IsNullOrWhiteSpace (s.Run.Value.ReleaseNotes) ? "REVIEW REQUIRED: add changes for this version." : s.Run.Value.ReleaseNotes;
  File.WriteAllText (notesPath, $"# {s.Driver.Value.DriverName} {s.Run.Value.Version}\n\nReview draft — verify before publishing.\n\n{changes}\n\n" +
   string.Join ("\n\n", PublicContacts (s).Where (c => c.Length != 0)) + "\n", new UTF8Encoding (false));
  File.WriteAllText (defaultsPath, JsonSerializer.Serialize (defaults, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding (false));
  return new (helpPath, notesPath, defaultsPath, defaults.CredentialsPath);
 }
}
