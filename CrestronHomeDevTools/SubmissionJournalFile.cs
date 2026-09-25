// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeDevTools;

// A brief Windows sharing/access refusal must not cause replay of a provider request.
// Retry only the same atomic rename of an already flushed, uniquely named journal file.
internal static class SubmissionJournalFile
	{
	private static readonly int[] Delays = [25, 50, 100, 200];
	internal static void Replace (string temporary, string destination, bool overwrite = true) => Replace (
		() => File.Move (temporary, destination, overwrite), Thread.Sleep, OperatingSystem.IsWindows ());

	internal static void Replace (Action replace, Action<int> wait, bool windows)
		{
		for (int attempt = 0; ; attempt++)
			{
			try { replace (); return; }
			catch (Exception exception) when (windows && attempt < Delays.Length && IsTransientCandidate (exception))
				{
				wait (Delays[attempt]);
				}
			}
		}

	private static bool IsTransientCandidate (Exception exception) =>
		(exception is IOException or UnauthorizedAccessException) &&
		exception.HResult is unchecked((int)0x80070005) or unchecked((int)0x80070020) or unchecked((int)0x80070021);
	}
