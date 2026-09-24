# Android on an unattended Windows worker

This source-preview setup starts an existing Google Android emulator when Windows
starts. It supports the Android stage of the public NUnit workflow. It does not
install Android, accept app agreements, select a Home, or manufacture UI evidence.

Use a dedicated test computer or an explicitly reserved emulator. Install Android
and Crestron Home, create the AVD under its intended Windows account, and complete
the app's first-use setup and connection to the intended test processor once.
Follow the NUnit [Android emulator setup](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidEmulatorSetup.md)
and [UI testing](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidUiTesting.md)
instructions. The workflow uses ADB; it does not need the operator's Windows mouse
or keyboard. A working emulator is not proof of a working Home connection.

## Automatic startup

In a built source-preview console archive the scripts are under
`scripts/automation/`; substitute that directory for `tools/` in the example.

Run administrator PowerShell 7.6 or later as the Windows account that owns the
existing AVD. Keep the reviewed launcher in a stable location. Record its SHA-256
when installing the trusted tool version, then supply that recorded value:

```powershell
./tools/InstallSubmissionAndroidFixture.ps1 -Name TestAndroid -Emulator C:/Android/Sdk/emulator/emulator.exe -Launcher C:/CI/Tools/RunAndroidFixture.ps1 -LauncherSha256 RECORDED_SHA256 -Avd TestAndroid -LogDirectory C:/CI/Private/android-status
```

The installer creates `CrestronSubmission-Android-TestAndroid`, starts it, and
refuses to replace an existing task. It uses Windows S4U under the AVD owner:
no saved Windows password and no logged-in desktop are required. S4U does not
supply Windows network-share credentials. Home's own processor authentication and
the evidence worker's saved credentials remain separate.

The default emulator console port is 5554, giving ADB serial `emulator-5554`.
For additional emulators, use a different AVD, task name, log directory and even
`-Port` for each. Do not have another startup task launch the same AVD or port.
The launcher uses two virtual CPU cores, 2 GiB of guest memory, software graphics,
no emulator window and a cold boot. Verify the hardware's capacity before running
multiple emulators or builds concurrently.

The task prevents overlapping instances and allows three process restarts, one
minute apart. It does not dismiss Android error dialogs or reset a failing AVD.
It retains current emulator stdout/stderr and one previous pair, plus
`process.json`. These diagnostic logs are private and require normal disk-space
monitoring; rotation occurs at launch, not during a long-running emulator session.

## Readiness and recovery

After setup, check the explicit ADB serial, Android boot completion, the intended
Home connection and the public Home-readiness fixture. Repeat the check after an
actual Windows restart without logging into its desktop, under the account that
will run the tests. Give a cold boot time to finish before classifying it as a
failure. A running Windows task or `sys.boot_completed=1` alone is insufficient.

Use the same private Android session-lock path for every job sharing an emulator.
Keep each job's processor identity and expected Home explicit. Do not let a retry
tap whichever Home or dialog happens to be visible. If the app requests initial
agreement, authentication, or recovery, report the missing setup and retain the
checkpoint rather than treating UI tests as passed or N/A.

For maintenance, stop the named task only while no app workflow owns the fixture.
Unregister that exact task when retiring the fixture. This does not remove the AVD
or its saved Home; deleting those is a separate deliberate operation. Temporary
test captures and device changes remain the NUnit workflow's cleanup responsibility.

## Validation limit

On 24 September 2026, a Windows 11 worker started Android 15 and Crestron Home
4.11.1 through this S4U launcher with no logged-in Windows session. Android showed
a System UI nonresponse dialog on the first tested cold boot; one observed Wait
action recovered it. Home then opened its first-use agreement screen. This is
startup evidence only. An actual Windows restart also automatically started the
fixture with no desktop login; Android reported boot complete after about 55
seconds, and Home again opened its first-use agreement screen without a crash.
App setup, reliable connected-Home readiness and the full
release-bound Android stage must be verified before relying on this worker for
unattended submissions.

Copyright (c) 2026 Neil Colvin. MIT licensed.
