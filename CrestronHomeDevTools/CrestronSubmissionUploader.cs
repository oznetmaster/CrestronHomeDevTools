// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrestronHomeDevTools;

/// <summary>Upload-only provider. Invoke through a journaled transport after current delivery authorization.</summary>
public sealed class CrestronSubmissionUploader : IDisposable
	{
	private const string Origin = "https://uploader.crestron.com/";
	private const int PackageLimit = 64 * 1024 * 1024, HtmlLimit = 2 * 1024 * 1024;
	private readonly HttpClient _client;
	private readonly string _root, _formHash, _termsHash;
	private readonly TimeSpan _timeout;
	private readonly AuthenticationHeaderValue _authentication;

	/// <summary>Hashes identify the reviewed form and accepted terms. Credentials and receipts must remain private.</summary>
	public CrestronSubmissionUploader (NetworkCredential credential, string reviewedFormSha256, string acceptedTermsSha256,
		string privateReceiptDirectory, TimeSpan timeout)
		: this (credential, reviewedFormSha256, acceptedTermsSha256, privateReceiptDirectory, timeout,
			new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false }) { }

	internal CrestronSubmissionUploader (NetworkCredential credential, string reviewedFormSha256, string acceptedTermsSha256,
		string privateReceiptDirectory, TimeSpan timeout, HttpMessageHandler handler)
		{
		ArgumentNullException.ThrowIfNull (credential);
		if (string.IsNullOrWhiteSpace (credential.UserName) || string.IsNullOrEmpty (credential.Password) ||
			credential.UserName.Any (c => c == ':' || char.IsControl (c))) throw new ArgumentException ("Provide the authorized uploader login.");
		if (!ValidHash (reviewedFormSha256) || !ValidHash (acceptedTermsSha256)) throw new ArgumentException ("Pin the reviewed form and accepted terms.");
		if (!Path.IsPathFullyQualified (privateReceiptDirectory) || !Directory.Exists (privateReceiptDirectory) ||
			(File.GetAttributes (privateReceiptDirectory) & FileAttributes.ReparsePoint) != 0)
			throw new ArgumentException ("Provide existing protected local receipt storage.");
		if (timeout < TimeSpan.FromSeconds (1) || timeout > TimeSpan.FromMinutes (10)) throw new ArgumentOutOfRangeException (nameof (timeout));
		_root = Path.GetFullPath (privateReceiptDirectory); _formHash = reviewedFormSha256; _termsHash = acceptedTermsSha256; _timeout = timeout;
		_authentication = new ("Basic", Convert.ToBase64String (Encoding.UTF8.GetBytes (credential.UserName + ":" + credential.Password)));
		_client = new HttpClient (handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
		}

	/// <summary>Never retries an upload. A failure after the POST intent requires provider reconciliation through the delivery journal.</summary>
	public async Task<SubmissionUploadReceipt> UploadAsync (Stream package, string filename, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (package);
		if (filename == null || !Regex.IsMatch (filename, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,245}\.pkg\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds (1)))
			throw new ArgumentException ("Use a plain package filename containing letters, digits, dots, underscores or hyphens.");
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
		deadline.CancelAfter (_timeout);
		byte[] bytes = await ReadBounded (package, PackageLimit, deadline.Token).ConfigureAwait (false);
		if (bytes.Length == 0) throw new InvalidDataException ("The package is empty.");
		string packageHash = Hash (bytes), attemptId = "upload-" + Guid.NewGuid ().ToString ("N"), attempt = Path.Combine (_root, attemptId);
		Directory.CreateDirectory (attempt);
		Write (attempt, "intent.json", new { Filename = filename, PackageSha256 = packageHash, StartedUtc = DateTimeOffset.UtcNow });
		bool postAttempted = false;
		try
			{
			byte[] form = await Fetch (new Uri (Origin + "index.php"), null, attempt, "form", HtmlLimit, deadline.Token).ConfigureAwait (false);
			byte[] terms = await Fetch (new Uri (Origin + "index.php?page=tos"), null, attempt, "terms", HtmlLimit, deadline.Token).ConfigureAwait (false);
			if (Hash (form) != _formHash || Hash (terms) != _termsHash) throw new InvalidDataException ("The reviewed form or accepted terms changed.");
			using var multipart = new MultipartFormDataContent ();
			multipart.Add (new StringContent ("Paste file url here"), "from");
			multipart.Add (new StringContent ("1"), "operation");
			multipart.Add (new StringContent ("2"), "operation");
			multipart.Add (new StringContent ("on"), "agreecheck");
			var file = new ByteArrayContent (bytes); file.Headers.ContentType = new ("application/octet-stream");
			multipart.Add (file, "upfile", filename);
			Write (attempt, "post-intent.json", new { PackageSha256 = packageHash, TermsSha256 = _termsHash, StartedUtc = DateTimeOffset.UtcNow });
			postAttempted = true;
			byte[] response = await Fetch (new Uri (Origin + "upload.php"), multipart, attempt, "upload", HtmlLimit, deadline.Token).ConfigureAwait (false);
			string uploaded = Encoding.UTF8.GetString (response);
			string text = WebUtility.HtmlDecode (Regex.Replace (uploaded, "<[^>]*>", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds (1)));
			if (!text.Contains ("Your file, " + filename + " was uploaded!", StringComparison.Ordinal))
				throw new InvalidDataException ("No confirmed upload message for this package.");
			var links = Matches (uploaded, "<a\\b[^>]*\\bhref\\s*=\\s*(?:\"(?<url>[^\"]+)\"|'(?<url>[^']+)')");
			var downloads = links.Where (link => link.Contains ("download.php", StringComparison.OrdinalIgnoreCase)).ToArray ();
			if (downloads.Length != 2) throw new InvalidDataException ("Unexpected upload receipt links.");
			Uri? landing = null, deletion = null;
			foreach (string link in downloads)
				{
				Uri url = ApprovedUri (link, "/download.php");
				var query = Query (url);
				if (query.Keys.Order ().SequenceEqual (new[] { "file" })) { if (landing != null) throw new InvalidDataException (); landing = url; }
				else if (query.Keys.Order ().SequenceEqual (new[] { "del", "file" })) { if (deletion != null) throw new InvalidDataException (); deletion = url; }
				else throw new InvalidDataException ("Unknown upload receipt query.");
				}
			if (landing == null || deletion == null || Query (landing)["file"] != Query (deletion)["file"])
				throw new InvalidDataException ("Receipt links do not identify one uploaded file.");
			Write (attempt, "provider-links.json", new { DownloadUrl = landing.AbsoluteUri, DeleteUrl = deletion.AbsoluteUri });
			byte[] page = await Fetch (landing, null, attempt, "download-page", HtmlLimit, deadline.Token).ConfigureAwait (false);
			var buttons = Matches (Encoding.UTF8.GetString (page), "<button\\b[^>]*\\bonclick\\s*=\\s*\"\\s*window\\.location\\.href\\s*=\\s*'(?<url>[^']+)'\\s*;?\\s*\"");
			if (buttons.Length != 1) throw new InvalidDataException ("Expected one observed download button.");
			Uri archiveUrl = ApprovedUri (buttons[0], "/download2.php");
			if (!Query (archiveUrl).Keys.Order ().SequenceEqual (new[] { "a", "b" })) throw new InvalidDataException ("Unknown download query.");
			byte[] downloaded = await Fetch (archiveUrl, null, attempt, "archive", bytes.Length, deadline.Token).ConfigureAwait (false);
			if (downloaded.Length != bytes.Length || Hash (downloaded) != packageHash) throw new InvalidDataException ("Downloaded package differs from the approved bytes.");
			Write (attempt, "verified.json", new { PackageSha256 = packageHash, Bytes = bytes.Length, DownloadUrl = landing.AbsoluteUri, VerifiedUtc = DateTimeOffset.UtcNow });
			return new (landing.AbsoluteUri, JsonSerializer.Serialize (new { Provider = "CrestronUploader", Attempt = attemptId, PackageSha256 = packageHash, DownloadVerified = true }));
			}
		catch
			{
			Write (attempt, "failed.json", new { PostAttempted = postAttempted, Outcome = postAttempted ? "RequiresReconciliation" : "NoUploadAttempted", FailedUtc = DateTimeOffset.UtcNow });
			cancellationToken.ThrowIfCancellationRequested ();
			throw new InvalidDataException ("Uploader validation failed; inspect its private receipt before any further delivery. No request was automatically replayed.");
			}
		}

	private async Task<byte[]> Fetch (Uri url, HttpContent? content, string attempt, string label, int limit, CancellationToken token)
		{
		if (url.Scheme != "https" || url.Host != "uploader.crestron.com" || !url.IsDefaultPort || url.UserInfo != "" || url.Fragment != "") throw new InvalidDataException ("Unexpected uploader origin.");
		using var request = new HttpRequestMessage (content == null ? HttpMethod.Get : HttpMethod.Post, url) { Content = content };
		request.Headers.Authorization = _authentication;
		using var response = await _client.SendAsync (request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait (false);
		Write (attempt, label + "-response.json", new { StatusCode = (int)response.StatusCode, ContentType = response.Content.Headers.ContentType?.ToString (), ReceivedUtc = DateTimeOffset.UtcNow });
		using var stream = await response.Content.ReadAsStreamAsync (token).ConfigureAwait (false);
		byte[] body = await ReadBounded (stream, limit, token).ConfigureAwait (false);
		using (var file = new FileStream (Path.Combine (attempt, label + ".bin"), FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { file.Write (body); file.Flush (true); }
		if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException ("Unexpected uploader response status.");
		return body;
		}
	private static string[] Matches (string html, string expression) => Regex.Matches (html, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds (1))
		.Select (match => WebUtility.HtmlDecode (match.Groups["url"].Value)).ToArray ();
	private static Uri ApprovedUri (string value, string path)
		{
		if (!Uri.TryCreate (value, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "uploader.crestron.com" ||
			!url.IsDefaultPort || url.UserInfo != "" || url.Fragment != "" || url.AbsolutePath != path)
			throw new InvalidDataException ("Unexpected returned uploader URL.");
		return url;
		}
	private static Dictionary<string, string> Query (Uri url)
		{
		var result = new Dictionary<string, string> (StringComparer.Ordinal);
		foreach (string item in url.Query.TrimStart ('?').Split ('&'))
			{
			string[] pair = item.Split ('=', 2);
			if (pair.Length != 2) throw new InvalidDataException ("Invalid uploader query.");
			string key = WebUtility.UrlDecode (pair[0]), value = WebUtility.UrlDecode (pair[1]);
			if (string.IsNullOrWhiteSpace (value) || value.Length > 1024 || value.Any (char.IsControl) || !result.TryAdd (key, value))
				throw new InvalidDataException ("Invalid or duplicate uploader query.");
			}
		return result;
		}
	private static async Task<byte[]> ReadBounded (Stream stream, int limit, CancellationToken token)
		{
		using var buffer = new MemoryStream (); var block = new byte[8192]; int count;
		while ((count = await stream.ReadAsync (block, token).ConfigureAwait (false)) != 0)
			{ if (buffer.Length + count > limit) throw new InvalidDataException ("Uploader data exceeds its limit."); buffer.Write (block, 0, count); }
		return buffer.ToArray ();
		}
	private static void Write (string directory, string name, object value)
		{ using var file = new FileStream (Path.Combine (directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read); JsonSerializer.Serialize (file, value); file.Flush (true); }
	private static bool ValidHash (string hash) => hash?.Length == 64 && hash.All (c => char.IsAsciiDigit (c) || c is >= 'a' and <= 'f');
	private static string Hash (byte[] bytes) => Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant ();
	public void Dispose () => _client.Dispose ();
	}