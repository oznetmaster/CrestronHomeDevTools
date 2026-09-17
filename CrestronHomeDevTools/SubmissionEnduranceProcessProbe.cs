// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

public sealed record SubmissionEnduranceProbeProgram (string Directory, string Executable,
	IReadOnlyList<SubmissionEvidenceFile> Files, string? SettingsFile = null);
public sealed record SubmissionEnduranceProbeRequest (int SchemaVersion, SubmissionEndurancePlan Plan, string? SettingsFile);
public sealed record SubmissionEnduranceWorkerPlan (SubmissionEndurancePlan Plan, SubmissionEnduranceProcessor Processor,
	SubmissionEnduranceProbeProgram Probe);

/// <summary>
/// Runs an explicitly trusted, read-only producer as a child process. Pins cover the complete published
/// directory. This is an execution contract, not a sandbox or proof that a producer tests driver function.
/// </summary>
public static class SubmissionEnduranceProcessProbe
	{
	private static readonly JsonSerializerOptions JsonOptions = new ()
		{ PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter () } };
	public static string GetProducerId (SubmissionEnduranceProbeProgram program)
		{
		ArgumentNullException.ThrowIfNull (program);
		if (program.Files == null || program.Files.Count is 0 or > 4096 ||
			string.IsNullOrWhiteSpace (program.Executable) || program.Executable.Contains ('\\') ||
			program.Files.Any (file => file == null || string.IsNullOrWhiteSpace (file.RelativePath) || file.RelativePath.Contains ('\\') ||
				file.Sha256?.Length != 64 || !file.Sha256.All (char.IsAsciiHexDigit)) ||
			program.Files.Select (file => file.RelativePath).Distinct (StringComparer.OrdinalIgnoreCase).Count () != program.Files.Count ||
			!program.Files.Any (file => file.RelativePath == program.Executable))
			throw new ArgumentException ("The producer requires an executable and a complete, unique SHA-256 file manifest using forward slashes.");
		var pins = program.Files.OrderBy (file => file.RelativePath, StringComparer.Ordinal)
			.Select (file => new SubmissionEvidenceFile (file.RelativePath, file.Sha256.ToUpperInvariant ())).ToArray ();
		return "process-sha256:" + Convert.ToHexString (SHA256.HashData (JsonSerializer.SerializeToUtf8Bytes (
			new { program.Executable, Files = pins, program.SettingsFile })));
		}

	/// <summary>Verify the published producer before deliberately acquiring a new monitor reservation.</summary>
	public static void Validate (SubmissionEnduranceProbeProgram program, SubmissionEndurancePlan plan)
		{
		using var bundle = OpenBundle (program, plan);
		}

	public static async Task<SubmissionEnduranceProbeResult> RunAsync (SubmissionEnduranceProbeProgram program,
		SubmissionEndurancePlan plan, CancellationToken token = default)
		{
		using var bundle = OpenBundle (program, plan);
		using var process = new Process { StartInfo = new ()
			{
			FileName = Path.GetFullPath (Path.Combine (program.Directory, program.Executable)),
			WorkingDirectory = Path.GetFullPath (program.Directory), UseShellExecute = false, CreateNoWindow = true,
			RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
			} };
		token.ThrowIfCancellationRequested ();
		if (!process.Start ()) throw new InvalidOperationException ("The endurance producer did not start.");
		using var stop = CancellationTokenSource.CreateLinkedTokenSource (token);
		async Task<byte[]> Read (Stream input, int limit, bool retain)
			{
			try
				{
				using var bytes = new MemoryStream ();
				var buffer = new byte[8192];
				int total = 0, count;
				while ((count = await input.ReadAsync (buffer, stop.Token).ConfigureAwait (false)) > 0)
					{
					total += count;
					if (total > limit) throw new InvalidDataException ("The endurance producer exceeded its output limit.");
					if (retain) bytes.Write (buffer, 0, count);
					}
				return bytes.ToArray ();
				}
			catch { stop.Cancel (); throw; }
			}
		var stdout = Read (process.StandardOutput.BaseStream, 8 * 1024 * 1024, true);
		var stderr = Read (process.StandardError.BaseStream, 64 * 1024, false);
		try
			{
			await JsonSerializer.SerializeAsync (process.StandardInput.BaseStream,
				new SubmissionEnduranceProbeRequest (1, plan, program.SettingsFile), cancellationToken: stop.Token).ConfigureAwait (false);
			process.StandardInput.Close ();
			await process.WaitForExitAsync (stop.Token).ConfigureAwait (false);
			await Task.WhenAll (stdout, stderr).ConfigureAwait (false);
			if (process.ExitCode != 0) throw new InvalidDataException ("The endurance producer reported failure.");
			return JsonSerializer.Deserialize<SubmissionEnduranceProbeResult> (await stdout.ConfigureAwait (false), JsonOptions)
				?? throw new InvalidDataException ("The endurance producer returned no observation.");
			}
		catch
			{
			stop.Cancel ();
			// Never release the collector lock while the child is still running, even on cancellation.
			if (!process.HasExited)
				{
				try { process.Kill (entireProcessTree: true); }
				catch (InvalidOperationException) when (process.HasExited) { }
				catch (System.ComponentModel.Win32Exception) { /* Keep ownership until external supervision stops the child. */ }
				await process.WaitForExitAsync (CancellationToken.None).ConfigureAwait (false);
				}
			try { await Task.WhenAll (stdout, stderr).ConfigureAwait (false); } catch { }
			token.ThrowIfCancellationRequested ();
			// Do not forward child output, settings or parsing exceptions into a public diagnostic.
			throw new InvalidDataException ("The endurance producer failed; no observation was accepted.");
			}
		}

	private static Bundle OpenBundle (SubmissionEnduranceProbeProgram program, SubmissionEndurancePlan plan)
		{
		_ = SubmissionEndurance.PlanDigest (plan);
		if (GetProducerId (program) != plan.ProducerId)
			throw new InvalidDataException ("The producer manifest differs from the pinned endurance plan.");
		string root = Path.GetFullPath (program.Directory);
		if (!Path.IsPathFullyQualified (program.Directory) ||
			(program.SettingsFile != null && !Path.IsPathFullyQualified (program.SettingsFile)))
			throw new ArgumentException ("Producer and private settings paths must be absolute.");
		var actual = new HashSet<string> (StringComparer.Ordinal);
		var pending = new Stack<string> ();
		int entries = 0;
		pending.Push (root);
		while (pending.TryPop (out var directory))
			{
			if ((File.GetAttributes (directory) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("Producer directories must not be links.");
			foreach (var path in System.IO.Directory.EnumerateFileSystemEntries (directory))
				{
				var attributes = File.GetAttributes (path);
				if ((attributes & FileAttributes.ReparsePoint) != 0)
					throw new InvalidDataException ("Producer files must not be links.");
				if ((attributes & FileAttributes.Directory) != 0) pending.Push (path);
				else actual.Add (Path.GetRelativePath (root, path).Replace ('\\', '/'));
				if (++entries > 4096)
					throw new InvalidDataException ("The producer bundle exceeds its file limit.");
				}
			}
		if (!actual.SetEquals (program.Files.Select (file => file.RelativePath)))
			throw new InvalidDataException ("The producer directory contains missing or unpinned files.");
		var bundle = new Bundle ();
		try
			{
			foreach (var pin in program.Files)
				{
				if (!SubmissionEvidence.SafeEvidencePath (root, pin.RelativePath, out var path))
					throw new InvalidDataException ("Invalid producer file path.");
				var file = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read);
				bundle.Files.Add (file);
				if (!Convert.ToHexString (SHA256.HashData (file)).Equals (pin.Sha256, StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException ("A pinned producer file changed.");
				}
			return bundle;
			}
		catch { bundle.Dispose (); throw; }
		}
	private sealed class Bundle : IDisposable
		{
		internal List<FileStream> Files { get; } = [];
		public void Dispose () { foreach (var file in Files) file.Dispose (); }
		}
	}