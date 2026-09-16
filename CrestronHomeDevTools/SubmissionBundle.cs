// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO.Compression;
using System.Security.Cryptography;

namespace CrestronHomeDevTools;

public sealed record SubmissionBundleReport (string BundleSha256, int FileCount, SubmissionValidationReport Validation)
	{
	public bool ValidationChecksPassed => Validation.ValidationChecksPassed;
	}

/// <summary>Creates and verifies private evidence archives. Digests detect changes; they do not authenticate the test producer.</summary>
public static class SubmissionBundle
	{
	private const int MAX_FILES = 4096;
	private const long MAX_FILE_BYTES = 64L * 1024 * 1024;
	private const long MAX_TOTAL_BYTES = 512L * 1024 * 1024;
	private static readonly string[] RequiredFiles = ["candidate.json", "policy.json", "template.pdf", "observations.json"];

	/// <summary>Only referenced evidence is copied. Output is created once, after validating the archive's own bytes.</summary>
	public static SubmissionBundleReport Create (string outputPath, string candidatePath, string expectedCandidateSha256,
		string packagePath, string policyPath, string templatePath, string observationsPath, string evidenceDirectory,
		DateTimeOffset now, CancellationToken cancellationToken = default)
		{
		RequireHash (expectedCandidateSha256);
		outputPath = Path.GetFullPath (outputPath);
		if (File.Exists (outputPath) || Directory.Exists (outputPath)) throw new IOException ("The bundle output already exists; use a new path.");
		using var scratch = new Scratch (Path.GetDirectoryName (outputPath)!);
		var files = new SortedDictionary<string, string> (StringComparer.Ordinal);
		long total = 0;
		void Copy (string source, string name)
			{
			if (!PortablePath (name) || files.Keys.Any (key => key.Equals (name, StringComparison.OrdinalIgnoreCase)))
				throw new InvalidDataException ("Bundle filenames must be unique portable relative paths.");
			if (files.Count >= MAX_FILES) throw new InvalidDataException ("Too many submission files.");
			var destination = Path.Combine (scratch.DirectoryPath, "snapshot", name.Replace ('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory (Path.GetDirectoryName (destination)!);
			using var input = Open (source);
			using var output = new FileStream (destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			total += CopyLimited (input, output, Math.Min (Limit (name), MAX_TOTAL_BYTES - total), cancellationToken);
			files.Add (name, destination);
			}
		Copy (candidatePath, "candidate.json");
		using (var candidate = Open (files["candidate.json"]))
			if (!Hash (candidate).Equals (expectedCandidateSha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("Candidate differs from the trusted release digest.");
		Copy (packagePath, "package/" + Path.GetFileName (packagePath));
		Copy (policyPath, "policy.json");
		Copy (templatePath, "template.pdf");
		Copy (observationsPath, "observations.json");
		var evidenceRoot = Path.TrimEndingDirectorySeparator (Path.GetFullPath (evidenceDirectory));
		foreach (var relative in ReferencedFiles (files["observations.json"]))
			{
			if (!SubmissionEvidence.SafeEvidencePath (evidenceRoot, relative, out var source))
				throw new InvalidDataException ("Referenced evidence must be a readable file within the private evidence directory, without links.");
			Copy (source, "evidence/" + relative);
			}
		var pending = Path.Combine (scratch.DirectoryPath, "bundle.zip");
		using (var output = new FileStream (pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
		using (var zip = new ZipArchive (output, ZipArchiveMode.Create))
			foreach (var item in files)
				{
				cancellationToken.ThrowIfCancellationRequested ();
				var entry = zip.CreateEntry (item.Key, CompressionLevel.Fastest);
				entry.LastWriteTime = new DateTimeOffset (1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
				using var source = Open (item.Value);
				using var destination = entry.Open ();
				CopyLimited (source, destination, Limit (item.Key), cancellationToken);
				}
		SubmissionBundleReport report;
		using (var retained = Open (pending))
			{
			var digest = Hash (retained);
			retained.Position = 0;
			report = CheckArchive (retained, digest, expectedCandidateSha256, scratch.DirectoryPath, now, cancellationToken);
			if (!report.ValidationChecksPassed)
				throw new InvalidDataException ("The archived package/evidence failed submission validation; no bundle was published.");
			}
		cancellationToken.ThrowIfCancellationRequested ();
		// The random staging directory belongs to this invocation; never replace an existing bundle.
		File.Move (pending, outputPath, overwrite: false);
		return report;
		}

	/// <summary>Verify against digests retained independently by trusted CI. scratchDirectory must be private existing storage.</summary>
	public static SubmissionBundleReport Check (string bundlePath, string expectedBundleSha256, string expectedCandidateSha256,
		string scratchDirectory, DateTimeOffset now, CancellationToken cancellationToken = default)
		{
		RequireHash (expectedBundleSha256);
		RequireHash (expectedCandidateSha256);
		using var input = Open (bundlePath);
		if (input.Length > MAX_TOTAL_BYTES + 1024 * 1024) throw new InvalidDataException ("The submission archive is too large.");
		cancellationToken.ThrowIfCancellationRequested ();
		var digest = Hash (input);
		if (!digest.Equals (expectedBundleSha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException ("The submission archive differs from the independently retained bundle digest.");
		input.Position = 0;
		return CheckArchive (input, digest, expectedCandidateSha256, scratchDirectory, now, cancellationToken);
		}

	private static SubmissionBundleReport CheckArchive (Stream input, string digest, string candidateDigest, string parent,
		DateTimeOffset now, CancellationToken token)
		{
		using var scratch = new Scratch (parent);
		using var zip = new ZipArchive (input, ZipArchiveMode.Read, leaveOpen: true);
		if (zip.Entries.Count > MAX_FILES) throw new InvalidDataException ("Too many archive entries.");
		var names = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		long total = 0;
		foreach (var entry in zip.Entries)
			{
			token.ThrowIfCancellationRequested ();
			var name = entry.FullName;
			if (!PortablePath (name) || !names.Add (name) || entry.Length < 0 || entry.Length > Limit (name) || entry.Length > MAX_TOTAL_BYTES - total ||
				((entry.ExternalAttributes >> 16) & 0xF000) is not (0 or 0x8000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("Archive entries must be bounded, unique regular files with portable paths.");
			if (!RequiredFiles.Contains (name, StringComparer.Ordinal) && !name.StartsWith ("evidence/", StringComparison.Ordinal) &&
				!(name.StartsWith ("package/", StringComparison.Ordinal) && name.Count (c => c == '/') == 1 && name.EndsWith (".pkg", StringComparison.OrdinalIgnoreCase)))
				throw new InvalidDataException ("Unexpected file in submission archive.");
			var path = Path.Combine (scratch.DirectoryPath, name.Replace ('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory (Path.GetDirectoryName (path)!);
			using var source = entry.Open ();
			using var destination = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			var copied = CopyLimited (source, destination, Math.Min (Limit (name), MAX_TOTAL_BYTES - total), token);
			if (copied != entry.Length) throw new InvalidDataException ("Archive entry length differs from its contents.");
			total += copied;
			}
		if (RequiredFiles.Any (required => !names.Contains (required)) || names.Count (name => name.StartsWith ("package/", StringComparison.Ordinal)) != 1)
			throw new InvalidDataException ("The archive needs one package and all required submission documents.");
		string FilePath (string name) => Path.Combine (scratch.DirectoryPath, name.Replace ('/', Path.DirectorySeparatorChar));
		var expectedNames = RequiredFiles.Concat (names.Where (name => name.StartsWith ("package/", StringComparison.Ordinal)))
			.Concat (ReferencedFiles (FilePath ("observations.json")).Select (name => "evidence/" + name)).ToHashSet (StringComparer.Ordinal);
		if (!expectedNames.SetEquals (names)) throw new InvalidDataException ("Archive evidence must exactly match the referenced files; extra or missing files are rejected.");
		var evidence = FilePath ("evidence");
		Directory.CreateDirectory (evidence);
		var report = SubmissionValidation.CheckFiles (FilePath ("candidate.json"), candidateDigest,
			FilePath (names.Single (name => name.StartsWith ("package/", StringComparison.Ordinal))), FilePath ("policy.json"),
			FilePath ("template.pdf"), FilePath ("observations.json"), evidence, now, token);
		return new (digest, names.Count, report);
		}

	private static IReadOnlyList<string> ReferencedFiles (string observationsPath)
		{
		var document = SubmissionValidation.ReadFile<SubmissionEvidenceDocument> (observationsPath);
		if (document.SchemaVersion != 1) throw new InvalidDataException ("Unsupported observation document.");
		var names = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);
		foreach (var observation in document.Observations)
			{
			if (observation?.Files == null) throw new InvalidDataException ("Observation files must not be null.");
			foreach (var file in observation.Files)
				{
				var relative = file?.RelativePath?.Replace ('\\', '/');
				if (relative == null || !PortablePath (relative)) throw new InvalidDataException ("Unsafe evidence path.");
				if (names.TryGetValue (relative, out var existing) && existing != relative)
					throw new InvalidDataException ("Evidence paths differ only by case.");
				names[relative] = relative;
				if (names.Count > MAX_FILES - 5) throw new InvalidDataException ("Too many referenced evidence files.");
				}
			}
		return names.Keys.Order (StringComparer.Ordinal).ToArray ();
		}

	private static bool PortablePath (string name)
		{
		if (string.IsNullOrWhiteSpace (name) || name.Length > 1024 || name.Any (c => char.IsControl (c) || "\\:*?\"<>|".Contains (c))) return false;
		return name.Split ('/').All (part => part is not ("" or "." or "..") && !part.EndsWith ('.') && !part.EndsWith (' ') && !ReservedName (part));
		}
	private static bool ReservedName (string part)
		{
		var stem = part.Split ('.')[0].TrimEnd (' ').ToUpperInvariant ();
		return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
			stem.Length == 4 && (stem.StartsWith ("COM", StringComparison.Ordinal) || stem.StartsWith ("LPT", StringComparison.Ordinal)) &&
			"123456789¹²³".Contains (stem[3]);
		}
	private static long Limit (string name) => name is "candidate.json" or "policy.json" or "observations.json" ? 16L * 1024 * 1024 : MAX_FILE_BYTES;
	private static long CopyLimited (Stream input, Stream output, long limit, CancellationToken token)
		{
		long count = 0;
		var buffer = new byte[65536];
		while (true)
			{
			token.ThrowIfCancellationRequested ();
			var read = input.Read (buffer);
			if (read == 0) return count;
			count += read;
			if (count > limit) throw new InvalidDataException ("Submission file or archive exceeds its size limit.");
			output.Write (buffer, 0, read);
			}
		}
	private static FileStream Open (string path) => new (path, FileMode.Open, FileAccess.Read, FileShare.Read);
	private static string Hash (Stream input) => Convert.ToHexString (SHA256.HashData (input)).ToLowerInvariant ();
	private static void RequireHash (string digest)
		{
		if (digest?.Length != 64 || !digest.All (char.IsAsciiHexDigit)) throw new ArgumentException ("An independently retained SHA-256 digest is required.");
		}
	private sealed class Scratch : IDisposable
		{
		private readonly string _parent;
		public string DirectoryPath { get; }
		public Scratch (string parent)
			{
			_parent = Path.TrimEndingDirectorySeparator (Path.GetFullPath (parent));
			if (!Directory.Exists (_parent)) throw new DirectoryNotFoundException ("Provide an existing private scratch/output directory.");
			DirectoryPath = Path.Combine (_parent, ".submission-" + Guid.NewGuid ().ToString ("N"));
			Directory.CreateDirectory (DirectoryPath);
			}
		public void Dispose ()
			{
			// Delete only the invocation's own random child, never a caller-supplied directory.
			if (Path.GetDirectoryName (Path.GetFullPath (DirectoryPath)) != _parent || !Path.GetFileName (DirectoryPath).StartsWith (".submission-", StringComparison.Ordinal))
				throw new IOException ("Invalid scratch cleanup target.");
			if ((File.GetAttributes (DirectoryPath) & FileAttributes.ReparsePoint) != 0) throw new IOException ("Scratch directory became a link; cleanup refused.");
			Directory.Delete (DirectoryPath, recursive: true);
			}
		}
	}