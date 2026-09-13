// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeDevTools;

internal static class RebootConfirmation
	{
	internal static bool ValidateCliAuthorization (string? processor, string? confirmation, bool interactive)
		{
		if (confirmation == null && interactive)
			return false;
		if (string.IsNullOrWhiteSpace (processor) || string.IsNullOrWhiteSpace (confirmation)
			 || !string.Equals (processor, confirmation, StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException ("Unattended reboot requires --processor TARGET --confirm-reboot TARGET with matching values.");
		return true;
		}

	internal static async Task<bool> ConfirmAsync (ProcessorRebootTarget target, TextReader input, TextWriter output, CancellationToken cancellationToken)
		{
		var expected = "REBOOT " + target.Host;
		await output.WriteLineAsync ($"Reboot {target.SystemName ?? target.Host} ({target.Host})? This interrupts all Crestron Home programs and drivers.");
		await output.WriteLineAsync ($"Type {expected} to confirm. Anything else cancels:");
		var answer = await input.ReadLineAsync (cancellationToken);
		return string.Equals (answer, expected, StringComparison.Ordinal);
		}
	}