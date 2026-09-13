// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeDevTools;

internal static class DriverVersions
	{
	// Preserve every component, including the Debug build number. Only padding is ignored.
	internal static bool Equal (string? left, string? right) => Version.TryParse (left, out var first)
		 && Version.TryParse (right, out var second) && first == second;
	}