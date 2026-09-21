// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

/// <summary>Deserialize an already bounded private stdin buffer without copying its contents.</summary>
internal static class ProtectedJsonInput
	{
	internal static T? Deserialize<T> (ReadOnlySpan<char> input, JsonSerializerOptions options)
		{
		// Windows PowerShell 5.1's Process.StandardInput emits a UTF-8 BOM. Console.In
		// can expose it as a character; accept one leading marker, not arbitrary JSON changes.
		if (!input.IsEmpty && input[0] == '\uFEFF')
			input = input[1..];
		return JsonSerializer.Deserialize<T> (input, options);
		}
	}
