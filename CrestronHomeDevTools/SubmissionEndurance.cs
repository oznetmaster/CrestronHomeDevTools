// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

public enum SubmissionEnduranceState { Collecting, ProbePending, Passed, Failed, Interrupted }
public sealed record SubmissionEndurancePlan (SubmissionEvidenceIdentity Identity, SubmissionRequirement Requirement,
	string ProcessorIdentity, string InstallationIdentity, string ReservationId, string ProducerId,
	TimeSpan SampleInterval, TimeSpan ProbeTimeout);
public sealed record SubmissionEnduranceProbeResult (SubmissionEvidenceIdentity Identity, string ProcessorIdentity,
	string InstallationIdentity, string ReservationId, string ProducerId, string BootIdentity,
	SubmissionEvidenceOutcome Outcome, byte[] Evidence);
public sealed record SubmissionEnduranceSample (DateTimeOffset ObservedUtc, SubmissionEvidenceOutcome Outcome,
	SubmissionEvidenceFile File, string BootIdentity);
public sealed record SubmissionEnduranceCheckpoint (int SchemaVersion, string PlanSha256, SubmissionEnduranceState State,
	DateTimeOffset UpdatedUtc, IReadOnlyList<SubmissionEnduranceSample> Samples, string Reason = "");

/// <summary>
/// Performs one read-only functional observation when due. The trusted caller must protect the processor for the
/// entire run and supply a producer that verifies its reservation, installed candidate and independent function.
/// A local file lock prevents concurrent collectors; it does not replace the shared processor reservation.
/// </summary>
public static class SubmissionEndurance
	{
	private static readonly JsonSerializerOptions JsonOptions = new ()
		{ PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, Converters = { new JsonStringEnumConverter () } };
	private const int MaximumEvidenceBytes = 4 * 1024 * 1024;
	private const int MaximumRetainedBytes = 8 * 1024 * 1024;
	private sealed record RetainedSample (DateTimeOffset ObservedUtc, SubmissionEvidenceOutcome Outcome,
		string Reason, SubmissionEnduranceProbeResult Probe);

	/// <summary>
	/// Call periodically from a scheduler. An interrupted probe is never replayed in this run. Cancellation is
	/// cooperative: this method retains its lock until the producer exits, including after its deadline expires.
	/// Never implement the producer by issuing non-idempotent device commands or retrying them automatically.
	/// </summary>
	public static async Task<SubmissionEnduranceCheckpoint> CollectAsync (string privateRunDirectory,
		SubmissionEndurancePlan plan, Func<CancellationToken, Task<SubmissionEnduranceProbeResult>> probe,
		TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (probe);
		var digest = PlanDigest (plan);
		var clock = timeProvider ?? TimeProvider.System;
		using var journal = new Journal (privateRunDirectory);
		var checkpoint = journal.Read (digest) ?? new (1, digest, SubmissionEnduranceState.Collecting, clock.GetUtcNow (), []);
		if (checkpoint.State is SubmissionEnduranceState.Passed or SubmissionEnduranceState.Failed or SubmissionEnduranceState.Interrupted)
			return checkpoint;
		SubmissionEnduranceCheckpoint Stop (string reason, SubmissionEnduranceState state = SubmissionEnduranceState.Failed)
			{
			checkpoint = checkpoint with { State = state, Reason = reason, UpdatedUtc = clock.GetUtcNow () };
			journal.Write (checkpoint);
			return checkpoint;
			}
		if (checkpoint.State == SubmissionEnduranceState.ProbePending)
			return Stop ("probe-interrupted", SubmissionEnduranceState.Interrupted);
		var now = clock.GetUtcNow ();
		if (now < checkpoint.UpdatedUtc)
			return Stop ("clock-reversed");
		if (checkpoint.Samples.Count > 0)
			{
			var elapsed = now - checkpoint.Samples[^1].ObservedUtc;
			if (elapsed > TimeSpan.FromSeconds (plan.Requirement.Execution!.MaximumSampleGapSeconds!.Value))
				return Stop ("sample-gap");
			if (elapsed < plan.SampleInterval)
				return checkpoint;
			}
		cancellationToken.ThrowIfCancellationRequested ();
		checkpoint = checkpoint with { State = SubmissionEnduranceState.ProbePending, UpdatedUtc = now };
		journal.Write (checkpoint); // Durable intent precedes the observation; a crash cannot silently erase it.
		SubmissionEnduranceProbeResult result;
		var started = clock.GetTimestamp ();
		using var deadline = new CancellationTokenSource (plan.ProbeTimeout, clock);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken, deadline.Token);
		try
			{
			result = await probe (linked.Token).ConfigureAwait (false);
			}
		catch (OperationCanceledException)
			{
			return Stop (deadline.IsCancellationRequested ? "probe-timeout" : "probe-cancelled", SubmissionEnduranceState.Interrupted);
			}
		catch (Exception exception) when (exception is not OutOfMemoryException)
			{
			// Exceptions can contain credentials or response bodies. Retain only a stable failure code here.
			return Stop ("probe-exception");
			}
		var observed = clock.GetUtcNow ();
		if (result?.Evidence == null || result.Evidence.Length is 0 or > MaximumEvidenceBytes)
			return Stop ("probe-evidence-invalid");
		var outcome = result.Outcome;
		var reason = "";
		if (result.Identity != plan.Identity || result.ProcessorIdentity != plan.ProcessorIdentity ||
			result.InstallationIdentity != plan.InstallationIdentity || result.ReservationId != plan.ReservationId || result.ProducerId != plan.ProducerId)
			reason = "probe-identity-mismatch";
		else if (string.IsNullOrWhiteSpace (result.BootIdentity) ||
			(checkpoint.Samples.Count > 0 && result.BootIdentity != checkpoint.Samples[0].BootIdentity))
			reason = "processor-restarted-or-unknown";
		else if (observed < now || (checkpoint.Samples.Count > 0 && observed <= checkpoint.Samples[^1].ObservedUtc))
			reason = "clock-reversed";
		else if (deadline.IsCancellationRequested || clock.GetElapsedTime (started) > plan.ProbeTimeout)
			reason = "probe-timeout";
		else if (cancellationToken.IsCancellationRequested)
			reason = "probe-cancelled";
		else if (checkpoint.Samples.Count > 0 && observed - checkpoint.Samples[^1].ObservedUtc >
			TimeSpan.FromSeconds (plan.Requirement.Execution!.MaximumSampleGapSeconds!.Value))
			reason = "sample-gap";
		else if (outcome != SubmissionEvidenceOutcome.Passed)
			reason = "functional-check-not-passed";
		if (reason.Length > 0)
			outcome = SubmissionEvidenceOutcome.Failed;
		var file = journal.Retain (checkpoint.Samples.Count, JsonSerializer.SerializeToUtf8Bytes (
			new RetainedSample (observed, outcome, reason, result), JsonOptions));
		checkpoint = checkpoint with { State = reason.Length == 0 ? SubmissionEnduranceState.Collecting : SubmissionEnduranceState.Failed,
			UpdatedUtc = observed, Reason = reason, Samples = [.. checkpoint.Samples, new (observed, outcome, file, result.BootIdentity)] };
		if (checkpoint.State == SubmissionEnduranceState.Collecting && checkpoint.Samples.Count >= 2 &&
			observed - checkpoint.Samples[0].ObservedUtc >= plan.Requirement.MinimumDuration)
			{
			var observation = Observation (plan, checkpoint);
			var report = SubmissionEvidence.Evaluate (plan.Identity, [plan.Requirement], [observation], journal.Root, observed);
			checkpoint = checkpoint with { State = report.EvidenceChecksPassed ? SubmissionEnduranceState.Passed : SubmissionEnduranceState.Failed,
				Reason = report.EvidenceChecksPassed ? "" : "evidence-validation-failed" };
			}
		journal.Write (checkpoint);
		return checkpoint;
		}

	/// <summary>Exports only a completed, revalidated run; partial time never becomes a passing observation.</summary>
	public static SubmissionObservation Export (string privateRunDirectory, SubmissionEndurancePlan plan, DateTimeOffset now)
		{
		using var journal = new Journal (privateRunDirectory);
		var checkpoint = journal.Read (PlanDigest (plan));
		if (checkpoint?.State != SubmissionEnduranceState.Passed)
			throw new InvalidOperationException ("Endurance collection has not passed.");
		var observation = Observation (plan, checkpoint);
		if (!SubmissionEvidence.Evaluate (plan.Identity, [plan.Requirement], [observation], journal.Root, now).EvidenceChecksPassed)
			throw new InvalidDataException ("Retained endurance evidence no longer passes validation.");
		return observation;
		}

	private static SubmissionObservation Observation (SubmissionEndurancePlan plan, SubmissionEnduranceCheckpoint checkpoint) =>
		new (plan.Requirement.Id, plan.Identity, SubmissionEvidenceOutcome.Passed, checkpoint.Samples[0].ObservedUtc,
			checkpoint.Samples[^1].ObservedUtc, checkpoint.Samples.Select (sample => sample.File).ToArray (),
			Execution: new (plan.Requirement.Execution!.Target, "endurance", Samples: checkpoint.Samples.Select (
				sample => new SubmissionFunctionalSample (sample.ObservedUtc, sample.Outcome, sample.File.RelativePath)).ToArray ()));

	/// <summary>Reads and verifies existing evidence without starting another observation.</summary>
	public static SubmissionEnduranceCheckpoint? ReadCheckpoint (string privateRunDirectory, SubmissionEndurancePlan plan)
		{
		using var journal = new Journal (privateRunDirectory);
		return journal.Read (PlanDigest (plan));
		}

	internal static string PlanDigest (SubmissionEndurancePlan plan)
		{
		ArgumentNullException.ThrowIfNull (plan);
		ArgumentNullException.ThrowIfNull (plan.Requirement);
		SubmissionExecution.ValidatePolicy (plan.Requirement);
		if (plan.Requirement.Execution is not { Method: "endurance", Restore: false, ResponseLimitSeconds: null,
			RequiredOutcome: SubmissionEvidenceOutcome.Passed, MaximumSampleGapSeconds: > 0 } ||
			plan.Requirement.MinimumDuration <= TimeSpan.Zero || string.IsNullOrWhiteSpace (plan.Requirement.Id) ||
			plan.SampleInterval <= TimeSpan.Zero || plan.ProbeTimeout <= TimeSpan.Zero || plan.ProbeTimeout.TotalMilliseconds > uint.MaxValue - 1 ||
			plan.SampleInterval + plan.ProbeTimeout > TimeSpan.FromSeconds (plan.Requirement.Execution.MaximumSampleGapSeconds.Value))
			throw new ArgumentException ("The collector requires a read-only endurance policy, positive duration and cadence with time for each probe within the approved gap.");
		foreach (var value in new[] { plan.ProcessorIdentity, plan.InstallationIdentity, plan.ReservationId, plan.ProducerId })
			ArgumentException.ThrowIfNullOrWhiteSpace (value);
		if (plan.Identity == null || !Hex (plan.Identity.PackageSha256, 64) || !Hex (plan.Identity.PolicySha256, 64) ||
			!Hex (plan.Identity.TemplateSha256, 64) || !(Hex (plan.Identity.SourceCommit, 40) || Hex (plan.Identity.SourceCommit, 64)))
			throw new ArgumentException ("The collector requires complete immutable candidate pins.");
		return Hash (JsonSerializer.SerializeToUtf8Bytes (plan, JsonOptions));
		}
	private static bool Hex (string? value, int length) => value?.Length == length && value.All (char.IsAsciiHexDigit);
	private static string Hash (byte[] value) => Convert.ToHexString (SHA256.HashData (value));

	private sealed class Journal : IDisposable
		{
		internal string Root { get; }
		private readonly FileStream _lock;
		internal Journal (string directory)
			{
			Root = Path.GetFullPath (directory);
			Directory.CreateDirectory (Root);
			if ((File.GetAttributes (Root) & FileAttributes.ReparsePoint) != 0)
				throw new InvalidDataException ("The private run directory must not be a link.");
			var lockPath = Path.Combine (Root, "collector.lock");
			if (File.Exists (lockPath) && !SubmissionEvidence.SafeEvidencePath (Root, "collector.lock", out _))
				throw new InvalidDataException ("The collector lock must not be a link.");
			_lock = new FileStream (lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			}
		internal SubmissionEnduranceCheckpoint? Read (string digest)
			{
			var path = Path.Combine (Root, "checkpoint.json");
			if (!File.Exists (path))
				{
				if (Directory.EnumerateFiles (Root).Any (file => Path.GetFileName (file) != "collector.lock"))
					throw new InvalidDataException ("A run with orphaned evidence requires review; it cannot be silently restarted.");
				return null;
				}
			if (!SubmissionEvidence.SafeEvidencePath (Root, "checkpoint.json", out _) || new FileInfo (path).Length > 16 * 1024 * 1024)
				throw new InvalidDataException ("Invalid checkpoint file.");
			var checkpoint = JsonSerializer.Deserialize<SubmissionEnduranceCheckpoint> (File.ReadAllBytes (path), JsonOptions);
			if (checkpoint == null || checkpoint.SchemaVersion != 1 || checkpoint.PlanSha256 != digest ||
				!Enum.IsDefined (checkpoint.State) || checkpoint.Samples == null || checkpoint.UpdatedUtc == default)
				throw new InvalidDataException ("Checkpoint is invalid or belongs to a different plan.");
			for (var i = 0; i < checkpoint.Samples.Count; i++)
				{
				var sample = checkpoint.Samples[i];
				if (sample?.File == null || sample.File.RelativePath != $"sample-{i:D6}.json" ||
					!SubmissionEvidence.SafeEvidencePath (Root, sample.File.RelativePath, out var evidencePath) ||
					new FileInfo (evidencePath).Length > MaximumRetainedBytes)
					throw new InvalidDataException ("Retained functional evidence is missing or changed.");
				var bytes = File.ReadAllBytes (evidencePath);
				if (Hash (bytes) != sample.File.Sha256)
					throw new InvalidDataException ("Retained functional evidence is missing or changed.");
				var retained = JsonSerializer.Deserialize<RetainedSample> (bytes, JsonOptions);
				if (retained?.Probe == null || retained.ObservedUtc != sample.ObservedUtc || retained.Outcome != sample.Outcome ||
					retained.Probe.BootIdentity != sample.BootIdentity)
					throw new InvalidDataException ("The checkpoint disagrees with its retained observation.");
				}
			return checkpoint;
			}
		internal SubmissionEvidenceFile Retain (int index, byte[] bytes)
			{
			var name = $"sample-{index:D6}.json";
			using var output = new FileStream (Path.Combine (Root, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
			output.Write (bytes);
			output.Flush (true);
			return new (name, Hash (bytes));
			}
		internal void Write (SubmissionEnduranceCheckpoint checkpoint)
			{
			var temporary = Path.Combine (Root, "checkpoint-" + Guid.NewGuid ().ToString ("N") + ".tmp");
			using (var output = new FileStream (temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
				output.Write (JsonSerializer.SerializeToUtf8Bytes (checkpoint, JsonOptions));
				output.Flush (true);
				}
			File.Move (temporary, Path.Combine (Root, "checkpoint.json"), true);
			}
		public void Dispose () => _lock.Dispose ();
		}
	}