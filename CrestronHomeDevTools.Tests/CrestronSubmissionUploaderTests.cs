// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class CrestronSubmissionUploaderTests
	{
	private string _root = null!;
	private static readonly byte[] Package = "synthetic driver package"u8.ToArray ();
	private const string Filename = "Example_Test_IP.pkg", Origin = "https://uploader.crestron.com/";
	[SetUp]
	public void SetUp () { _root = Path.Combine (TestContext.CurrentContext.WorkDirectory, "upload-provider-" + Guid.NewGuid ().ToString ("N")); Directory.CreateDirectory (_root); }
	[TearDown]
	public void TearDown () => Directory.Delete (_root, recursive: true);
	private static string Hash (byte[] bytes) => Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant ();
	private CrestronSubmissionUploader Create (Handler handler, TimeSpan? timeout = null) => new (new NetworkCredential ("synthetic", "not-a-real-password"), Hash ("form"u8.ToArray ()), Hash ("terms"u8.ToArray ()), _root, timeout ?? TimeSpan.FromSeconds (5), handler);

	[TestCase ("valid")]
	[TestCase ("duplicate-existing")]
	public async Task UploadConfirmsDownloadedBytesAndRetainsPrivateEvidence (string mode)
		{
		var handler = new Handler { Mode = mode }; using var uploader = Create (handler);
		var result = await uploader.UploadAsync (new MemoryStream (Package), Filename);
		Assert.That (result.DownloadUrl, Is.EqualTo (Origin + "download.php?file=synthetic"));
		Assert.That (handler.Posts, Is.EqualTo (1));
		Assert.That (handler.Paths, Has.Count.EqualTo (5));
		string directory = Directory.GetDirectories (_root).Single ();
		using var verified = JsonDocument.Parse (File.ReadAllBytes (Path.Combine (directory, "verified.json")));
		Assert.That (verified.RootElement.GetProperty ("PackageSha256").GetString (), Is.EqualTo (Hash (Package)));
		Assert.That (File.ReadAllBytes (Path.Combine (directory, "archive.bin")), Is.EqualTo (Package));
		Assert.That (Directory.GetFiles (directory).All (file => !File.ReadAllText (file).Contains ("not-a-real-password")), Is.True);
		Assert.That (handler.FileBytes, Is.EqualTo (Package));
		Assert.That (handler.FormFields, Is.EqualTo (new[] { "from=Paste file url here", "operation=1", "operation=2", "agreecheck=on" }));
		}

	[TestCase ("changed-form", 0)]
	[TestCase ("changed-terms", 0)]
	[TestCase ("form-redirect", 0)]
	[TestCase ("post-redirect", 1)]
	[TestCase ("post-error", 1)]
	[TestCase ("lost-response", 1)]
	[TestCase ("wrong-filename", 1)]
	[TestCase ("external-link", 1)]
	[TestCase ("duplicate-link", 1)]
	[TestCase ("different-delete-id", 1)]
	[TestCase ("external-button", 1)]
	[TestCase ("unknown-button", 1)]
	[TestCase ("duplicate-query", 1)]
	[TestCase ("wrong-bytes", 1)]
	[TestCase ("extra-bytes", 1)]
	public void UnconfirmedResponsesNeverProduceAReceiptOrRetry (string mode, int posts)
		{
		var handler = new Handler { Mode = mode }; using var uploader = Create (handler);
		var error = Assert.ThrowsAsync<InvalidDataException> (() => uploader.UploadAsync (new MemoryStream (Package), Filename));
		Assert.That (handler.Posts, Is.EqualTo (posts));
		Assert.That (handler.Paths.All (path => path.StartsWith (Origin, StringComparison.Ordinal)), Is.True);
		string directory = Directory.GetDirectories (_root).Single ();
		Assert.That (File.Exists (Path.Combine (directory, "verified.json")), Is.False);
		using var failure = JsonDocument.Parse (File.ReadAllBytes (Path.Combine (directory, "failed.json")));
		Assert.That (failure.RootElement.GetProperty ("PostAttempted").GetBoolean (), Is.EqualTo (posts != 0));
		Assert.That (error!.Message, Does.Not.Contain ("synthetic-private-provider-error"));
		}

	[Test]
	public async Task RetainedResponseVerificationDoesNotRepeatPostOrEraseFailure ()
		{
		var handler = new Handler { Mode = "wrong-bytes" }; using var uploader = Create (handler);
		Assert.ThrowsAsync<InvalidDataException> (() => uploader.UploadAsync (new MemoryStream (Package), Filename));
		string original = Directory.GetDirectories (_root).Single ();
		byte[] failed = File.ReadAllBytes (Path.Combine (original, "failed.json"));
		handler.Mode = "valid";
		var receipt = await uploader.VerifyRetainedUploadAsync (new MemoryStream (Package), Filename, Path.GetFileName (original));
		Assert.That (receipt.DownloadUrl, Is.EqualTo (Origin + "download.php?file=synthetic"));
		Assert.That (handler.Posts, Is.EqualTo (1));
		Assert.That (File.ReadAllBytes (Path.Combine (original, "failed.json")), Is.EqualTo (failed));
		Assert.That (Directory.GetDirectories (_root, "verify-*").Length, Is.EqualTo (1));
		}

	[Test]
	public void RetainedResponseCannotVerifyDifferentPackageBytes ()
		{
		var handler = new Handler { Mode = "wrong-bytes" }; using var uploader = Create (handler);
		Assert.ThrowsAsync<InvalidDataException> (() => uploader.UploadAsync (new MemoryStream (Package), Filename));
		string original = Directory.GetDirectories (_root).Single (); int requests = handler.Paths.Count;
		Assert.ThrowsAsync<InvalidDataException> (() => uploader.VerifyRetainedUploadAsync (new MemoryStream ("different"u8.ToArray ()), Filename, Path.GetFileName (original)));
		Assert.That (handler.Paths.Count, Is.EqualTo (requests));
		}

	[Test]
	public void DeadlineStopsTheOnlyPendingRequest ()
		{
		var handler = new Handler { Mode = "hang" }; using var uploader = Create (handler, TimeSpan.FromSeconds (1));
		Assert.ThrowsAsync<InvalidDataException> (() => uploader.UploadAsync (new MemoryStream (Package), Filename));
		Assert.That (handler.Cancelled, Is.True); Assert.That (handler.Paths, Has.Count.EqualTo (1));
		}

	[Test]
	public async Task JournalBlocksRepeatedUploadAndPreservesUnknownOutcome ()
		{
		string receipts = Path.Combine (_root, "receipts"), journal = Path.Combine (_root, "journal"); Directory.CreateDirectory (receipts); Directory.CreateDirectory (journal);
		string package = Path.Combine (_root, Filename), form = Path.Combine (_root, "signed.pdf"); File.WriteAllBytes (package, Package); File.WriteAllText (form, "synthetic form");
		var plan = new SubmissionDeliveryPlan (new ('a', 64), new ('b', 64), new ('c', 64), Hash (Package), Hash (File.ReadAllBytes (form)), Filename, "signed.pdf", "sender@example.test", "recipient@example.test");
		var handler = new Handler { Mode = "wrong-bytes" }; using var uploader = new CrestronSubmissionUploader (new NetworkCredential ("synthetic", "fake"), Hash ("form"u8.ToArray ()), Hash ("terms"u8.ToArray ()), receipts, TimeSpan.FromSeconds (5), handler);
		var transport = new Transport (uploader);
		Assert.ThrowsAsync<InvalidDataException> (() => SubmissionDelivery.ExecuteAsync (journal, plan, package, form, transport));
		Assert.That (SubmissionDelivery.Read (journal, plan)!.State, Is.EqualTo (SubmissionDeliveryState.OutcomeUnknown));
		handler.Mode = "valid";
		Assert.ThrowsAsync<InvalidOperationException> (() => SubmissionDelivery.ExecuteAsync (journal, plan, package, form, transport));
		Assert.That (handler.Posts, Is.EqualTo (1)); Assert.That (transport.Sends, Is.Zero);
		await Task.CompletedTask;
		}

	private sealed class Transport (CrestronSubmissionUploader uploader) : ISubmissionDeliveryTransport
		{
		internal int Sends;
		public Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken token) => uploader.UploadAsync (package, filename, token);
		public Task<SubmissionMailReceipt> SendAsync (SubmissionDeliveryPlan plan, SubmissionUploadReceipt receipt, Stream form, string messageId, CancellationToken token)
			{ Sends++; return Task.FromResult (new SubmissionMailReceipt ("simulated-only")); }
		}
	private sealed class Handler : HttpMessageHandler
		{
		internal string Mode = "valid";
		internal int Posts;
		internal bool Cancelled;
		internal readonly List<string> Paths = [], FormFields = [];
		internal byte[]? FileBytes;
		protected override async Task<HttpResponseMessage> SendAsync (HttpRequestMessage request, CancellationToken token)
			{
			Paths.Add (request.RequestUri!.AbsoluteUri);
			Assert.That (request.Headers.Authorization?.Scheme, Is.EqualTo ("Basic"));
			if (Mode == "hang") { try { await Task.Delay (Timeout.Infinite, token); } catch (OperationCanceledException) { Cancelled = true; throw; } }
			string route = request.RequestUri.PathAndQuery;
			if (route == "/index.php") return Response (Mode == "changed-form" ? "changed" : "form", Mode == "form-redirect" ? 302 : 200);
			if (route == "/index.php?page=tos") return Response (Mode == "changed-terms" ? "changed" : "terms");
			if (route == "/upload.php")
				{
				Posts++; Assert.That (request.Method, Is.EqualTo (HttpMethod.Post));
				foreach (var part in (MultipartFormDataContent)request.Content!)
					{
					string name = part.Headers.ContentDisposition!.Name!.Trim ('"');
					if (name == "upfile") { FileBytes = await part.ReadAsByteArrayAsync (token); Assert.That (part.Headers.ContentDisposition.FileName!.Trim ('"'), Is.EqualTo (Filename)); }
					else FormFields.Add (name + "=" + await part.ReadAsStringAsync (token));
					}
				if (Mode == "lost-response") throw new HttpRequestException ("synthetic-private-provider-error");
				string url = (Mode == "external-link" ? "https://outside.example.test/" : Origin) + "download.php?file=synthetic";
				if (Mode == "duplicate-existing") return Response ("That file has already been uploaded. Filename: " + Filename + " Download Link: <a href=\"" + url + "\">download</a>");
				string text = "Your file, " + (Mode == "wrong-filename" ? "different.pkg" : Filename) + " was uploaded!";
				string page = text + "<a href=\"" + url + "\">download</a><a href=\"" + Origin + "download.php?file=" + (Mode == "different-delete-id" ? "other" : "synthetic") + "&amp;del=synthetic\">delete</a>";
				if (Mode == "duplicate-link") page += "<a href=\"" + url + "\">duplicate</a>";
				return Response (page, Mode == "post-error" ? 500 : Mode == "post-redirect" ? 302 : 200);
				}
			if (route == "/download.php?file=synthetic")
				{
				string url = (Mode == "external-button" ? "https://outside.example.test/" : Origin) + "download2.php?a=synthetic&amp;b=synthetic" + (Mode == "duplicate-query" ? "&amp;a=again" : "");
				return Response (Mode == "unknown-button" ? "No recognized button" : "<button onclick=\"window.location.href = '" + url + "';\">Download</button>");
				}
			Assert.That (route, Is.EqualTo ("/download2.php?a=synthetic&b=synthetic"));
			return new HttpResponseMessage (HttpStatusCode.OK) { Content = new ByteArrayContent (Mode == "wrong-bytes" ? "wrong"u8.ToArray () : Mode == "extra-bytes" ? Package.Concat (new byte[] { 1 }).ToArray () : Package) };
			}
		private static HttpResponseMessage Response (string text, int status = 200) => new ((HttpStatusCode)status) { Content = new StringContent (text), Headers = { Location = new Uri ("https://outside.example.test/redirect") } };
		}
	}