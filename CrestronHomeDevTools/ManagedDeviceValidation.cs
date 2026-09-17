// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

namespace CrestronHomeDevTools;

public sealed record ManagedDeviceTestTarget (string Alias, ManagedDeviceRequest Request);
public sealed record ManagedDeviceTestBinding (string Alias, int DeviceId, ManagedDeviceRequest Request);
public sealed record ManagedDeviceTestOutcome (bool Passed, bool RestorationConfirmed);
public sealed record ManagedDeviceValidationResult (bool Passed, bool RestorationConfirmed, bool CleanupConfirmed,
	IReadOnlyList<ManagedDeviceTestBinding> Bindings);

/// <summary>Commission, bind, validate and remove temporary managed children under a caller-owned processor reservation.</summary>
public static class ManagedDeviceValidation
	{
	/// <remarks>
	/// The connection factory must target the same processor as the held reservation. Ownership is verified around
	/// every stage. The test callback receives the actual created identities and must report independently verified
	/// physical/UI restoration. Exceptions or unconfirmed restoration retain children and journals for reconciliation.
	/// The caller releases its reservation only after restoration and cleanup are both confirmed.
	/// </remarks>
	public static Task<ManagedDeviceValidationResult> RunAsync (
		Func<CancellationToken, Task<ConfigurationClient>> openConnection,
		IReadOnlyList<ManagedDeviceTestTarget> targets, string journalDirectory, TimeSpan operationTimeout,
		Func<CancellationToken, Task> verifyOwnership,
		Func<IReadOnlyList<ManagedDeviceTestBinding>, CancellationToken, Task<ManagedDeviceTestOutcome>> runTests,
		CancellationToken cancellationToken = default)
		{
		ArgumentNullException.ThrowIfNull (openConnection);
		if (operationTimeout <= TimeSpan.Zero || operationTimeout > TimeSpan.FromHours (1)) throw new ArgumentOutOfRangeException (nameof (operationTimeout));
		return RunCoreAsync (targets, journalDirectory, verifyOwnership, runTests,
			async (request, journal, token) =>
				{
				await using var client = await openConnection (token).ConfigureAwait (false);
				return await ManagedDeviceCommissioning.CommissionAsync (client, request, journal, operationTimeout, token).ConfigureAwait (false);
				},
			async (journal, token) =>
				{
				// UI tests can outlast a configuration websocket's idle lifetime.
				await using var client = await openConnection (token).ConfigureAwait (false);
				return await ManagedDeviceCommissioning.RemoveCreatedAsync (client, journal, operationTimeout, token).ConfigureAwait (false);
				}, cancellationToken);
		}

	internal static async Task<ManagedDeviceValidationResult> RunCoreAsync (IReadOnlyList<ManagedDeviceTestTarget> targets,
		string journalDirectory, Func<CancellationToken, Task> verifyOwnership,
		Func<IReadOnlyList<ManagedDeviceTestBinding>, CancellationToken, Task<ManagedDeviceTestOutcome>> runTests,
		Func<ManagedDeviceRequest, string, CancellationToken, Task<ManagedDeviceResult>> commission,
		Func<string, CancellationToken, Task<ManagedDeviceCleanupResult>> cleanup, CancellationToken token = default)
		{
		ArgumentNullException.ThrowIfNull (targets);
		targets = targets.ToArray ();
		ArgumentNullException.ThrowIfNull (verifyOwnership);
		ArgumentNullException.ThrowIfNull (runTests);
		ArgumentException.ThrowIfNullOrWhiteSpace (journalDirectory);
		if (targets.Count == 0 || targets.Any (target => target == null || target.Request == null ||
			string.IsNullOrWhiteSpace (target.Alias) || target.Alias.Length > 64 || target.Alias.Any (c => !char.IsAsciiLetterOrDigit (c) && c is not ('_' or '-'))))
			throw new ArgumentException ("Provide named managed-child targets with simple, distinct aliases.", nameof (targets));
		if (targets.Select (target => target.Alias).Distinct (StringComparer.OrdinalIgnoreCase).Count () != targets.Count ||
			targets.Select (target => (target.Request.ParentId, target.Request.ManagedDeviceId)).Distinct ().Count () != targets.Count ||
			targets.Select (target => (target.Request.LocationId, target.Request.Name)).Distinct ().Count () != targets.Count)
			throw new ArgumentException ("Aliases, managed-child identities and room/name pairs must be distinct.", nameof (targets));
		foreach (var target in targets) ManagedDeviceCommissioning.Validate (target.Request);
		string directory = Path.GetFullPath (journalDirectory);
		if (Directory.Exists (directory) || File.Exists (directory)) throw new InvalidOperationException ("The validation journal already exists; reconcile the previous run instead of replaying it.");
		Directory.CreateDirectory (directory);
		void Record (string name, object value)
			{
			using var file = new FileStream (Path.Combine (directory, name + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			JsonSerializer.Serialize (file, value);
			file.Flush (true);
			}
		Record ("request", targets);
		var bindings = new List<ManagedDeviceTestBinding> ();
		ManagedDeviceValidationResult Finish (bool passed, bool restored, bool cleaned)
			{
			var result = new ManagedDeviceValidationResult (passed && restored && cleaned, restored, cleaned, Array.AsReadOnly (bindings.ToArray ()));
			Record ("result", result);
			return result;
			}
		try
			{
			foreach (var target in targets)
				{
				await verifyOwnership (token).ConfigureAwait (false);
				var created = await commission (target.Request, Path.Combine (directory, target.Alias), token).ConfigureAwait (false);
				await verifyOwnership (token).ConfigureAwait (false);
				if (created.DeviceId <= 0 || bindings.Any (binding => binding.DeviceId == created.DeviceId))
					throw new InvalidDataException ("Commissioning did not return distinct positive child IDs.");
				bindings.Add (new (target.Alias, created.DeviceId, target.Request));
				Record ("binding-" + target.Alias, bindings[^1]);
				if (created.State != "Ready") return Finish (false, false, false);
				}
			var frozen = Array.AsReadOnly (bindings.ToArray ());
			Record ("bindings", frozen);
			await verifyOwnership (token).ConfigureAwait (false);
			Record ("tests-intent", new { Utc = DateTimeOffset.UtcNow });
			var tests = await runTests (frozen, token).ConfigureAwait (false) ?? throw new InvalidDataException ("The test producer returned no outcome.");
			Record ("tests-result", tests);
			await verifyOwnership (token).ConfigureAwait (false);
			if (!tests.RestorationConfirmed) return Finish (false, false, false);
			foreach (var binding in bindings.AsEnumerable ().Reverse ())
				{
				await verifyOwnership (token).ConfigureAwait (false);
				var removed = await cleanup (Path.Combine (directory, binding.Alias), token).ConfigureAwait (false);
				await verifyOwnership (token).ConfigureAwait (false);
				if (removed.DeviceId != binding.DeviceId || !removed.Removed || !removed.OtherDevicesPreserved)
					throw new InvalidDataException ("Cleanup did not confirm the bound child and preservation of other devices.");
				Record ("removed-" + binding.Alias, removed);
				}
			return Finish (tests.Passed, true, true);
			}
		catch (Exception exception)
			{
			try { Record ("stopped", new { Type = exception.GetType ().Name, Utc = DateTimeOffset.UtcNow, ReconciliationRequired = true }); }
			catch { /* Preserve the original failure even if its evidence cannot be written. */ }
			throw;
			}
		}
	}