// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace CrestronHomeDevTools;

public enum DriverNameChallengePhase { Before, Challenge, Restored }
public sealed record DriverNameChallengeObservation (DriverNameChallengePhase Phase, int DeviceId, string ExpectedName, string AbsentName);
public sealed record DriverNameChallengeResult (int DeviceId, string OriginalName, string ChallengeName, bool ChallengeObserved, bool NameRestored);

/// <summary>Temporarily names a known driver instance for UI association. Requires externally held processor/UI reservations.</summary>
public static class DriverNameChallenge
	{
	private const string Command = "deviceName:setName";

	/// <summary>The observer must verify the expected UI tile and restore its own navigation even when its assertions fail.</summary>
	public static async Task<DriverNameChallengeResult> RunAsync (ConfigurationClient client, DriverInstanceReady target,
		string privateEvidenceDirectory, Action verifyOwnership,
		Func<DriverNameChallengeObservation, CancellationToken, Task> observe, TimeSpan timeout, CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (client);
		ArgumentNullException.ThrowIfNull (target);
		ArgumentNullException.ThrowIfNull (verifyOwnership);
		ArgumentNullException.ThrowIfNull (observe);
		if (target.DeviceId <= 0 || string.IsNullOrWhiteSpace (target.Model) || string.IsNullOrWhiteSpace (target.Version) ||
			timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (2) || !Path.IsPathFullyQualified (privateEvidenceDirectory) || !Directory.Exists (privateEvidenceDirectory))
			throw new ArgumentException ("Provide an exact installed target, existing private evidence directory and timeout up to two minutes.");
		var id = Guid.NewGuid ().ToString ("N");
		var challenge = "CI-" + id[..24];
		DeviceInfo? original = null;
		bool attempted = false, observed = false, restored = true;
		Exception? failure = null;
		async Task<DeviceInfo> Read (CancellationToken token)
			{
			verifyOwnership ();
			var current = await client.GetDeviceAsync (target.DeviceId, token).ConfigureAwait (false);
			if (current == null || current.Id != target.DeviceId || current.Model != target.Model || !current.Commands.Contains (Command) ||
				!current.PropertyValues.TryGetValue ("cp.driverInformation:version", out var version) || version.GetString () != target.Version ||
				!current.PropertyValues.TryGetValue ("cp.driverConfiguration:driverLoadingStatus", out var state) || state.GetString () != "Loaded" ||
				(original != null && (current.ParentDeviceId != original.ParentDeviceId || current.LocationId != original.LocationId)))
				throw new InvalidDataException ("Driver identity, readiness or location changed during name binding.");
			return current;
			}
		async Task WaitName (string name, CancellationToken token)
			{
			using var wait = CancellationTokenSource.CreateLinkedTokenSource (token);
			wait.CancelAfter (timeout);
			while ((await Read (wait.Token).ConfigureAwait (false)).Name != name)
				await Task.Delay (TimeSpan.FromMilliseconds (250), wait.Token).ConfigureAwait (false);
			}
		async Task Observe (DriverNameChallengePhase phase, string expected, string absent, CancellationToken token)
			{
			verifyOwnership ();
			using var wait = CancellationTokenSource.CreateLinkedTokenSource (token);
			wait.CancelAfter (timeout);
			await observe (new (phase, target.DeviceId, expected, absent), wait.Token).ConfigureAwait (false);
			verifyOwnership ();
			}
		void Record (string stage, object value)
			{
			using var stream = new FileStream (Path.Combine (privateEvidenceDirectory, "name-binding-" + id + "-" + stage + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			JsonSerializer.Serialize (stream, value);
			stream.Flush (flushToDisk: true);
			}
		try
			{
			original = await Read (cancellationToken).ConfigureAwait (false);
			if (string.IsNullOrWhiteSpace (original.Name)) throw new InvalidDataException ("Driver has no original name to restore.");
			verifyOwnership ();
			var devices = await client.GetDevicesAsync (cancellationToken).ConfigureAwait (false);
			if (devices.Count (d => d.Name == original.Name) != 1 || devices.Any (d => d.Name == challenge))
				throw new InvalidDataException ("Driver names are not unique within the processor inventory.");
			await Observe (DriverNameChallengePhase.Before, original.Name, challenge, cancellationToken).ConfigureAwait (false);
			if ((await Read (cancellationToken).ConfigureAwait (false)).Name != original.Name)
				throw new InvalidDataException ("Driver name changed before the challenge.");
			Record ("intent", new { Utc = DateTimeOffset.UtcNow, target.DeviceId, target.Model, target.Version, OriginalName = original.Name, ChallengeName = challenge, original.ParentDeviceId, original.LocationId });
			verifyOwnership ();
			attempted = true;
			restored = false;
			await client.ExecuteDeviceCommandAsync (target.DeviceId, Command, new { name = challenge }, cancellationToken).ConfigureAwait (false);
			await WaitName (challenge, cancellationToken).ConfigureAwait (false);
			observed = true;
			await Observe (DriverNameChallengePhase.Challenge, challenge, original.Name, cancellationToken).ConfigureAwait (false);
			}
		catch (Exception exception) { failure = exception; }
		finally
			{
			if (attempted && original != null)
				{
				// A cancelled assertion still gets bounded restoration, while ownership remains valid.
				using var cleanup = new CancellationTokenSource (timeout + timeout);
				try
					{
					var current = await Read (cleanup.Token).ConfigureAwait (false);
					if (current.Name == challenge)
						{
						verifyOwnership ();
						await client.ExecuteDeviceCommandAsync (target.DeviceId, Command, new { name = original.Name }, cleanup.Token).ConfigureAwait (false);
						await WaitName (original.Name!, cleanup.Token).ConfigureAwait (false);
						}
					else if (current.Name != original.Name || !observed)
						throw new InvalidDataException ("The outstanding rename is uncertain or another name was assigned; no request was replayed.");
					await Observe (DriverNameChallengePhase.Restored, original.Name!, challenge, cleanup.Token).ConfigureAwait (false);
					restored = true;
					}
				catch (Exception restoreFailure) { failure = failure == null ? restoreFailure : new AggregateException (failure, restoreFailure); }
				}
			try
				{
				Record ("result", new { Utc = DateTimeOffset.UtcNow, target.DeviceId, Attempted = attempted, ChallengeObserved = observed,
					NameRestored = restored, Passed = failure == null, Failure = failure?.GetType ().Name });
				}
			catch (Exception recordFailure) { failure = failure == null ? recordFailure : new AggregateException (failure, recordFailure); }
			}
		if (failure != null) ExceptionDispatchInfo.Capture (failure).Throw ();
		return new (target.DeviceId, original!.Name!, challenge, observed, restored);
		}
	}