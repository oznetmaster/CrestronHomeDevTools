// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text.Json;

namespace CrestronHomeDevTools;

/// <summary>Private, trusted intake configuration. File digests identify frozen inputs, not operation permission.</summary>
public sealed record SubmissionReleaseIntakeSettings(int SchemaVersion, string Repository, long ReleaseId,
 string PackageName, string PrivateRoot, string ProfileSnapshotPath, string ProfileSnapshotSha256,
 string ToolingManifestPath, string ToolingSha256, bool AllowPrerelease = false);
public sealed record SubmissionReleaseIntakeResult(SubmissionReleaseAvailability Availability,
 string ReasonCode, string? RunDirectory, SubmissionWorkflowCheckpoint? Checkpoint);

/// <summary>Intake only: verify frozen local inputs and a released package before opening a persistent workflow.
/// It neither runs repository code nor deploys, signs, uploads or sends anything.</summary>
public static class SubmissionReleaseIntake
{
 public static async Task<SubmissionReleaseIntakeResult> PrepareAsync(SubmissionReleaseIntakeSettings settings,
  GitHubSubmissionRelease github, CancellationToken cancellationToken = default)
 {
  ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(github);
  if (settings.SchemaVersion != 1 || !Path.IsPathFullyQualified(settings.PrivateRoot) || !Directory.Exists(settings.PrivateRoot))
   throw new ArgumentException("Use schema version 1 and an existing protected workflow root.");
  VerifyInput(settings.ProfileSnapshotPath, settings.ProfileSnapshotSha256);
  VerifyInput(settings.ToolingManifestPath, settings.ToolingSha256);
  var inspection = await github.InspectAsync(settings.Repository, settings.ReleaseId, settings.PackageName,
   settings.AllowPrerelease, cancellationToken).ConfigureAwait(false);
  if (inspection.Availability != SubmissionReleaseAvailability.Ready)
   return new(inspection.Availability, inspection.ReasonCode, null, null);
  var release = new SubmissionWorkflowRelease(inspection.Repository, inspection.ReleaseId, inspection.Tag,
   inspection.SourceCommit!, inspection.PackageSha256!, settings.ProfileSnapshotSha256, settings.ToolingSha256);
  string directory = Path.Combine(settings.PrivateRoot, SubmissionWorkflow.RunKey(release));
  Directory.CreateDirectory(directory);
  if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
   throw new InvalidDataException("Workflow directories cannot be links.");
  using var gate = new FileStream(Path.Combine(directory, "intake.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
  // An existing run must match before any candidate file is changed or downloaded.
  if (File.Exists(Path.Combine(directory, "state.json"))) SubmissionWorkflow.Read(settings.PrivateRoot, release);
  // The exclusive intake lock proves no intake transfer is still using these exact scratch names.
  // Retain the candidate, receipts, evidence and all unrelated files.
  foreach (string scratch in Directory.EnumerateFiles(directory, "candidate.pkg.*.download", SearchOption.TopDirectoryOnly))
  {
   string name = Path.GetFileName(scratch);
   if (name.Length != "candidate.pkg..download".Length + 32 ||
    !Guid.TryParseExact(name["candidate.pkg.".Length..^".download".Length], "N", out _)) continue;
   if ((File.GetAttributes(scratch) & FileAttributes.ReparsePoint) != 0)
    throw new InvalidDataException("Download scratch files cannot be redirected.");
   File.Delete(scratch);
  }
  await github.DownloadVerifiedAsync(inspection, Path.Combine(directory, "candidate.pkg"),
   settings.AllowPrerelease, cancellationToken).ConfigureAwait(false);
  VerifyInput(settings.ProfileSnapshotPath, settings.ProfileSnapshotSha256);
  VerifyInput(settings.ToolingManifestPath, settings.ToolingSha256);
  string receipt = Path.Combine(directory, "release.json");
  byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(inspection, new JsonSerializerOptions { WriteIndented = true });
  if (File.Exists(receipt))
  {
   if (!File.ReadAllBytes(receipt).AsSpan().SequenceEqual(bytes))
    throw new InvalidDataException("Retained release metadata changed. Review this attempt instead of replacing it.");
  }
  else
  {
   string pending = receipt + ".tmp";
   // A previous process may have stopped before its atomic rename. It is an intake scratch file only.
   using (var f = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None)) { f.Write(bytes); f.Flush(true); }
   File.Move(pending, receipt);
  }
  var state = SubmissionWorkflow.Open(settings.PrivateRoot, release);
  return new(SubmissionReleaseAvailability.Ready, "candidate-retained", directory, state);
 }

 private static void VerifyInput(string path, string digest)
 {
  if (!Path.IsPathFullyQualified(path) || digest == null || digest.Length != 64 ||
   digest.Any(c => !(char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')))
   throw new ArgumentException("Supply absolute frozen-input paths with their independently recorded SHA-256 digests.");
  using var stream = File.OpenRead(path);
  if (stream.Length > 16 * 1024 * 1024 || Convert.ToHexStringLower(SHA256.HashData(stream)) != digest)
   throw new InvalidDataException("A frozen setup snapshot or tooling manifest changed.");
 }
}
