// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

using Renci.SshNet;

namespace CrestronHomeDevTools;

public sealed record DriverPayloadFile (long Bytes, string Sha256);
public sealed record DriverPayloadMatch (DriverPackageInfo Package, string PackageSha256, string CatalogueId,
	string Directory, IReadOnlyDictionary<string, DriverPayloadFile> Files, DateTimeOffset ObservedUtc);

/// <summary>
/// Compares a pinned package with its extracted processor files, without deployment or modification.
/// The caller must separately verify the installed instance and hold its processor reservation.
/// This comparison is not an attestation of the process's loaded memory or an installation mapping.
/// </summary>
public static class DriverPayloadInspection
	{
	private const string PAYLOAD_ROOT = "/user/Data/UsedThirdPartyDrivers";
	private const int MAXIMUM_ENTRIES = 2048;
	private const long MAXIMUM_FILE_BYTES = 64 * 1024 * 1024;
	private const long MAXIMUM_TOTAL_BYTES = 256 * 1024 * 1024;

	public static async Task<DriverPayloadMatch> CompareAsync (string host, NetworkCredential credential,
		string sshFingerprint, string packagePath, string expectedSha256, string catalogueId,
		TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		ArgumentException.ThrowIfNullOrWhiteSpace (host);
		ArgumentNullException.ThrowIfNull (credential);
		ArgumentException.ThrowIfNullOrWhiteSpace (sshFingerprint);
		ValidateCatalogueId (catalogueId);
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (10))
			throw new ArgumentOutOfRangeException (nameof (timeout));
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (timeout);
		// Hold the original package open throughout comparison; no build may replace it.
		await using var input = new FileStream (packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		var package = await ReadPackageAsync (input, expectedSha256, deadline.Token).ConfigureAwait (false);
		using var sftp = new SftpClient (host, credential.UserName, credential.Password);
		sftp.ConnectionInfo.Timeout = TimeSpan.FromSeconds (10);
		sftp.OperationTimeout = TimeSpan.FromSeconds (20);
		sftp.HostKeyReceived += (_, e) => e.CanTrust = e.FingerPrintSHA256 == sshFingerprint;
		await sftp.ConnectAsync (deadline.Token).ConfigureAwait (false);
		return await CompareCoreAsync (package, catalogueId, new SftpSource (sftp), deadline.Token).ConfigureAwait (false);
		}

	internal sealed record PackagePayload (DriverPackageInfo Identity, string Sha256, IReadOnlyDictionary<string, DriverPayloadFile> Files);
	internal sealed record Entry (string Path, bool Directory, bool Regular, bool Link, long Bytes);
	internal interface IFileSource
		{
		Task<IReadOnlyList<Entry>> ListAsync (string directory, CancellationToken token);
		Task<Stream> OpenReadAsync (string path, CancellationToken token);
		}

	internal static async Task<PackagePayload> ReadPackageAsync (Stream input, string expectedSha256, CancellationToken token)
		{
		if (!input.CanSeek || input.Length is <= 0 or > MAXIMUM_FILE_BYTES || expectedSha256?.Length != 64 || !expectedSha256.All (char.IsAsciiHexDigit))
			throw new ArgumentException ("Provide a nonempty package no larger than 64 MiB and its trusted SHA-256.");
		input.Position = 0;
		var hash = Convert.ToHexString (await SHA256.HashDataAsync (input, token).ConfigureAwait (false));
		if (!hash.Equals (expectedSha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException ("The candidate package differs from its trusted hash.");
		input.Position = 0;
		var identity = DriverDeployment.Inspect (input);
		if (!Version.TryParse (identity.Version, out var version) || version.Revision < 0 || !identity.Version.All (c => char.IsAsciiDigit (c) || c == '.'))
			throw new InvalidDataException ("The candidate must identify a four-part processor version.");
		input.Position = 0;
		using var archive = new ZipArchive (input, ZipArchiveMode.Read, leaveOpen: true);
		if (archive.Entries.Count is 0 or > MAXIMUM_ENTRIES)
			throw new InvalidDataException ("Candidate payload has too many entries.");
		var files = new Dictionary<string, DriverPayloadFile> (StringComparer.Ordinal);
		var names = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		long total = 0;
		foreach (var entry in archive.Entries)
			{
			token.ThrowIfCancellationRequested ();
			var path = entry.FullName.Replace ('\\', '/');
			bool directory = path.EndsWith ('/');
			var name = directory ? path[..^1] : path;
			if (!SafeRelative (name) || !names.Add (name) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 ||
				entry.Length < 0 || entry.Length > MAXIMUM_FILE_BYTES || (total += entry.Length) > MAXIMUM_TOTAL_BYTES || directory && entry.Length != 0)
				throw new InvalidDataException ("Candidate payload contains an unsafe, duplicate or oversized entry.");
			if (directory) continue;
			await using var content = entry.Open ();
			files.Add (path, new (entry.Length, await HashAsync (content, entry.Length, token).ConfigureAwait (false)));
			}
		var fileNames = new HashSet<string> (files.Keys, StringComparer.OrdinalIgnoreCase);
		foreach (var path in names)
			for (int slash = path.IndexOf ('/'); slash >= 0; slash = path.IndexOf ('/', slash + 1))
				if (fileNames.Contains (path[..slash]))
					throw new InvalidDataException ("Candidate payload contains a file/directory collision.");
		return new (identity, hash, files.AsReadOnly ());
		}

	internal static async Task<DriverPayloadMatch> CompareCoreAsync (PackagePayload package, string catalogueId, IFileSource source, CancellationToken token)
		{
		ValidateCatalogueId (catalogueId);
		var catalogue = PAYLOAD_ROOT + "/" + StorageKey (catalogueId, package.Identity.Version);
		var root = catalogue + "/" + package.Identity.Version;
		await RequireDirectoryAsync (source, PAYLOAD_ROOT, catalogue, token).ConfigureAwait (false);
		await RequireDirectoryAsync (source, catalogue, root, token).ConfigureAwait (false);
		var observed = new Dictionary<string, DriverPayloadFile> (StringComparer.Ordinal);
		var seen = new HashSet<string> (StringComparer.OrdinalIgnoreCase);
		var pending = new Stack<string> ();
		pending.Push (root);
		int count = 0;
		long total = 0;
		while (pending.TryPop (out var directory))
			{
			foreach (var entry in await source.ListAsync (directory, token).ConfigureAwait (false))
				{
				if (++count > MAXIMUM_ENTRIES || entry.Link || !entry.Path.StartsWith (directory + "/", StringComparison.Ordinal) ||
					entry.Path[(directory.Length + 1)..].Contains ('/') || !entry.Path.StartsWith (root + "/", StringComparison.Ordinal) ||
					!SafeRelative (entry.Path[(root.Length + 1)..]) || !seen.Add (entry.Path))
					throw new InvalidDataException ("Unexpected or linked processor payload layout.");
				if (entry.Directory)
					{
					pending.Push (entry.Path);
					continue;
					}
				var relative = entry.Path[(root.Length + 1)..];
				if (!entry.Regular || entry.Bytes < 0 || entry.Bytes > MAXIMUM_FILE_BYTES || (total += entry.Bytes) > MAXIMUM_TOTAL_BYTES ||
					!package.Files.TryGetValue (relative, out var expected) || entry.Bytes != expected.Bytes)
					throw new InvalidDataException ("Processor payload file inventory differs from the candidate.");
				await using var content = await source.OpenReadAsync (entry.Path, token).ConfigureAwait (false);
				var hash = await HashAsync (content, entry.Bytes, token).ConfigureAwait (false);
				if (!hash.Equals (expected.Sha256, StringComparison.Ordinal))
					throw new InvalidDataException ("Processor payload bytes differ from the candidate.");
				observed.Add (relative, new (entry.Bytes, hash));
				}
			}
		if (observed.Count != package.Files.Count)
			throw new InvalidDataException ("Processor payload is missing candidate files.");
		return new (package.Identity, package.Sha256, catalogueId, root, observed.AsReadOnly (), DateTimeOffset.UtcNow);
		}

	private static async Task RequireDirectoryAsync (IFileSource source, string parent, string path, CancellationToken token)
		{
		var entries = (await source.ListAsync (parent, token).ConfigureAwait (false)).Where (entry => entry.Path == path).ToArray ();
		if (entries.Length != 1 || !entries[0].Directory || entries[0].Link)
			throw new InvalidDataException ("The selected processor payload directory is missing, ambiguous or linked.");
		}

	private static void ValidateCatalogueId (string catalogueId)
		{
		if (string.IsNullOrWhiteSpace (catalogueId) || catalogueId.Length > 256 || catalogueId is "." or ".." ||
			catalogueId.Any (c => char.IsControl (c) || c is '/' or '\\' or ':'))
			throw new ArgumentException ("Provide the exact catalogue ID as one ordinary directory name.");
		}

	private static string StorageKey (string catalogueId, string packageVersion)
		{
		// Configuration catalogue IDs include a namespace and version. Extracted
		// files use the unversioned driver key, then a separate version directory.
		// Retain support for callers which already supply that storage key.
		const string prefix = "chdriver.";
		if (!catalogueId.StartsWith (prefix, StringComparison.Ordinal)) return catalogueId;
		var parts = catalogueId[prefix.Length..].Split ('.');
		if (parts.Length <= 4 || parts.Any (string.IsNullOrEmpty) ||
			!Version.TryParse (string.Join ('.', parts[^4..]), out var selectedVersion) ||
			!Version.TryParse (packageVersion, out var expectedVersion) || selectedVersion != expectedVersion)
			throw new InvalidDataException ("The selected catalogue ID must identify the candidate's four-part version.");
		return string.Join ('.', parts[..^4]);
		}

	private static bool SafeRelative (string path) => path.Length is > 0 and <= 512 && !path.Any (c => char.IsControl (c) || c is '\\' or ':') &&
		path.Split ('/').All (part => part is not ("" or "." or ".."));

	private static async Task<string> HashAsync (Stream input, long expectedBytes, CancellationToken token)
		{
		using var hash = IncrementalHash.CreateHash (HashAlgorithmName.SHA256);
		var buffer = new byte[65536];
		long total = 0;
		int read;
		while ((read = await input.ReadAsync (buffer.AsMemory (), token).ConfigureAwait (false)) != 0)
			{
			if ((total += read) > expectedBytes || total > MAXIMUM_FILE_BYTES)
				throw new InvalidDataException ("Payload stream exceeds its declared length.");
			hash.AppendData (buffer, 0, read);
			}
		if (total != expectedBytes)
			throw new InvalidDataException ("Payload stream ended before its declared length.");
		return Convert.ToHexString (hash.GetHashAndReset ());
		}

	private sealed class SftpSource (SftpClient client) : IFileSource
		{
		public async Task<IReadOnlyList<Entry>> ListAsync (string directory, CancellationToken token)
			{
			var result = new List<Entry> ();
			await foreach (var entry in client.ListDirectoryAsync (directory, token).ConfigureAwait (false))
				{
				if (entry.Name is "." or "..") continue;
				if (result.Count >= MAXIMUM_ENTRIES)
					throw new InvalidDataException ("Processor directory exceeds the inspection limit.");
				result.Add (new (entry.FullName, entry.IsDirectory, entry.IsRegularFile, entry.IsSymbolicLink, entry.Length));
				}
			return result;
			}

		public async Task<Stream> OpenReadAsync (string path, CancellationToken token) =>
			await client.OpenAsync (path, FileMode.Open, FileAccess.Read, token).ConfigureAwait (false);
		}
	}