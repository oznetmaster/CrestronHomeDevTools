// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestronHomeDevTools;

public sealed record SubmissionOutageRecordingContext (string EvidenceDirectory, SubmissionEvidenceIdentity Identity);
public sealed record SubmissionOutageRestoredState (SubmissionOutageCapture Capture, bool MatchesOriginal);

/// <summary>Optional human gate after preparation, before a fresh baseline and interruption.
/// Readiness never operates hardware; the recorder excludes this wait from its observation budget.</summary>
public interface ISubmissionOutageReadiness
{
 Task WaitUntilReadyAsync(CancellationToken token);
}

/// <summary>Trusted, explicitly configured hardware bindings. Preflight and baseline capture must be
/// read-only. Operations must honor cancellation and retain raw evidence inside the supplied directory.
/// Connectivity control must remain available while the selected processor/device is interrupted.</summary>
public interface ISubmissionOutageHardware
	{
	/// <summary>True only when the program capture bounds its start, not load completion.</summary>
	bool ProgramLoadIsLowerBound => false;
	IReadOnlyList<string> Components
		{
		get;
		}
	IReadOnlyList<string> Functions
		{
		get;
		}
	Task PreflightAsync (SubmissionOutageRecordingContext context, CancellationToken token);
	Task<SubmissionOutageCapture> CaptureOriginalAsync (CancellationToken token);
	Task<SubmissionOutageCapture> InterruptAsync (string component, CancellationToken token);
	Task<SubmissionOutageCapture> RestoreConnectivityAsync (string component, CancellationToken token);
	Task<SubmissionOutageCapture?> ObserveProgramLoadedAsync (string component, CancellationToken token);
	Task<SubmissionOutageFunction> VerifyFunctionAsync (string function, CancellationToken token);
	Task<SubmissionOutageRestoredState> RestoreOriginalAsync (SubmissionOutageCapture original, CancellationToken token);
	}

public sealed record SubmissionOutageRecordingResult (string EvidenceDirectory,
	 string? RecordRelativePath, SubmissionOutageMeasurementReport? Measurements, string[] Issues)
	{
	public bool Passed => Issues.Length == 0 && Measurements?.MeasurementChecksPassed == true;
	}

/// <summary>Runs an initial outage capture with durable progress and a separate restoration budget.
/// It does not infer electrical/network events from ping, operator replies or API availability.</summary>
public static class SubmissionOutageRecorder
	{
	private static readonly JsonSerializerOptions Json = new ()
		{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter () }
		};

	public static Task<SubmissionOutageRecordingResult> RecordAsync (SubmissionOutageMeasurementPlan plan,
		 ISubmissionOutageHardware hardware, string evidenceDirectory, TimeSpan observationTimeout,
		 TimeSpan restorationTimeout, CancellationToken token = default) =>
		 RecordCoreAsync (plan, hardware, evidenceDirectory, observationTimeout, restorationTimeout,
			  TimeProvider.System, (duration, ct) => Task.Delay (duration, ct), token);

	internal static async Task<SubmissionOutageRecordingResult> RecordCoreAsync (SubmissionOutageMeasurementPlan plan,
		 ISubmissionOutageHardware hardware, string evidenceDirectory, TimeSpan observationTimeout,
		 TimeSpan restorationTimeout, TimeProvider clock, Func<TimeSpan, CancellationToken, Task> delay,
		 CancellationToken token)
		{
		SubmissionOutageMeasurements.ValidatePlan (plan);
		ArgumentNullException.ThrowIfNull (hardware);
		token.ThrowIfCancellationRequested ();
		plan = plan with
			{
			RequiredComponents = [.. plan.RequiredComponents],
			RequiredFunctions = [.. plan.RequiredFunctions]
			};
		static bool Same (string[] expected, IReadOnlyList<string> actual) => actual != null &&
			 expected.Order (StringComparer.Ordinal).SequenceEqual (actual.Order (StringComparer.Ordinal), StringComparer.Ordinal);
		if (!Same (plan.RequiredComponents, hardware.Components) || !Same (plan.RequiredFunctions, hardware.Functions) ||
			 observationTimeout <= plan.MinimumInterruption || observationTimeout > TimeSpan.FromHours (1) ||
			 restorationTimeout < TimeSpan.FromSeconds (1) || restorationTimeout > TimeSpan.FromHours (1))
			throw new InvalidDataException ("Bind exactly the planned components/functions and bounded observation/restoration timeouts before recording.");
		string root = Path.GetFullPath (evidenceDirectory);
		if (Directory.Exists (root))
			throw new IOException ("Outage recording requires a new evidence directory; interrupted recordings are never replayed.");
		Directory.CreateDirectory (root);
		using var exclusive = new FileStream (Path.Combine (root, "recording.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
		void Save (string name, object value)
			{
			using var output = new FileStream (Path.Combine (root, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			JsonSerializer.Serialize (output, value, Json);
			output.Flush (flushToDisk: true);
			}
		int sequence = 0;
		void Journal (string phase, object value) => Save ($"{sequence++:D3}-{phase}.json", new
			{
			Utc = clock.GetUtcNow (),
			Value = value
			});
		Save ("plan.json", plan);
		DateTimeOffset started = clock.GetUtcNow ();
		void Capture (SubmissionOutageCapture capture)
			{
			if (capture == null || capture.EarliestUtc < started || capture.LatestUtc < capture.EarliestUtc ||
				 capture.LatestUtc > clock.GetUtcNow () || capture.Evidence == null ||
				 !SubmissionEvidence.SafeEvidencePath (root, capture.Evidence.RelativePath, out var file))
				throw new InvalidDataException ("Hardware capture lacks fresh, ordered bounds and retained evidence.");
			using var input = File.OpenRead (file);
			if (input.Length > 64 * 1024 * 1024 || !string.Equals (Convert.ToHexStringLower (SHA256.HashData (input)),
					  capture.Evidence.Sha256, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException ("Hardware capture evidence changed or exceeds its size limit.");
			}
		var issues = new List<string> ();
		var attempts = new List<string> ();
		var interrupted = new Dictionary<string, SubmissionOutageCapture> (StringComparer.Ordinal);
		var restored = new Dictionary<string, SubmissionOutageCapture> (StringComparer.Ordinal);
		var functions = new List<SubmissionOutageFunction> ();
		SubmissionOutageCapture? original = null, loaded = null;
		SubmissionOutageRestoredState? verified = null;
		string stage = "preflight";
		using var observation = CancellationTokenSource.CreateLinkedTokenSource (token);
		observation.CancelAfter (observationTimeout);
		long activeStarted = clock.GetTimestamp();
		// Neither cleanup budget inherits caller cancellation nor the observation timeout.
		try
			{
			await hardware.PreflightAsync (new (root, plan.Identity), observation.Token).ConfigureAwait (false);
			if(hardware is ISubmissionOutageReadiness readiness) {
				stage="readiness";
				observation.Token.ThrowIfCancellationRequested();
				var remaining=observationTimeout-clock.GetElapsedTime(activeStarted);
				if(remaining<=TimeSpan.Zero)throw new TimeoutException("Outage preparation exhausted its budget.");
				observation.CancelAfter(Timeout.InfiniteTimeSpan);
				await readiness.WaitUntilReadyAsync(token).ConfigureAwait(false);
				observation.CancelAfter(remaining);
			}
			stage = "baseline";
			original = await hardware.CaptureOriginalAsync (observation.Token).ConfigureAwait (false);
			Capture (original);
			Journal (stage, original);
			foreach (string component in plan.RequiredComponents)
				{
				stage = "interrupt";
				observation.Token.ThrowIfCancellationRequested ();
				Journal ("interrupt-intent", new
					{
					Component = component
					});
				attempts.Add (component); // Restore even if an interrupted command throws after changing hardware.
				var capture = await hardware.InterruptAsync (component, observation.Token).ConfigureAwait (false);
				Capture (capture);
				interrupted.Add (component, capture);
				Journal ("interrupted", new
					{
					Component = component,
					Capture = capture
					});
				}
			stage = "hold";
			// Start after ALL interruptions have been acknowledged; never use a delayed operator reply
			// as the physical event time. The assessor independently checks the captured common interval.
			Journal ("hold-start", new
				{
				plan.MinimumInterruption
				});
			await delay (plan.MinimumInterruption, observation.Token).ConfigureAwait (false);
			Journal ("hold-complete", new
				{
				plan.MinimumInterruption
				});
			}
		catch (Exception error)
			{
			issues.Add (stage + ":" + error.GetType ().Name);
			// Do not persist arbitrary exception messages: transports can include credentials.
			}
		finally
			{
			if (attempts.Count > 0)
				{
				foreach (string component in attempts.AsEnumerable ().Reverse ())
					{
					// A timed-out component must not consume the next component's restoration budget.
					using var recovery = new CancellationTokenSource (restorationTimeout);
					try
						{
						// Recovery must still be attempted if a progress write failed.
						var capture = await hardware.RestoreConnectivityAsync (component, recovery.Token).ConfigureAwait (false);
						Capture (capture);
						restored.Add (component, capture);
						Journal ("connectivity-restored", new
							{
							Component = component,
							Capture = capture
							});
						}
					catch (Exception error) { issues.Add ("connectivity-restoration:" + error.GetType ().Name); }
					}
				try
					{
					if (issues.Count == 0)
						{
						stage = "program-load";
						if (plan.RecoveryClock == SubmissionOutageRecoveryClock.ProgramLoaded)
							{
							loaded = await hardware.ObserveProgramLoadedAsync (plan.ProgramComponent!, observation.Token).ConfigureAwait (false);
							if (loaded != null)
								Capture (loaded);
							Journal (stage, new
								{
								Capture = loaded
								});
							}
						foreach (string function in plan.RequiredFunctions)
							{
							stage = "function";
							observation.Token.ThrowIfCancellationRequested ();
							var result = await hardware.VerifyFunctionAsync (function, observation.Token).ConfigureAwait (false);
							if (result.Id != function)
								throw new InvalidDataException ("Functional assertion identity changed.");
							Capture (result.Observation);
							functions.Add (result);
							Journal (stage, result);
							}
						}
					}
				catch (Exception error) { issues.Add (stage + ":" + error.GetType ().Name); }
				finally
					{
					// A distinct budget also covers restoration of app navigation and collateral devices.
					using var cleanup = new CancellationTokenSource (restorationTimeout);
					try
						{
						verified = await hardware.RestoreOriginalAsync (original!, cleanup.Token).ConfigureAwait (false);
						Capture (verified.Capture);
						Journal ("original-state-restored", verified);
						}
					catch (Exception error) { issues.Add ("original-restoration:" + error.GetType ().Name); }
					}
				}
			}
		SubmissionOutageMeasurementReport? measurements = null;
		string? recordPath = null;
		if (issues.Count == 0 && original != null && verified != null)
			{
			bool lowerBound = plan.RecoveryClock == SubmissionOutageRecoveryClock.ProgramLoaded && hardware.ProgramLoadIsLowerBound;
			var record = new SubmissionOutageMeasurementRecord (lowerBound ? 2 : 1, plan.Identity,
				 plan.RequiredComponents.Select (c => new SubmissionComponentInterruption (c, interrupted[c], restored[c])).ToArray (),
				 loaded, functions.ToArray (), original, verified.Capture, verified.MatchesOriginal)
				{ ProgramLoadIsLowerBound = lowerBound };
			Save ("measurements.json", record);
			try
				{
				measurements = SubmissionOutageMeasurements.Assess (plan, record, root, clock.GetUtcNow ());
				recordPath = "measurements.json";
				Save ("assessment.json", measurements);
				}
			catch (Exception error) { issues.Add ("measurement-validation:" + error.GetType ().Name); }
			}
		var outcome = new SubmissionOutageRecordingResult (root, recordPath, measurements, issues.ToArray ());
		Save ("recording-result.json", outcome);
		return outcome;
		}
	}
