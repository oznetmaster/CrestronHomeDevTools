// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

/// <summary>Reviewed paths and hashes supplied by protected orchestration, never by an evidence-producing worker.</summary>
public sealed record SubmissionDeliveryRevalidationSettings (
	string PythonPath, string PythonSha256, string DotnetPath, string DotnetSha256,
	string ToolsDirectory, IReadOnlyList<SubmissionEvidenceFile> ToolFiles,
	string ValidatorDirectory, IReadOnlyList<SubmissionEvidenceFile> ValidatorFiles,
	string PreparationSettingsPath, string PreparationSettingsSha256, string PreparedDirectory,
	string AttemptsDirectory, string DeliveryReviewSha256, TimeSpan Timeout);

/// <summary>Runs the pinned offline revalidator; it never calls a delivery transport.</summary>
public static class SubmissionDeliveryRevalidation
	{
	private const int Limit = 1024 * 1024;
	private static readonly JsonSerializerOptions Json = new ()
		{
		PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		RespectRequiredConstructorParameters = true
		};
	private sealed record Result (int SchemaVersion, string State, string DeliveryReviewSha256, string SignedReviewSha256,
		string AuthorizationSha256, string PlanFileSha256, SubmissionDeliveryPlan Plan, DateTimeOffset ExpiresUtc,
		DateTimeOffset RevalidatedUtc, bool DeliveryAttempted, bool SubmissionReady, string ValidationReportSha256);

	public static async Task<SubmissionDeliveryAuthorization> CheckAsync (SubmissionDeliveryRevalidationSettings settings,
		SubmissionDeliveryPlan plan, SubmissionDeliveryStep step, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (settings);
		cancellationToken.ThrowIfCancellationRequested ();
		string digest = SubmissionDelivery.PlanDigest (plan);
		if (!Enum.IsDefined (step) || settings.Timeout < TimeSpan.FromSeconds (1) || settings.Timeout > TimeSpan.FromMinutes (10))
			throw new ArgumentException ("Select a known step and a bounded revalidation timeout.");
		foreach (string path in new[] { settings.PythonPath, settings.DotnetPath, settings.ToolsDirectory, settings.ValidatorDirectory,
			settings.PreparationSettingsPath, settings.PreparedDirectory, settings.AttemptsDirectory })
			if (!Path.IsPathFullyQualified (path)) throw new ArgumentException ("Revalidation requires absolute reviewed paths.");
		if (!ValidHash (settings.DeliveryReviewSha256)) throw new ArgumentException ("Pin the approved delivery review.");
		string attempts = Path.GetFullPath (settings.AttemptsDirectory);
		foreach (string path in new[] { settings.ToolsDirectory, settings.ValidatorDirectory, settings.PreparedDirectory, settings.PreparationSettingsPath, settings.PythonPath, settings.DotnetPath })
			if (Within (attempts, path) || Within (path, attempts)) throw new ArgumentException ("Keep attempts separate from immutable inputs.");
		if ((File.GetAttributes (attempts) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException ("Linked attempt storage is unsupported.");
		using var gate = new FileStream (Path.Combine (attempts, "revalidation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		foreach (string previous in Directory.EnumerateDirectories (attempts))
			{
			if (!File.Exists (Path.Combine (previous, "finished.json")))
				throw new InvalidOperationException ("An unfinished revalidation requires inspection before another process is started.");
			using var finished = Parse (await ReadFile (Path.Combine (previous, "finished.json"), Limit, cancellationToken).ConfigureAwait (false));
			if (finished.RootElement.GetProperty ("Success").ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
				finished.RootElement.GetProperty ("CompletedUtc").GetDateTimeOffset () == default)
				throw new InvalidDataException ("An incomplete terminal record requires inspection.");
			}
		using var pins = new Pins ();
		pins.File (settings.PythonPath, settings.PythonSha256);
		pins.File (settings.DotnetPath, settings.DotnetSha256);
		pins.Directory (settings.ToolsDirectory, settings.ToolFiles);
		pins.Directory (settings.ValidatorDirectory, settings.ValidatorFiles);
		var preparation = pins.File (settings.PreparationSettingsPath, settings.PreparationSettingsSha256);
		using var preparationJson = Parse (await Read (preparation, Limit, cancellationToken).ConfigureAwait (false));
		string validator = preparationJson.RootElement.GetProperty ("validator").GetString ()!;
		if (Path.GetFullPath (preparationJson.RootElement.GetProperty ("dotnet").GetString ()!) != Path.GetFullPath (settings.DotnetPath) ||
			!Within (validator, settings.ValidatorDirectory) ||
			!settings.ValidatorFiles.Any (file => Path.GetFullPath (Path.Combine (settings.ValidatorDirectory, file.RelativePath)) == Path.GetFullPath (validator)) ||
			!settings.ToolFiles.Any (file => file.RelativePath == "revalidate_delivery.py"))
			throw new InvalidDataException ("Preparation settings do not select the pinned validator and revalidator.");
		string attempt = Path.Combine (attempts, step + "-" + Guid.NewGuid ().ToString ("N"));
		cancellationToken.ThrowIfCancellationRequested ();
		Directory.CreateDirectory (attempt);
		DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
		Write (Path.Combine (attempt, "intent.json"), new { PlanSha256 = digest, Step = step.ToString (), StartedUtc = startedUtc });
		try
			{
			string request = Path.Combine (attempt, "settings.json"), output = Path.Combine (attempt, "result");
			Write (request, new { schemaVersion = 1, preparedDirectory = settings.PreparedDirectory,
				preparationSettings = settings.PreparationSettingsPath, output });
			using var process = new Process { StartInfo = new ()
				{
				FileName = settings.PythonPath, WorkingDirectory = settings.ToolsDirectory, UseShellExecute = false,
				CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
				} };
			foreach (string argument in new[] { "-B", "-E", "-s", Path.Combine (settings.ToolsDirectory, "revalidate_delivery.py"),
				"--settings", request, "--delivery-review-sha256", settings.DeliveryReviewSha256,
				"--signed-review-sha256", plan.ReviewSha256, "--authorization-sha256", plan.AuthorizationSha256 })
				process.StartInfo.ArgumentList.Add (argument);
			foreach (string key in process.StartInfo.Environment.Keys.Where (key => key.StartsWith ("PYTHON", StringComparison.OrdinalIgnoreCase) ||
				key.StartsWith ("CRESTRON_HOME_", StringComparison.OrdinalIgnoreCase) || key.StartsWith ("DOTNET_", StringComparison.OrdinalIgnoreCase)).ToArray ())
				process.StartInfo.Environment.Remove (key);
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
			deadline.CancelAfter (settings.Timeout);
			if (!process.Start ()) throw new IOException ("Unable to start revalidation.");
			Task<byte[]> stdout = Task.FromResult (Array.Empty<byte> ());
			Task<byte[]> stderr = Task.FromResult (Array.Empty<byte> ());
			byte[] returned;
			try
				{
				Write (Path.Combine (attempt, "process.json"), new { process.Id, StartedUtc = process.StartTime.ToUniversalTime () });
				process.StandardInput.Close ();
				stdout = Capture (process.StandardOutput.BaseStream, Limit, true, deadline);
				stderr = Capture (process.StandardError.BaseStream, 64 * 1024, false, deadline);
				await process.WaitForExitAsync (deadline.Token).ConfigureAwait (false);
				await Task.WhenAll (stdout, stderr).ConfigureAwait (false);
				if (process.ExitCode != 0) throw new InvalidDataException ("The offline revalidator refused the delivery.");
				returned = await stdout.ConfigureAwait (false);
				}
			catch
				{
				deadline.Cancel ();
				if (!process.HasExited)
					{
					try { process.Kill (entireProcessTree: true); }
					catch (InvalidOperationException) when (process.HasExited) { }
					catch (System.ComponentModel.Win32Exception) { /* Keep ownership until the process actually exits. */ }
					await process.WaitForExitAsync (CancellationToken.None).ConfigureAwait (false);
					}
				try { await Task.WhenAll (stdout, stderr).ConfigureAwait (false); } catch { }
				throw;
				}
			using var returnedJson = Parse (returned);
			byte[] receiptBytes = await ReadFile (Path.Combine (output, "revalidation-receipt.json"), Limit, cancellationToken).ConfigureAwait (false);
			using var receiptJson = Parse (receiptBytes);
			string marker = System.Text.Encoding.ASCII.GetString (await ReadFile (Path.Combine (output, "COMPLETE"), 80, cancellationToken).ConfigureAwait (false)).Trim ();
			var result = receiptJson.RootElement.Deserialize<Result> (Json) ?? throw new InvalidDataException ("Missing revalidation result.");
			if (marker != Hash (receiptBytes) || !JsonElement.DeepEquals (returnedJson.RootElement, receiptJson.RootElement) ||
				result.SchemaVersion != 1 || result.State != "DeliveryRevalidated" || result.DeliveryAttempted || result.SubmissionReady ||
				result.DeliveryReviewSha256 != settings.DeliveryReviewSha256 || result.SignedReviewSha256 != plan.ReviewSha256 ||
				result.AuthorizationSha256 != plan.AuthorizationSha256 || result.RevalidatedUtc < startedUtc || result.RevalidatedUtc > DateTimeOffset.UtcNow ||
				SubmissionDelivery.PlanDigest (result.Plan) != digest || result.ExpiresUtc <= DateTimeOffset.UtcNow ||
				!ValidHash (result.PlanFileSha256) || !ValidHash (result.ValidationReportSha256))
				throw new InvalidDataException ("Revalidation did not confirm this exact approved delivery.");
			foreach (var file in new[] { new SubmissionEvidenceFile ("delivery-plan.json", result.PlanFileSha256),
				new SubmissionEvidenceFile ("validation-report.json", result.ValidationReportSha256) })
				{
				byte[] bytes = await ReadFile (Path.Combine (output, file.RelativePath), Limit, cancellationToken).ConfigureAwait (false);
				if (Hash (bytes) != file.Sha256) throw new InvalidDataException ("Retained revalidation files differ from the completed receipt.");
				if (file.RelativePath == "delivery-plan.json")
					{
					using var actualPlan = Parse (bytes);
					if (SubmissionDelivery.PlanDigest (actualPlan.RootElement.Deserialize<SubmissionDeliveryPlan> (Json)!) != digest)
						throw new InvalidDataException ("The retained plan differs from the revalidated request.");
					}
				}
			Write (Path.Combine (attempt, "finished.json"), new { Success = true, CompletedUtc = DateTimeOffset.UtcNow, ReceiptSha256 = marker });
			return new (digest, result.ExpiresUtc);
			}
		catch
			{
			Write (Path.Combine (attempt, "finished.json"), new { Success = false, CompletedUtc = DateTimeOffset.UtcNow });
			cancellationToken.ThrowIfCancellationRequested ();
			throw new InvalidDataException ("Delivery revalidation failed; inspect its private attempt. No transport was called by this checker.");
			}
		}

	private static async Task<byte[]> Capture (Stream input, int limit, bool retain, CancellationTokenSource stop)
		{
		try
			{
			using var memory = new MemoryStream (); var buffer = new byte[8192]; int count, total = 0;
			while ((count = await input.ReadAsync (buffer, stop.Token).ConfigureAwait (false)) != 0)
				{ if ((total += count) > limit) throw new InvalidDataException ("Revalidator output exceeded its limit."); if (retain) memory.Write (buffer, 0, count); }
			return memory.ToArray ();
			}
		catch { stop.Cancel (); throw; }
		}
	private static async Task<byte[]> Read (Stream file, int limit, CancellationToken token)
		{
		if (file.Length > limit) throw new InvalidDataException ("Revalidation metadata exceeds its limit.");
		var bytes = new byte[checked((int)file.Length)]; await file.ReadExactlyAsync (bytes, token).ConfigureAwait (false); return bytes;
		}
	private static async Task<byte[]> ReadFile (string path, int limit, CancellationToken token)
		{ using var file = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read); return await Read (file, limit, token).ConfigureAwait (false); }
	private static JsonDocument Parse (byte[] bytes)
		{
		var document = JsonDocument.Parse (bytes);
		void Check (JsonElement value)
			{
			if (value.ValueKind == JsonValueKind.Object)
				{ var names = new HashSet<string> (StringComparer.OrdinalIgnoreCase); foreach (var property in value.EnumerateObject ()) { if (!names.Add (property.Name)) throw new InvalidDataException ("Duplicate revalidation field."); Check (property.Value); } }
			else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray ()) Check (item);
			}
		try { Check (document.RootElement); return document; } catch { document.Dispose (); throw; }
		}
	private static void Write (string path, object value)
		{ using var file = new FileStream (path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); JsonSerializer.Serialize (file, value); file.Flush (true); }
	private static bool ValidHash (string value) => value?.Length == 64 && value.All (c => char.IsAsciiDigit (c) || c is >= 'a' and <= 'f');
	private static string Hash (byte[] bytes) => Convert.ToHexString (SHA256.HashData (bytes)).ToLowerInvariant ();
	private static bool Within (string path, string root)
		{
		string relative = Path.GetRelativePath (Path.GetFullPath (root), Path.GetFullPath (path));
		return relative == "." || (!Path.IsPathFullyQualified (relative) && relative != ".." && !relative.StartsWith (".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
		}
	private sealed class Pins : IDisposable
		{
		private readonly List<FileStream> _files = [];
		internal FileStream File (string path, string hash)
			{
			if (!ValidHash (hash) || (System.IO.File.GetAttributes (path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException ("Invalid tool pin or linked file.");
			var file = new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read); _files.Add (file);
			if (Convert.ToHexString (SHA256.HashData (file)).ToLowerInvariant () != hash) throw new InvalidDataException ("Reviewed tool or settings file changed.");
			file.Position = 0; return file;
			}
		internal void Directory (string root, IReadOnlyList<SubmissionEvidenceFile> files)
			{
			if (files == null || files.Count is 0 or > 4096 || files.Any (file => file == null) ||
				files.Select (file => file.RelativePath).Distinct (StringComparer.OrdinalIgnoreCase).Count () != files.Count) throw new InvalidDataException ("Invalid complete tool manifest.");
			var actual = new HashSet<string> (StringComparer.Ordinal); var pending = new Stack<string> (); pending.Push (root); int entries = 0;
			while (pending.TryPop (out var directory))
				{
				if ((System.IO.File.GetAttributes (directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException ("Linked tool directory.");
				foreach (var path in System.IO.Directory.EnumerateFileSystemEntries (directory))
					{
					var attributes = System.IO.File.GetAttributes (path);
					if ((attributes & FileAttributes.ReparsePoint) != 0 || ++entries > 4096) throw new InvalidDataException ("Unsafe tool tree.");
					if ((attributes & FileAttributes.Directory) != 0) pending.Push (path); else actual.Add (Path.GetRelativePath (root, path).Replace ('\\', '/'));
					}
				}
			if (!actual.SetEquals (files.Select (file => file.RelativePath))) throw new InvalidDataException ("Unpinned or missing tool file.");
			foreach (var file in files)
				{ if (!SubmissionEvidence.SafeEvidencePath (root, file.RelativePath, out var path)) throw new InvalidDataException ("Unsafe tool path."); File (path, file.Sha256); }
			}
		public void Dispose () { foreach (var file in _files) file.Dispose (); }
		}
	}