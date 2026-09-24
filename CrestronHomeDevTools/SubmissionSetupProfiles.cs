// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace CrestronHomeDevTools;

/// <summary>Editable private setup facts. These values are not evidence or approval.</summary>
public sealed class SubmissionDeveloperProfile
{
	[Category("Identity"), DisplayName("Developer name"), Description("Your name as it should appear on submission documents.")]
	[Required]
	public string DeveloperName { get; set; } = "";
	[Category("Identity"), DisplayName("Company / organisation"), Description("Leave blank when submitting as an individual.")]
	public string Company { get; set; } = "";
	[Category("Identity"), DisplayName("Postal address"), Description("Business contact address, if applicable.")]
	public string PostalAddress { get; set; } = "";
	[Category("Contact"), DisplayName("Submission contact email"), Description("Private correspondence address used for the submission.")]
	[Required]
	[EmailAddress]
	public string ContactEmail { get; set; } = "";
	[Category("Contact"), DisplayName("Contact telephone"), Description("Optional contact number.")]
	public string ContactPhone { get; set; } = "";
	[Category("Contact"), DisplayName("Public support form URL"), Description("An account-free contact form is suitable. Supply at least one public support method.")]
	[Url]
	public string SupportWebsite { get; set; } = "";
	[Category("Contact"), DisplayName("Public support email"), Description("Optional when a support website or telephone is supplied.")]
	[EmailAddress]
	public string SupportEmail { get; set; } = "";
	[Category("Contact"), DisplayName("Public support telephone"), Description("Optional when another public support method is supplied.")]
	public string SupportPhone { get; set; } = "";
	[Category("Delivery"), DisplayName("Outgoing mail server"), Description("Host associated with the named saved SMTP credential.")]
	[Required]
	public string SmtpHost { get; set; } = "";
	[Category("Delivery"), DisplayName("Outgoing mail port"), Description("587 for STARTTLS, or 465 for implicit TLS.")]
	[Required]
	public string SmtpPort { get; set; } = "587";
	[Category("Delivery"), DisplayName("Submission sender email"), Description("Must match the sender on the saved SMTP credential.")]
	[Required]
	[EmailAddress]
	public string SenderEmail { get; set; } = "";
	[Category("Delivery"), DisplayName("Saved mail credential"), Description("Name from the Credentials tab; never enter a password here.")]
	[Required]
	public string SmtpCredential { get; set; } = "";
	[Category("Delivery"), DisplayName("Saved uploader credential"), Description("Name from the Credentials tab.")]
	[Required]
	public string UploaderCredential { get; set; } = "";
	[Category("Signing"), DisplayName("Saved signature"), Description("Name of the encrypted signature image. Saving does not authorize its use.")]
	[Required]
	public string SignatureEntry { get; set; } = "";
	[Category("Equipment"), DisplayName("Equipment inventory file"), Description("Optional existing DevTools inventory listing processors, Windows machines and permitted roles.")]
	public string ResourceInventoryPath { get; set; } = "";
	[Category("Other"), DisplayName("Other reusable information"), Description("Additional factual information, not passwords or blanket approvals.")]
	public string AdditionalInformation { get; set; } = "";
	public override string ToString () => "Private submission setup (values hidden)";
}

/// <summary>Editable private setup facts. These values are not evidence or approval.</summary>
public sealed class SubmissionDriverProfile
{
	[Category("Product"), DisplayName("Driver name"), Description("Public name of the driver.")]
	[Required]
	public string DriverName { get; set; } = "";
	[Category("Product"), DisplayName("Project repository URL"), Description("The driver source repository.")]
	[Required]
	[Url]
	public string RepositoryUrl { get; set; } = "";
	[Category("Product"), DisplayName("Driver project within repository"), Description("Path relative to the repository, if there is more than one project.")]
	public string ProjectPath { get; set; } = "";
	[Category("Product"), DisplayName("Device manufacturer"), Description("Manufacturer of the equipment being controlled.")]
	[Required]
	public string Manufacturer { get; set; } = "";
	[Category("Product"), DisplayName("Supported models"), Description("Exact supported model names and regional limitations.")]
	[Required]
	public string Models { get; set; } = "";
	[Category("Product"), DisplayName("Device category"), Description("For example, thermostat or weather station.")]
	[Required]
	public string DeviceCategory { get; set; } = "";
	[Category("Product"), DisplayName("Connection / protocol"), Description("For example, IP with local network authentication.")]
	[Required]
	public string Connection { get; set; } = "";
	[Category("Help"), DisplayName("Description and features"), Description("Supported functions and intended use.")]
	[Required]
	public string Description { get; set; } = "";
	[Category("Help"), DisplayName("Installation and prerequisites"), Description("Required hardware, subscriptions, accounts and setup steps.")]
	[Required]
	public string Installation { get; set; } = "";
	[Category("Help"), DisplayName("System requirements and dependencies"), Description("Supported processor/app versions, network requirements and required services.")]
	public string Requirements { get; set; } = "";
	[Category("Help"), DisplayName("Configuration fields"), Description("Explain the installer settings, defaults and allowed values.")]
	[Required]
	public string Configuration { get; set; } = "";
	[Category("Help"), DisplayName("Using the driver"), Description("Explain user controls, feedback and normal behaviour.")]
	[Required]
	public string Usage { get; set; } = "";
	[Category("Help"), DisplayName("Known limitations"), Description("Include unsupported variants and necessary qualifications.")]
	[Required]
	public string Limitations { get; set; } = "";
	[Category("Help"), DisplayName("Troubleshooting"), Description("Common problems and recovery steps.")]
	[Required]
	public string Troubleshooting { get; set; } = "";
	[Category("Help"), DisplayName("Licensing and acknowledgements"), Description("Product notices and known third-party requirements; dependency audit remains required.")]
	public string Licensing { get; set; } = "";
	[Category("Support overrides"), DisplayName("Support form URL override"), Description("Blank inherits the developer profile.")]
	[Url]
	public string SupportWebsite { get; set; } = "";
	[Category("Support overrides"), DisplayName("Support email override"), Description("Blank inherits the developer profile.")]
	[EmailAddress]
	public string SupportEmail { get; set; } = "";
	[Category("Support overrides"), DisplayName("Support telephone override"), Description("Blank inherits the developer profile.")]
	public string SupportPhone { get; set; } = "";
	[Category("Test equipment"), DisplayName("Available test devices"), Description("Models, hardware revisions and firmware versions available for testing.")]
	[Required]
	public string TestDeviceModels { get; set; } = "";
	[Category("Test equipment"), DisplayName("Real-use restrictions"), Description("Say whether test devices control a home/office and describe restrictions. Use test only where appropriate.")]
	[Required]
	public string RealUseRestrictions { get; set; } = "";
	[Category("Test equipment"), DisplayName("Permitted test changes"), Description("Describe allowed actions and required restoration. Final operations remain subject to authorization.")]
	[Required]
	public string PermittedTestChanges { get; set; } = "";
	[Category("Test equipment"), DisplayName("Unavailable tests / limitations"), Description("For example, a permanently wired device that cannot be power-cycled. This is not an N/A verdict or test result.")]
	public string UnavailableTests { get; set; } = "";
	[Category("Other"), DisplayName("Other driver information"), Description("Driver-specific facts not covered above; no passwords.")]
	public string AdditionalInformation { get; set; } = "";
	public override string ToString () => "Private submission setup (values hidden)";
}

/// <summary>Editable private setup facts. These values are not evidence or approval.</summary>
public sealed class SubmissionRunProfile
{
	[Category("Selection"), DisplayName("Developer profile"), Description("Saved profile name from the Developer tab.")]
	[Required]
	public string DeveloperProfile { get; set; } = "";
	[Category("Selection"), DisplayName("Driver profile"), Description("Saved profile name from the Driver tab.")]
	[Required]
	public string DriverProfile { get; set; } = "";
	[Category("Candidate"), DisplayName("Driver version"), Description("Version being submitted.")]
	[Required]
	public string Version { get; set; } = "";
	[Category("Candidate"), DisplayName("Exact manifest version"), Description("Four components from the candidate manifest, for example 1.2.3.0. Do not infer a build revision.")]
	[Required]
	public string ManifestVersion { get; set; } = "";
	[Category("Candidate"), DisplayName("Release notes"), Description("User-facing changes in this version. The workflow adds your public support contact and repository link.")]
	public string ReleaseNotes { get; set; } = "";
	[Category("Candidate"), DisplayName("Source reference"), Description("Commit or tag to build; the workflow must resolve and pin its actual identity.")]
	[Required]
	public string SourceReference { get; set; } = "";
	[Category("Candidate"), DisplayName("Candidate package path"), Description("Optional until built; must be verified and hashed by the workflow.")]
	public string PackagePath { get; set; } = "";
	[Category("Candidate"), DisplayName("Private submission workspace"), Description("Absolute local directory outside the public driver repository.")]
	[Required]
	public string PrivateWorkspace { get; set; } = "";
	[Category("Equipment"), DisplayName("Test processor"), Description("Name from the equipment inventory, or an explicit endpoint for initial setup.")]
	[Required]
	public string ProcessorResource { get; set; } = "";
	[Category("Equipment"), DisplayName("Processor address"), Description("Endpoint bound to the saved processor credential.")]
	[Required]
	public string ProcessorHost { get; set; } = "";
	[Category("Equipment"), DisplayName("Saved processor credential"), Description("Existing private-store entry name.")]
	[Required]
	public string ProcessorCredential { get; set; } = "";
	[Category("Equipment"), DisplayName("Worker computer"), Description("Name of the Windows machine running tests/monitoring; this computer is allowed.")]
	[Required]
	public string WindowsResource { get; set; } = "";
	[Category("Equipment"), DisplayName("Saved Windows credential"), Description("Only needed for a remote worker.")]
	public string WindowsCredential { get; set; } = "";
	[Category("Equipment"), DisplayName("Remote worker address"), Description("Hostname or IP bound to the saved Windows credential. Leave blank when working locally without a remote credential.")]
	public string WindowsHost { get; set; } = "";
	[Category("Equipment"), DisplayName("Android app test target"), Description("Emulator/device identifier or describe why app testing is not applicable.")]
	[Required]
	public string AndroidTarget { get; set; } = "";
	[Category("Automation rehearsal"), DisplayName("Reviewed workflow settings template"), Description("Optional absolute path to the private automation settings template. It supplies executable test, equipment, endurance and review bindings; preparation does not invent them.")]
	public string AutomationSettingsTemplate { get; set; } = "";
	[Category("Automation rehearsal"), DisplayName("Reviewed tooling manifest"), Description("Absolute path to the public workflow's tooling manifest. Preparation captures and pins its current bytes.")]
	public string AutomationToolingManifest { get; set; } = "";
	[Category("Automation rehearsal"), DisplayName("Release package asset name"), Description("Exact .pkg asset filename; may include ${version}. No directory path.")]
	public string AutomationPackageName { get; set; } = "";
	[Category("Automation rehearsal"), DisplayName("Earliest release publication (UTC)"), Description("Explicit cutoff, for example 2026-09-24T00:00:00Z. Older releases are excluded. Preparation does not start discovery.")]
	public string AutomationNotBeforeUtc { get; set; } = "";
	[Category("Review"), DisplayName("Submission-specific notes"), Description("Changes since previous submissions, known gaps and review context. No test results are inferred.")]
	public string SubmissionNotes { get; set; } = "";
	public override string ToString () => "Private submission setup (values hidden)";
}

/// <summary>A versioned profile. Its value is private; do not write it to public logs.</summary>
public sealed record SubmissionSetupProfile<T> (string Name, int Revision, DateTimeOffset SavedUtc, T Value)
{
 public override string ToString () => $"Saved setup revision {Revision} (values hidden)";
}

/// <summary>Frozen setup inputs for one attempt, not signed approval or a test result.</summary>
public sealed record SubmissionSetupSnapshot (
 SubmissionSetupProfile<SubmissionDeveloperProfile> Developer,
 SubmissionSetupProfile<SubmissionDriverProfile> Driver,
 SubmissionSetupProfile<SubmissionRunProfile> Run)
{
 public override string ToString () => "Private submission snapshot (values hidden)";
 public string SupportWebsite => string.IsNullOrWhiteSpace (Driver.Value.SupportWebsite) ? Developer.Value.SupportWebsite : Driver.Value.SupportWebsite;
 public string SupportEmail => string.IsNullOrWhiteSpace (Driver.Value.SupportEmail) ? Developer.Value.SupportEmail : Driver.Value.SupportEmail;
 public string SupportPhone => string.IsNullOrWhiteSpace (Driver.Value.SupportPhone) ? Developer.Value.SupportPhone : Driver.Value.SupportPhone;
}

public static class SubmissionSetupValidation
{
 /// <summary>Return missing/invalid field labels only, never the entered private values. Drafts can still be saved.</summary>
 public static IReadOnlyList<string> Check (object profile)
 {
  var errors = new List<string> ();
  foreach (var property in profile.GetType ().GetProperties ())
  {
   string? value = property.GetValue (profile) as string;
   string label = property.GetCustomAttributes (typeof (DisplayNameAttribute), false).Cast<DisplayNameAttribute> ().FirstOrDefault ()?.DisplayName ?? property.Name;
   bool required = property.IsDefined (typeof (RequiredAttribute), false);
   if (string.IsNullOrWhiteSpace (value)) { if (required) errors.Add (label + " is required."); continue; }
   if (value.Length > 32000) errors.Add (label + " exceeds 32,000 characters.");
   foreach (var rule in property.GetCustomAttributes (typeof (ValidationAttribute), false).Cast<ValidationAttribute> ())
    if (!rule.IsValid (value)) errors.Add (label + " is invalid.");
   if (property.Name.EndsWith ("Website", StringComparison.Ordinal) || property.Name.EndsWith ("Url", StringComparison.Ordinal))
    if (!Uri.TryCreate (value, UriKind.Absolute, out var uri) || uri.Scheme != "https") errors.Add (label + " must be an HTTPS URL.");
  }
  if (profile is SubmissionDeveloperProfile d)
  {
   if (string.IsNullOrWhiteSpace (d.SupportWebsite) && string.IsNullOrWhiteSpace (d.SupportEmail) && string.IsNullOrWhiteSpace (d.SupportPhone)) errors.Add ("At least one public support contact is required.");
   if (!int.TryParse (d.SmtpPort, out int port) || port is not (465 or 587)) errors.Add ("Outgoing mail port must be 465 or 587 for TLS.");
  }
  if (profile is SubmissionRunProfile r && !string.IsNullOrWhiteSpace (r.PrivateWorkspace) && !Path.IsPathFullyQualified (r.PrivateWorkspace)) errors.Add ("Private workspace must be an absolute path.");
  if (profile is SubmissionRunProfile versioned && !string.IsNullOrWhiteSpace (versioned.ManifestVersion) &&
   (versioned.ManifestVersion.Split ('.').Length != 4 || versioned.ManifestVersion.Split ('.').Any (p => p.Length is < 1 or > 5 || !p.All (char.IsAsciiDigit) || !int.TryParse (p, out int n) || n > 65534))) errors.Add ("Exact manifest version must have four numeric components from 0 to 65534.");
  return errors.Distinct ().ToArray ();
 }
}
