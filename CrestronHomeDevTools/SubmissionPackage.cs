// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO.Compression;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrestronHomeDevTools;

public enum PortalSubmissionKind
	{
	Unspecified, NewDriver, ExistingDriverUpdate
	}

public sealed record SubmissionPackageRequirements (
	string DriverId, string DriverVersion, PortalSubmissionKind Kind, string DeveloperFilenameToken, string PublicSupportEmail = "")
	{
	/// <summary>Approved public support URL. Email, website, or both may be specified.</summary>
	public string? PublicSupportWebsite { get; init; }
	}

public sealed record SubmissionPackageIssue (string Code, string Message);

/// <summary>Offline structural checks only. This report does not authorize signing or submission.</summary>
public sealed record SubmissionPackageReport (
	string PackageFileName, string Sha256, DriverPackageInfo? Identity, IReadOnlyList<SubmissionPackageIssue> Issues)
	{
	public bool PackageChecksPassed => Issues.Count == 0;
	}

public static class SubmissionPackage
	{
	/// <summary>Inspect a package without extracting files, executing code or contacting a processor.</summary>
	public static SubmissionPackageReport Inspect (string path, SubmissionPackageRequirements requirements)
		{
		using var input = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
		return Inspect (input, Path.GetFileName (path), requirements);
		}

	internal static SubmissionPackageReport Inspect (Stream input, string fileName, SubmissionPackageRequirements requirements)
		{
		ArgumentNullException.ThrowIfNull (requirements);
		if (!Guid.TryParse (requirements.DriverId, out var expectedId))
			throw new ArgumentException ("Expected driver ID must be a GUID.", nameof (requirements));
		if (!Version.TryParse (requirements.DriverVersion, out var expectedVersion) || expectedVersion.Revision < 0 ||
			new[] { expectedVersion.Major, expectedVersion.Minor, expectedVersion.Build, expectedVersion.Revision }.Any (component => component > 65534))
			throw new ArgumentException ("Expected driver version must have four numeric components.", nameof (requirements));
		if (requirements.Kind is not (PortalSubmissionKind.NewDriver or PortalSubmissionKind.ExistingDriverUpdate))
			throw new ArgumentException ("Specify whether this is a new portal driver or an existing portal driver update.", nameof (requirements));
		bool hasEmail = !string.IsNullOrEmpty (requirements.PublicSupportEmail);
		bool hasWebsite = !string.IsNullOrEmpty (requirements.PublicSupportWebsite);
		if (!hasEmail && !hasWebsite)
			throw new ArgumentException ("Provide an approved public support email or website.", nameof (requirements));
		if (hasEmail && (!MailAddress.TryCreate (requirements.PublicSupportEmail, out var supportAddress) || supportAddress.Address != requirements.PublicSupportEmail))
			throw new ArgumentException ("Provide the approved public support email address.", nameof (requirements));
		if (hasWebsite && (!Uri.TryCreate (requirements.PublicSupportWebsite, UriKind.Absolute, out var website) ||
			website.Scheme is not ("https" or "http") || string.IsNullOrEmpty (website.Host) || !string.IsNullOrEmpty (website.UserInfo) ||
			requirements.PublicSupportWebsite!.Any (char.IsWhiteSpace)))
			throw new ArgumentException ("Provide an absolute HTTP or HTTPS support website without embedded credentials.", nameof (requirements));
		if (requirements.Kind == PortalSubmissionKind.NewDriver &&
			 (string.IsNullOrWhiteSpace (requirements.DeveloperFilenameToken) || requirements.DeveloperFilenameToken.Any (c => !char.IsAsciiLetterOrDigit (c) && c != '-')))
			throw new ArgumentException ("New submissions require a developer filename token containing letters, digits or hyphens.", nameof (requirements));

		var issues = new List<SubmissionPackageIssue> ();
		void Issue (string code, string message) => issues.Add (new (code, message));
		input.Position = 0;
		var digest = Convert.ToHexString (SHA256.HashData (input)).ToLowerInvariant ();
		input.Position = 0;
		DriverPackageInfo? identity = null;
		var basename = Path.GetFileNameWithoutExtension (fileName);
		if (!fileName.EndsWith (".pkg", StringComparison.OrdinalIgnoreCase))
			Issue ("package-extension", "The candidate must be a .pkg file.");
		if (requirements.Kind == PortalSubmissionKind.NewDriver &&
			 !basename.Split ('_').Contains (requirements.DeveloperFilenameToken, StringComparer.Ordinal))
			Issue ("developer-filename", "A new portal driver's filename must include the configured developer token as an underscore-separated component.");
		try
			{
			identity = DriverDeployment.Inspect (input);
			if (Guid.Parse (identity.DriverId) != expectedId)
				Issue ("driver-id", "Package driver GUID differs from the release candidate.");
			if (Version.Parse (identity.Version) != expectedVersion)
				Issue ("driver-version", "Package driver version differs from the release candidate.");
			input.Position = 0;
			using var zip = new ZipArchive (input, ZipArchiveMode.Read, true);
			if (zip.Entries.Count > 4096)
				Issue ("archive-size", "The package contains more than 4096 archive entries.");
			if (zip.Entries.GroupBy (entry => entry.FullName, StringComparer.OrdinalIgnoreCase).Any (group => group.Count () > 1))
				Issue ("duplicate-entry", "The archive contains duplicate or case-colliding entry names.");
			if (zip.Entries.Any (entry => entry.FullName.Contains ('\\') || entry.FullName.StartsWith ('/') ||
				entry.FullName.Contains (':') || entry.FullName.Split ('/').Any (part => part is "." or "..")))
				Issue ("archive-path", "Archive paths must be relative forward-slash paths without traversal components.");
			if (zip.Entries.Any (entry => Path.GetFileName (entry.FullName).Equals ("LiveTestSettings.json", StringComparison.OrdinalIgnoreCase) ||
				Path.GetFileName (entry.FullName).Equals ("wiserkeys.params", StringComparison.OrdinalIgnoreCase)))
				Issue ("private-test-input", "The package contains a known private test-input filename. Remove test inputs before submission.");

			if (zip.Entries.Any (entry => entry.FullName.EndsWith (".pdf", StringComparison.OrdinalIgnoreCase) &&
				string.IsNullOrWhiteSpace (Path.GetFileNameWithoutExtension (entry.FullName.Replace ('\\', '/')))))
				Issue ("unnamed-document", "Every packaged PDF, including supporting documents, must have a nonempty filename before its extension.");

			foreach (var extension in new[] { ".dll", ".dat", ".pdf" })
				{
				var expectedName = basename + extension;
				if (zip.Entries.Count (entry => entry.FullName.Equals (expectedName, StringComparison.Ordinal)) != 1)
					Issue ("matching-" + extension[1..], "Expected exactly one root " + extension + " file whose basename and case match the package.");
				}

			var manifest = zip.Entries.Single (entry => !entry.FullName.Contains ('/') && !entry.FullName.Contains ('\\') && entry.FullName.EndsWith (".dat", StringComparison.OrdinalIgnoreCase));
			using var manifestStream = manifest.Open ();
			using var document = JsonDocument.Parse (manifestStream);
			var metadata = document.RootElement;
			static bool HasText (JsonElement element, string key) => element.TryGetProperty (key, out var value) &&
				value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace (value.GetString ());
			var hasContact = metadata.TryGetProperty ("developerContact", out var contact) && contact.ValueKind == JsonValueKind.Object;
			if (!HasText (metadata, "developer") || !hasContact || !HasText (contact, "company"))
				Issue ("developer-metadata", "Generated package metadata must identify the developer and developer contact company.");
			if (hasEmail && (!hasContact || !HasText (contact, "email") || !contact.GetProperty ("email").GetString ()!.Equals (requirements.PublicSupportEmail, StringComparison.OrdinalIgnoreCase)))
				Issue ("support-email", "Generated package metadata must contain the approved public support email, not a private submission address.");
			if (hasWebsite && (!hasContact || !HasText (contact, "website") || !contact.GetProperty ("website").GetString ()!.Equals (requirements.PublicSupportWebsite, StringComparison.Ordinal)))
				Issue ("support-website", "Generated package metadata must contain the approved public support website.");
			if (!metadata.TryGetProperty ("dependencyGroup", out var dependency) || dependency.ValueKind != JsonValueKind.String)
				Issue ("dependency-group", "Generated package metadata must contain a dependencyGroup string; an empty string is valid for a standalone driver.");
			if (!metadata.TryGetProperty ("assemblyFileName", out var assembly) || assembly.ValueKind != JsonValueKind.String ||
				assembly.GetString () != basename + ".dll")
				Issue ("assembly-reference", "Manifest assemblyFileName must exactly match the packaged driver DLL.");

			var pdf = zip.Entries.FirstOrDefault (entry => entry.FullName == basename + ".pdf");
			if (pdf != null)
				{
				using var pdfStream = pdf.Open ();
				Span<byte> header = stackalloc byte[5];
				if (pdf.Length < 5 || pdfStream.ReadAtLeast (header, header.Length, false) != header.Length || Encoding.ASCII.GetString (header) != "%PDF-")
					Issue ("help-pdf-header", "The matching help file does not have a PDF header.");
				}
			}
		catch (Exception exception) when (exception is InvalidDataException or JsonException or InvalidOperationException or FormatException)
			{
			Issue ("invalid-package", "The package archive or generated manifest is invalid or incomplete.");
			}
		return new (fileName, digest, identity, issues.AsReadOnly ());
		}
	}