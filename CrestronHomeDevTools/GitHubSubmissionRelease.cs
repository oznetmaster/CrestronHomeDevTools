// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace CrestronHomeDevTools;

public enum SubmissionReleaseAvailability { Ready, AwaitingPackage, NotEligible }
public sealed record SubmissionReleaseInspection(SubmissionReleaseAvailability Availability, string Repository, long ReleaseId,
 string Tag, string? SourceCommit, long? AssetId, string? PackageName, string? PackageSha256, long? PackageBytes, string ReasonCode);

/// <summary>Read published release identity from GitHub. It never treats target_commitish (which may be a branch)
/// as a frozen commit, downloads code, schedules tests or grants submission authority.</summary>
public sealed class GitHubSubmissionRelease(HttpClient client)
{
 /// <summary>The private profile supplies the exact repository and package name. Authentication, if needed,
 /// belongs on the caller's HTTP client; token formats and lengths are not inferred or logged.</summary>
 public async Task<SubmissionReleaseInspection> InspectAsync(string repository, long releaseId, string packageName,
  bool allowPrerelease = false, CancellationToken cancellationToken = default)
 {
  ArgumentNullException.ThrowIfNull(repository);
  var parts = repository.Split('/');
  if (parts.Length != 2 || parts.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".." || p.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))) ||
   releaseId <= 0 || string.IsNullOrWhiteSpace(packageName) || !packageName.EndsWith(".pkg", StringComparison.Ordinal) ||
   packageName.IndexOfAny(['/', '\\', ':']) >= 0 || packageName.Any(char.IsControl))
   throw new ArgumentException("Supply the configured repository, release ID and exact package asset name.");
  using var release = await GetAsync($"repos/{repository}/releases/{releaseId}", cancellationToken).ConfigureAwait(false);
  var r = release.RootElement;
  if (r.GetProperty("id").GetInt64() != releaseId) throw new InvalidDataException("GitHub returned a different release.");
  string tag = r.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Missing release tag.");
  if (string.IsNullOrWhiteSpace(tag) || tag.Length > 256 || tag.Any(char.IsControl)) throw new InvalidDataException("Invalid release tag.");
  SubmissionReleaseInspection Result(SubmissionReleaseAvailability availability, string reason) => new(availability,repository,releaseId,tag,null,null,null,null,null,reason);
  if (r.GetProperty("draft").GetBoolean() || r.GetProperty("published_at").ValueKind == JsonValueKind.Null)
   return Result(SubmissionReleaseAvailability.NotEligible,"not-published");
  if (r.GetProperty("prerelease").GetBoolean() && !allowPrerelease)
   return Result(SubmissionReleaseAvailability.NotEligible,"prerelease-not-selected");
  var matches = r.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == packageName).ToArray();
  if (matches.Length == 0) return Result(SubmissionReleaseAvailability.AwaitingPackage,"package-not-uploaded");
  if (matches.Length != 1) throw new InvalidDataException("More than one asset matches the configured package.");
  var asset = matches[0];
  if (asset.GetProperty("state").GetString() != "uploaded") return Result(SubmissionReleaseAvailability.AwaitingPackage,"package-upload-in-progress");
  long size = asset.GetProperty("size").GetInt64(), assetId = asset.GetProperty("id").GetInt64();
  if (size is <= 0 or > 64 * 1024 * 1024 || assetId <= 0) throw new InvalidDataException("Invalid package size or asset identity.");
  string? digest = asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
  if (digest == null) return Result(SubmissionReleaseAvailability.AwaitingPackage,"package-digest-unavailable");
  if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || digest[7..].Any(c => !(char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')))
   throw new InvalidDataException("Unsupported release asset digest.");
  using var commit = await GetAsync($"repos/{repository}/commits/{Uri.EscapeDataString(tag)}", cancellationToken).ConfigureAwait(false);
  string? sha = commit.RootElement.GetProperty("sha").GetString();
  if (sha == null || sha.Length != 40 || sha.Any(c => !(char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')))
   throw new InvalidDataException("Release tag did not resolve to a commit.");
  return new(SubmissionReleaseAvailability.Ready,repository,releaseId,tag,sha,assetId,packageName,digest[7..],size,"ready-to-freeze");
 }

 /// <summary>Retain the exact inspected package. A repeated call verifies an existing copy; it never
 /// replaces it. Recheck release/tag identity after transfer so a concurrently edited release is rejected.
 /// The HTTP client must support the GitHub asset endpoint's HTTPS redirects without forwarding credentials
 /// to other hosts (the standard HttpClientHandler strips Authorization on redirects).</summary>
 public async Task DownloadVerifiedAsync(SubmissionReleaseInspection inspected, string destination,
  bool allowPrerelease = false, CancellationToken cancellationToken = default)
 {
  ArgumentNullException.ThrowIfNull(inspected);
  if (inspected.Availability != SubmissionReleaseAvailability.Ready || inspected.AssetId is not > 0 ||
   inspected.PackageName == null || inspected.PackageSha256 == null || inspected.PackageBytes is not > 0 ||
   !Path.IsPathFullyQualified(destination) || !Directory.Exists(Path.GetDirectoryName(destination)))
   throw new ArgumentException("Supply a ready release inspection and an absolute destination with an existing protected parent.");
  // Validate caller-supplied metadata against GitHub before using its fields in a request.
  await CheckIdentity().ConfigureAwait(false);
  if (File.Exists(destination))
  {
   await VerifyFile(destination).ConfigureAwait(false);
   return;
  }
  string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
  try
  {
   using var request = new HttpRequestMessage(HttpMethod.Get,
    $"https://api.github.com/repos/{inspected.Repository}/releases/assets/{inspected.AssetId}");
   request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
   request.Headers.UserAgent.ParseAdd("CrestronHomeDevTools");
   request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
   using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
   response.EnsureSuccessStatusCode();
   if (response.RequestMessage?.RequestUri?.Scheme is string scheme && scheme != Uri.UriSchemeHttps)
    throw new InvalidDataException("Package transfer must remain HTTPS.");
   await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
   await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
   {
    byte[] buffer = new byte[81920]; long total = 0; int count;
    while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
    {
     total += count;
     if (total > inspected.PackageBytes) throw new InvalidDataException("Package exceeds its inspected length.");
     await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
    }
    target.Flush(true);
   }
   await VerifyFile(temporary).ConfigureAwait(false);
   await CheckIdentity().ConfigureAwait(false);
   File.Move(temporary, destination); // Never replace an earlier retained candidate.
  }
  finally { if (File.Exists(temporary)) File.Delete(temporary); }

  async Task CheckIdentity()
  {
   var current = await InspectAsync(inspected.Repository, inspected.ReleaseId, inspected.PackageName,
    allowPrerelease, cancellationToken).ConfigureAwait(false);
   if (current != inspected) throw new InvalidDataException("The release or package changed since inspection. Retain the existing run for review.");
  }
  async Task VerifyFile(string path)
  {
   if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != inspected.PackageBytes)
    throw new InvalidDataException("Retained package path or length does not match inspection.");
   await using var stream = File.OpenRead(path);
   if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)) != inspected.PackageSha256)
    throw new InvalidDataException("Package digest does not match the published asset.");
  }
 }

 private async Task<JsonDocument> GetAsync(string path, CancellationToken token)
 {
  using var request = new HttpRequestMessage(HttpMethod.Get,"https://api.github.com/"+path);
  request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
  request.Headers.UserAgent.ParseAdd("CrestronHomeDevTools");
  request.Headers.Add("X-GitHub-Api-Version","2026-03-10");
  using var response = await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
  response.EnsureSuccessStatusCode();
  await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
  using var data = new MemoryStream();
  byte[] buffer = new byte[8192];
  int count;
  while ((count = await stream.ReadAsync(buffer,token).ConfigureAwait(false)) != 0)
  {
   if (data.Length+count > 4*1024*1024) throw new InvalidDataException("Release metadata exceeds its size limit.");
   data.Write(buffer,0,count);
  }
  return JsonDocument.Parse(data.ToArray());
 }
}
