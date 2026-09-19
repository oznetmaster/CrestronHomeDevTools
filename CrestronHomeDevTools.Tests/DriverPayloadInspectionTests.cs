// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class DriverPayloadInspectionTests
	{
	private const string ROOT = "/user/Data/UsedThirdPartyDrivers/example.platform.ip.developer/1.002.0003.0000";
	private const string KEY = "example.platform.ip.developer";
	private static readonly Dictionary<string, byte[]> Payload = new ()
		{
		["Example.dll"] = "synthetic assembly; never loaded"u8.ToArray (),
		["Example.dat"] = JsonSerializer.SerializeToUtf8Bytes (new { driverId = "0e124d74-b0de-4a93-9eee-a966bca51660", baseModel = "Example Platform", manufacturer = "Example", driverVersion = "1.002.0003.0000" }),
		["Translations/en-US.json"] = "{}"u8.ToArray ()
		};

	private static MemoryStream Package (Action<ZipArchive>? alter = null)
		{
		var data = new MemoryStream ();
		using (var zip = new ZipArchive (data, ZipArchiveMode.Create, true))
			{
			foreach (var (name, bytes) in Payload)
				{
				using var stream = zip.CreateEntry (name).Open ();
				stream.Write (bytes);
				}
			alter?.Invoke (zip);
			}
		data.Position = 0;
		return data;
		}

	private static async Task<DriverPayloadInspection.PackagePayload> Prepared (Action<ZipArchive>? alter = null)
		{
		using var package = Package (alter);
		return await DriverPayloadInspection.ReadPackageAsync (package, Convert.ToHexString (SHA256.HashData (package.ToArray ())), default);
		}

	[Test]
	public async Task ExactBytesAndPathsMatchWithoutExposingContents ()
		{
		var expected = await Prepared ();
		var source = new Source ();
		var result = await DriverPayloadInspection.CompareCoreAsync (expected, KEY, source, default);
		Assert.That (result.Package, Is.EqualTo (expected.Identity));
		Assert.That (result.PackageSha256, Is.EqualTo (expected.Sha256));
		Assert.That (result.Directory, Is.EqualTo (ROOT));
		Assert.That (result.Files.Count, Is.EqualTo (3));
		Assert.That (source.Opened, Is.EquivalentTo (Payload.Keys.Select (path => ROOT + "/" + path)));
		Assert.That (JsonSerializer.Serialize (result), Does.Not.Contain ("synthetic assembly; never loaded"));
		}

	[Test]
	public void IncorrectPackagePinIsRejectedBeforeRemoteAccess ()
		{
		using var package = Package ();
		Assert.ThrowsAsync<InvalidDataException> (() => DriverPayloadInspection.ReadPackageAsync (package, new string ('0', 64), default));
		}

	[TestCase ("chdriver.example.platform.ip.developer.1.002.0003.0000")]
	[TestCase ("chdriver.example.platform.ip.developer.1.2.3.0")]
	public async Task ConfigurationCatalogueIdResolvesToUnversionedStorageKey (string catalogueId)
		{
		var source = new Source ();
		var result = await DriverPayloadInspection.CompareCoreAsync (await Prepared (), catalogueId, source, default);
		Assert.That (result.CatalogueId, Is.EqualTo (catalogueId));
		Assert.That (result.Directory, Is.EqualTo (ROOT));
		Assert.That (source.Opened, Is.EquivalentTo (Payload.Keys.Select (path => ROOT + "/" + path)));
		}

	[TestCase ("chdriver.example.platform.ip.developer.1.002.0004.0000")]
	[TestCase ("chdriver.example.platform.ip.developer.1.2.3")]
	[TestCase ("chdriver.1.002.0003.0000")]
	[TestCase ("chdriver..1.002.0003.0000")]
	public async Task IncorrectOrIncompleteCatalogueVersionIsRejected (string catalogueId)
		{
		var expected = await Prepared ();
		var source = new Source ();
		Assert.ThrowsAsync<InvalidDataException> (() => DriverPayloadInspection.CompareCoreAsync (expected, catalogueId, source, default));
		Assert.That (source.Opened, Is.Empty);
		}

	[TestCase ("../outside.txt")]
	[TestCase ("/outside.txt")]
	[TestCase ("folder/../outside.txt")]
	[TestCase ("folder//file.txt")]
	[TestCase ("C:/file.txt")]
	[TestCase ("Translations/EN-US.json")]
	[TestCase ("Example.dll/child")]
	public void UnsafeDuplicateOrCollidingCandidateEntryIsRejected (string path) =>
		Assert.ThrowsAsync<InvalidDataException> (() => Prepared (zip => zip.CreateEntry (path)));

	[Test]
	public void CandidateSymlinkIsRejected () => Assert.ThrowsAsync<InvalidDataException> (() =>
		Prepared (zip => zip.CreateEntry ("link").ExternalAttributes = 0xA1FF << 16));

	[TestCase ("changed")]
	[TestCase ("missing")]
	[TestCase ("extra")]
	[TestCase ("case")]
	public async Task SameVersionDoesNotHideDifferentPayload (string change)
		{
		var expected = await Prepared ();
		var source = new Source ();
		switch (change)
			{
			case "changed": source.Files["Translations/en-US.json"] = "[]"u8.ToArray (); break;
			case "missing": source.Files.Remove ("Translations/en-US.json"); break;
			case "extra": source.Files.Add ("unexpected.cfg", "private"u8.ToArray ()); break;
			case "case": source.Files.Add ("Example.DLL", source.Files["Example.dll"]); source.Files.Remove ("Example.dll"); break;
			}
		Assert.ThrowsAsync<InvalidDataException> (() => DriverPayloadInspection.CompareCoreAsync (expected, KEY, source, default));
		Assert.That (source.Opened, Does.Not.Contain (ROOT + "/unexpected.cfg"));
		}

	[TestCase (0)]
	[TestCase (1)]
	[TestCase (2)]
	public async Task LinksInCatalogueVersionOrFileAreRejected (int level)
		{
		var expected = await Prepared ();
		var source = new Source { Link = level == 0 ? ROOT[..ROOT.LastIndexOf ('/')] : level == 1 ? ROOT : ROOT + "/Example.dll" };
		Assert.ThrowsAsync<InvalidDataException> (() => DriverPayloadInspection.CompareCoreAsync (expected, KEY, source, default));
		Assert.That (source.Opened, Is.Empty);
		}

	[TestCase ("../other")]
	[TestCase ("/user/other")]
	[TestCase ("..")]
	[TestCase ("bad\\other")]
	public async Task UnsafeCatalogueIdNeverReadsRemoteFiles (string id)
		{
		var expected = await Prepared ();
		var source = new Source ();
		Assert.ThrowsAsync<ArgumentException> (() => DriverPayloadInspection.CompareCoreAsync (expected, id, source, default));
		Assert.That (source.Listed, Is.Empty);
		}

	[TestCase (true)]
	[TestCase (false)]
	public async Task GrowingOrTruncatedRemoteStreamIsRejected (bool grow)
		{
		var expected = await Prepared ();
		var source = new Source { AlterStream = grow ? [1, 2, 3] : [1] };
		Assert.ThrowsAsync<InvalidDataException> (() => DriverPayloadInspection.CompareCoreAsync (expected, KEY, source, default));
		}

	[Test]
	public async Task CancellationDoesNotContinueRemoteInspection ()
		{
		var expected = await Prepared ();
		var source = new Source ();
		Assert.CatchAsync<OperationCanceledException> (() => DriverPayloadInspection.CompareCoreAsync (expected, KEY, source, new CancellationToken (true)));
		Assert.That (source.Opened, Is.Empty);
		}

	private sealed class Source : DriverPayloadInspection.IFileSource
		{
		internal Dictionary<string, byte[]> Files = Payload.ToDictionary (entry => entry.Key, entry => entry.Value);
		internal List<string> Opened = [];
		internal List<string> Listed = [];
		internal string? Link;
		internal byte[]? AlterStream;

		public Task<IReadOnlyList<DriverPayloadInspection.Entry>> ListAsync (string directory, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Listed.Add (directory);
			var entries = new Dictionary<string, DriverPayloadInspection.Entry> (StringComparer.Ordinal);
			foreach (var (relative, bytes) in Files)
				{
				var full = ROOT + "/" + relative;
				if (!full.StartsWith (directory + "/", StringComparison.Ordinal)) continue;
				var rest = full[(directory.Length + 1)..];
				var slash = rest.IndexOf ('/');
				var path = slash >= 0 ? directory + "/" + rest[..slash] : full;
				entries[path] = new (path, slash >= 0, slash < 0, path == Link, slash < 0 ? bytes.Length : 0);
				}
			return Task.FromResult<IReadOnlyList<DriverPayloadInspection.Entry>> (entries.Values.ToArray ());
			}

		public Task<Stream> OpenReadAsync (string path, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Opened.Add (path);
			return Task.FromResult<Stream> (new MemoryStream (path.EndsWith ("en-US.json", StringComparison.Ordinal) && AlterStream != null
				? AlterStream : Files[path[(ROOT.Length + 1)..]]));
			}
		}
	}