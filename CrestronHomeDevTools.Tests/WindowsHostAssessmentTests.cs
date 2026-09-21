// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeDevTools.Tests;

public sealed class WindowsHostAssessmentTests
	{
	private static WindowsHostSnapshot Snapshot () => new ("synthetic", DateTimeOffset.UtcNow, "Windows", ["test CPU"], 4,
		16384, 8192, @"C:\Work", 300000, true, 1, true, null, ["10.0.100"],
		[new ("sshd", "Running", "Automatic"), new ("actions.runner.example", "Running", "Automatic")]);
	[Test]
	public void LoggedInSessionDoesNotProveUnlockedUsableOrRestartResilientDesktop ()
		{
		var result = WindowsHostAssessment.Evaluate (Snapshot (), new ("UI and emulator", 4, 4096, 20000, RequiresDesktop: true, RequiresVirtualization: true));
		Assert.That (result.Missing, Is.Empty);
		Assert.That (result.DeclaredPrerequisitesSatisfied, Is.False);
		Assert.That (result.Unverified, Does.Contain ("unlocked-desktop-and-application-access").And.Contain ("desktop-after-remote-disconnect-and-restart").And.Contain ("emulator-acceleration-and-adb-rehearsal"));
		}
	[Test]
	public void CombinedBudgetUsesAvailableMemoryAndActualWorkDisk ()
		{
		var result = WindowsHostAssessment.Evaluate (Snapshot () with
			{
			AvailableMemoryMiB = 512,
			AvailableDiskMiB = 1000
			}, new ("combined", 4, 4096, 20000, 10));
		Assert.That (result.Missing, Is.EquivalentTo (new[] { "available-memory", "available-work-disk" }));
		Assert.That (result.DeclaredPrerequisitesSatisfied, Is.False);
		}
	[Test]
	public void ServicesDoNotProveRemoteAccessAndSessionZeroCannotDoDesktopWork ()
		{
		var result = WindowsHostAssessment.Evaluate (Snapshot () with
			{
			SessionId = 0,
			UserInteractive = false
			},
			new ("UI worker", 2, 1024, 1000, RequiresDesktop: true, RequiresSshService: true, RequiresGitHubRunnerService: true));
		Assert.That (result.Missing, Does.Contain ("interactive-user-session"));
		Assert.That (result.Unverified, Does.Contain ("ssh-access-from-orchestrator").And.Contain ("github-runner-registration-and-access"));
		}
	[Test]
	public void PassingPrerequisitesIsNotACompletedWorkloadRehearsal ()
		{
		var result = WindowsHostAssessment.Evaluate (Snapshot (), new ("build", 2, 2048, 10000, 10));
		Assert.That (result.DeclaredPrerequisitesSatisfied, Is.True);
		Assert.That (result.WorkloadRehearsalRequired, Is.True);
		}
	[Test]
	[Platform ("Win")]
	public async Task LocalReadOnlyProbeReturnsThisMachineAndDoesNotClaimDesktopReadiness ()
		{
		var snapshot = await WindowsHostAssessment.ReadLocalAsync (TestContext.CurrentContext.WorkDirectory);
		Assert.That (snapshot.MachineName, Is.EqualTo (Environment.MachineName).IgnoreCase);
		Assert.That (snapshot.LogicalProcessors, Is.Positive);
		Assert.That (snapshot.TotalMemoryMiB, Is.Positive);
		Assert.That (snapshot.AvailableDiskMiB, Is.Positive);
		Assert.That (snapshot.DesktopUsable, Is.Null);
		}
	}