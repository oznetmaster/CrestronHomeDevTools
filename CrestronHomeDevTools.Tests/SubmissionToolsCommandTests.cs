// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class SubmissionToolsCommandTests
	{
	private string _root = null!;
	private byte[] _manifest = null!;

	[SetUp]
	public void SetUp ()
		{
		_root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "submission-runtime-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (Path.Combine (_root, "scripts"));
		File.WriteAllText (Path.Combine (_root, "scripts", "entry.py"), "pinned fixture");
		_manifest = JsonSerializer.SerializeToUtf8Bytes (new
			{
			schemaVersion = 1,
			platform = "win-x64",
			files = new[] { new { path = "scripts/entry.py", sha256 = Convert.ToHexStringLower (SHA256.HashData (File.ReadAllBytes (Path.Combine (_root, "scripts", "entry.py")))) } }
			});
		}

	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);

	private Task Verify (byte[]? manifest = null, CancellationToken token = default) =>
		SubmissionToolsCommand.VerifyBundleAsync (_root, new MemoryStream (manifest ?? _manifest), token);

	[Test]
	public async Task MatchingFilesPassAndDiskManifestCannotOverrideEmbeddedInventory ()
		{
		File.WriteAllText (Path.Combine (_root, "manifest.json"), "untrusted disk manifest");
		await Verify ();
		File.WriteAllText (Path.Combine (_root, "scripts", "entry.py"), "changed fixture");
		Assert.ThrowsAsync<InvalidDataException> (() => Verify ());
		}

	[Test]
	public void UnlistedModuleIsRejectedBeforeInterpreterStarts ()
		{
		File.WriteAllText (Path.Combine (_root, "scripts", "json.py"), "unexpected shadow module");
		Assert.ThrowsAsync<InvalidDataException> (() => Verify ());
		}

	[Test]
	public void MissingPinnedFileIsRejected ()
		{
		File.Delete (Path.Combine (_root, "scripts", "entry.py"));
		Assert.CatchAsync<IOException> (() => Verify ());
		}

	[TestCase ("../escape.py")]
	[TestCase ("scripts/../escape.py")]
	[TestCase ("/escape.py")]
	[TestCase ("C:/escape.py")]
	[TestCase ("scripts/entry.py:stream")]
	[TestCase ("scripts//entry.py")]
	[TestCase ("scripts/entry.py.")]
	[TestCase ("scripts/entry.py ")]
	[TestCase ("scripts\\entry.py")]
	public void UnsafeInventoryPathsAreRejected (string path)
		{
		byte[] manifest = JsonSerializer.SerializeToUtf8Bytes (new
			{
			schemaVersion = 1, platform = "win-x64", files = new[] { new { path, sha256 = new string ('a', 64) } }
			});
		Assert.ThrowsAsync<InvalidDataException> (() => Verify (manifest));
		}

	[TestCase ("{}")]
	[TestCase ("{\"schemaVersion\":1,\"platform\":\"win-x64\",\"files\":null}")]
	[TestCase ("{\"schemaVersion\":1,\"platform\":\"win-x64\",\"files\":[null]}")]
	[TestCase ("{\"schemaVersion\":1,\"platform\":\"win-x64\",\"files\":[{}]}")]
	public void IncompleteInventoriesHaveActionableFailure (string json) =>
		Assert.ThrowsAsync<InvalidDataException> (() => Verify (Encoding.UTF8.GetBytes (json)));

	[Test]
	public void CaseInsensitiveDuplicateIsRejected ()
		{
		using var document = JsonDocument.Parse (_manifest);
		var file = document.RootElement.GetProperty ("files")[0];
		var hash = file.GetProperty ("sha256").GetString ();
		byte[] manifest = JsonSerializer.SerializeToUtf8Bytes (new
			{
			schemaVersion = 1, platform = "win-x64",
			files = new[] { new { path = "scripts/entry.py", sha256 = hash }, new { path = "SCRIPTS/ENTRY.PY", sha256 = hash } }
			});
		Assert.ThrowsAsync<InvalidDataException> (() => Verify (manifest));
		}

	[Test]
	public void CancellationStopsVerification () =>
		Assert.CatchAsync<OperationCanceledException> (() => Verify (token: new CancellationToken (canceled: true)));

	[Test]
	public async Task SourceOnlyBuildReportsMissingRuntimeWithoutUnhandledException ()
		{
		Assert.That (typeof (SubmissionToolsCommand).Assembly.GetManifestResourceNames (), Does.Not.Contain ("SubmissionRuntimeManifest"));
		Assert.That (await SubmissionToolsCommand.RunAsync (["runtime-check"], CancellationToken.None), Is.EqualTo (2));
		}
	}