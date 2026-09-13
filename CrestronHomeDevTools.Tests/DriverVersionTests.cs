// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DriverVersionTests
	{
	[TestCase ("1.003.0003.26", "1.3.003.0026", true)]
	[TestCase ("2.0.000.0005", "2.0.0.5", true)]
	[TestCase ("2.0.000.0005", "2.0.000.0006", false)]
	[TestCase ("2.0.0", "2.0.0.5", false)]
	[TestCase ("unknown", "unknown", false)]
	[TestCase (null, null, false)]
	public void ComparesNumericComponentsWithoutLosingTheDebugBuild (string? first, string? second, bool expected)
		 => Assert.That (DriverVersions.Equal (first, second), Is.EqualTo (expected));
	}