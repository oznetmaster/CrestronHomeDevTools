// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record SubmissionEnduranceProcessor (string Host, string SshFingerprint);
public sealed record SubmissionEnduranceMonitorStatus (string ReservationState, SubmissionEnduranceCheckpoint? Checkpoint);

/// <summary>
/// Retains a shared processor reservation between scheduled collector invocations. Start explicitly once;
/// Collect resumes that ownership, and Finish releases only after a known terminal observation.
/// Uncertain acquisition, release or an interrupted probe requires reconciliation, never automatic replay.
/// </summary>
public static class SubmissionEnduranceMonitor
	{
	/// <summary>The collector's evidence directory, separate from the monitor's ownership journal.</summary>
	public static string GetEvidenceDirectory (string directory) => Path.Combine (Path.GetFullPath (directory), "observations");

	/// <summary>Inspect both durable ownership and observations without contacting the processor or running a probe.</summary>
	public static SubmissionEnduranceMonitorStatus ReadStatus (string directory, SubmissionEndurancePlan plan, SubmissionEnduranceProcessor processor)
		{
		using var journal = new MonitorJournal (directory, plan, processor);
		return new (journal.Read () ?? "NotStarted", SubmissionEndurance.ReadCheckpoint (GetEvidenceDirectory (directory), plan));
		}

	public static Task StartAsync (string directory, SubmissionEndurancePlan plan, SubmissionEnduranceProcessor processor,
		NetworkCredential credential, CancellationToken token = default) => StartCoreAsync (directory, plan, processor,
		async ct => await ProcessorOperationLease.AcquireAsync (processor.Host, credential, processor.SshFingerprint, plan.ReservationId, ct).ConfigureAwait (false), token);

	public static Task<SubmissionEnduranceCheckpoint> CollectAsync (string directory, SubmissionEndurancePlan plan,
		SubmissionEnduranceProcessor processor, NetworkCredential credential,
		Func<CancellationToken, Task<SubmissionEnduranceProbeResult>> probe, CancellationToken token = default) =>
		CollectCoreAsync (directory, plan, processor,
			async ct => await ProcessorOperationLease.ResumeAsync (processor.Host, credential, processor.SshFingerprint, plan.ReservationId, ct).ConfigureAwait (false),
			probe, TimeProvider.System, token);

	public static Task FinishAsync (string directory, SubmissionEndurancePlan plan, SubmissionEnduranceProcessor processor,
		NetworkCredential credential, CancellationToken token = default) => FinishCoreAsync (directory, plan, processor,
		async ct => await ProcessorOperationLease.ResumeAsync (processor.Host, credential, processor.SshFingerprint, plan.ReservationId, ct).ConfigureAwait (false), token);

	internal static async Task StartCoreAsync (string directory, SubmissionEndurancePlan plan, SubmissionEnduranceProcessor processor,
		Func<CancellationToken, Task<IProcessorOperationLease>> acquire, CancellationToken token = default)
		{
		using var journal = new MonitorJournal (directory, plan, processor);
		if (journal.Read () != null || SubmissionEndurance.ReadCheckpoint (GetEvidenceDirectory (directory), plan) != null)
			throw new InvalidOperationException ("An existing monitor cannot be started again. Inspect its recorded ownership and outcome.");
		journal.Write ("Acquiring");
		using var lease = await acquire (token).ConfigureAwait (false);
		RequireOwner (lease, plan);
		journal.Write ("Held");
		// Disposing closes the transport only. The remote reservation intentionally remains.
		}

	/// <summary>Explicit operator stop between probes. Preserves all observations and the original requirement,
	/// records operator-stopped for an incomplete collection, and releases only its verified reservation.</summary>
	public static Task StopAsync (string directory, SubmissionEndurancePlan plan, SubmissionEnduranceProcessor processor,
		NetworkCredential credential, CancellationToken token = default) => StopCoreAsync (directory, plan, processor,
		async ct => await ProcessorOperationLease.ResumeAsync (processor.Host, credential, processor.SshFingerprint, plan.ReservationId, ct).ConfigureAwait (false), token);

	internal static async Task StopCoreAsync (string directory, SubmissionEndurancePlan plan, SubmissionEnduranceProcessor processor,
		Func<CancellationToken, Task<IProcessorOperationLease>> resume, CancellationToken token = default)
		{
		using var journal = new MonitorJournal (directory, plan, processor);
		var checkpoint = SubmissionEndurance.ReadCheckpoint (GetEvidenceDirectory (directory), plan);
		string? state = journal.Read ();
		if (state == "Released" && checkpoint?.State is SubmissionEnduranceState.Passed or SubmissionEnduranceState.Failed) return;
		if (state != "Held" || checkpoint?.State is not (SubmissionEnduranceState.Collecting or SubmissionEnduranceState.Passed or SubmissionEnduranceState.Failed))
			throw new InvalidOperationException ("Inspect pending probes or uncertain ownership before stopping.");
		using var lease = await resume (token).ConfigureAwait (false);
		RequireOwner (lease, plan);
		_ = SubmissionEndurance.Stop (GetEvidenceDirectory (directory), plan);
		journal.Write ("Releasing");
		await lease.ReleaseAsync (token).ConfigureAwait (false);
		journal.Write ("Released");
		}

	internal static async Task<SubmissionEnduranceCheckpoint> CollectCoreAsync (string directory, SubmissionEndurancePlan plan,
		SubmissionEnduranceProcessor processor, Func<CancellationToken, Task<IProcessorOperationLease>> resume,
		Func<CancellationToken, Task<SubmissionEnduranceProbeResult>> probe, TimeProvider clock, CancellationToken token = default)
		{
		ArgumentNullException.ThrowIfNull (probe);
		using var journal = new MonitorJournal (directory, plan, processor);
		string state = journal.Read () ?? throw new InvalidOperationException ("Start the monitor before collecting observations.");
		var checkpoint = SubmissionEndurance.ReadCheckpoint (GetEvidenceDirectory (directory), plan);
		if (state == "Released" && checkpoint?.State is SubmissionEnduranceState.Passed or SubmissionEnduranceState.Failed)
			return checkpoint;
		if (state != "Held")
			throw new InvalidOperationException ("Monitor ownership requires reconciliation before another observation.");
		return await SubmissionEndurance.CollectAsync (GetEvidenceDirectory (directory), plan, async ct =>
			{
				using var before = await resume (ct).ConfigureAwait (false);
				RequireOwner (before, plan);
				var result = await probe (ct).ConfigureAwait (false);
				// Independently reconnect after the probe; returning the planned owner in a result is insufficient.
				using var after = await resume (ct).ConfigureAwait (false);
				RequireOwner (after, plan);
				return result;
			}, clock, token).ConfigureAwait (false);
		}

	internal static async Task FinishCoreAsync (string directory, SubmissionEndurancePlan plan, SubmissionEnduranceProcessor processor,
		Func<CancellationToken, Task<IProcessorOperationLease>> resume, CancellationToken token = default)
		{
		using var journal = new MonitorJournal (directory, plan, processor);
		string? state = journal.Read ();
		var checkpoint = SubmissionEndurance.ReadCheckpoint (GetEvidenceDirectory (directory), plan);
		if (checkpoint?.State is not (SubmissionEnduranceState.Passed or SubmissionEnduranceState.Failed))
			throw new InvalidOperationException ("Only a completed or failed read-only run can finish automatically; pending or interrupted probes require inspection.");
		if (state == "Released") return;
		if (state != "Held")
			throw new InvalidOperationException ("Monitor ownership requires reconciliation; release is not retried.");
		using var lease = await resume (token).ConfigureAwait (false);
		RequireOwner (lease, plan);
		journal.Write ("Releasing");
		await lease.ReleaseAsync (token).ConfigureAwait (false);
		journal.Write ("Released");
		}

	private static void RequireOwner (IProcessorOperationLease lease, SubmissionEndurancePlan plan)
		{
		if (lease.Owner != plan.ReservationId)
			throw new InvalidDataException ("The resumed processor reservation does not belong to this monitor.");
		}

	private sealed record MonitorRecord (int SchemaVersion, string PlanSha256, SubmissionEnduranceProcessor Processor, string State);
	private sealed class MonitorJournal : IDisposable
		{
		private readonly string _root;
		private readonly string _digest;
		private readonly SubmissionEnduranceProcessor _processor;
		private readonly FileStream _lock;
		internal MonitorJournal (string directory, SubmissionEndurancePlan plan, SubmissionEnduranceProcessor processor)
			{
			ArgumentNullException.ThrowIfNull (processor);
			ArgumentException.ThrowIfNullOrWhiteSpace (processor.Host);
			ArgumentException.ThrowIfNullOrWhiteSpace (processor.SshFingerprint);
			_digest = SubmissionEndurance.PlanDigest (plan);
			if (!Guid.TryParseExact (plan.ReservationId, "N", out _))
				throw new ArgumentException ("A monitor reservation must be a unique GUID in N format.");
			_processor = processor;
			_root = Path.GetFullPath (directory);
			Directory.CreateDirectory (_root);
			if ((File.GetAttributes (_root) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("The private monitor directory must not be a link.");
			CheckPath ("monitor.lock");
			_lock = new FileStream (Path.Combine (_root, "monitor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			}
		private string CheckPath (string name)
			{
			string path = Path.Combine (_root, name);
			if (File.Exists (path) && !SubmissionEvidence.SafeEvidencePath (_root, name, out _))
				throw new InvalidDataException ("A monitor journal path must not be a link.");
			return path;
			}
		internal string? Read ()
			{
			string path = CheckPath ("monitor.json");
			if (!File.Exists (path)) return null;
			if (new FileInfo (path).Length > 64 * 1024)
				throw new InvalidDataException ("The monitor record exceeds its size limit.");
			var record = JsonSerializer.Deserialize<MonitorRecord> (File.ReadAllBytes (path));
			if (record?.SchemaVersion != 1 || record.PlanSha256 != _digest || record.Processor != _processor ||
				record.State is not ("Acquiring" or "Held" or "Releasing" or "Released"))
				throw new InvalidDataException ("Monitor identity or ownership state changed.");
			return record.State;
			}
		internal void Write (string state)
			{
			string path = CheckPath ("monitor.json");
			string temporary = Path.Combine (_root, "monitor-" + Guid.NewGuid ().ToString ("N") + ".tmp");
			using (var output = new FileStream (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				JsonSerializer.Serialize (output, new MonitorRecord (1, _digest, _processor, state));
				output.Flush (true);
				}
			SubmissionJournalFile.Replace (temporary, path);
			}
		public void Dispose () => _lock.Dispose ();
		}
	}
