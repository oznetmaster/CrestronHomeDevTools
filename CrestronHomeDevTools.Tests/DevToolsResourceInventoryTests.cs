// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

[TestFixture]
public sealed class DevToolsResourceInventoryTests
	{
	private static DevToolsResource Computer (string name, DevToolsResourceState state, bool verified) => new (
		name, DevToolsResourceKind.WindowsComputer, state, state == DevToolsResourceState.Planned ? null : name + ".example.invalid",
		[DevToolsResourceRole.AndroidEmulator, DevToolsResourceRole.ConfigureProUi, DevToolsResourceRole.GitHubRunner],
		verified ? [new ("android-adb", DateTimeOffset.UtcNow, "operator-check-1")] : [], new Dictionary<string, string> ());

	[Test]
	public void PlannedOrUnverifiedMachinesCannotBeChosen ()
		{
		var inventory = new DevToolsResourceInventory (1, [Computer ("future", DevToolsResourceState.Planned, true), Computer ("unverified", DevToolsResourceState.Ready, false)]);
		Assert.Throws<InvalidOperationException> (() => inventory.Select (DevToolsResourceKind.WindowsComputer, DevToolsResourceRole.AndroidEmulator, ["android-adb"]));
		}

	[Test]
	public void MultipleCapableMachinesRequireExplicitSelection ()
		{
		var inventory = new DevToolsResourceInventory (1, [Computer ("first", DevToolsResourceState.Ready, true), Computer ("second", DevToolsResourceState.Ready, true)]);
		Assert.Throws<InvalidOperationException> (() => inventory.Select (DevToolsResourceKind.WindowsComputer, DevToolsResourceRole.AndroidEmulator, ["android-adb"]));
		Assert.That (inventory.Select (DevToolsResourceKind.WindowsComputer, DevToolsResourceRole.ConfigureProUi, ["android-adb"], "second").Name, Is.EqualTo ("second"));
		Assert.Throws<InvalidOperationException> (() => inventory.Select (DevToolsResourceKind.WindowsComputer, DevToolsResourceRole.Notifications, [], "second"));
		}

	[Test]
	public void ProcessorCannotBeConfiguredAsWindowsUiWorker ()
		{
		var invalid = Computer ("processor", DevToolsResourceState.Ready, true) with
			{
			Kind = DevToolsResourceKind.CrestronProcessor
			};
		Assert.Throws<ArgumentException> (() => new DevToolsResourceInventory (1, [invalid]).Validate ());
		}
	}