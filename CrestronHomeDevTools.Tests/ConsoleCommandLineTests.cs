// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class ConsoleCommandLineTests
	{
	[Test]
	public void InteractiveCommandPreservesQuotedSystemNameAndWindowsPath ()
		 => Assert.That (ConsoleCommandLine.Split ("drivers --processor \"Development Home\" --settings \"C:\\My Settings\\private.json\""),
			  Is.EqualTo (new[] { "drivers", "--processor", "Development Home", "--settings", @"C:\My Settings\private.json" }));
	[Test]
	public void UnterminatedQuoteIsRejectedBeforeExecutingCommand ()
		 => Assert.Throws<ArgumentException> (() => ConsoleCommandLine.Split ("drivers --processor \"unfinished"));
	}