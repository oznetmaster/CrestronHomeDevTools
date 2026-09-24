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

After completing app setup, add
`-ApplicationActivity com.crestron.phoenix.app/.host.MainActivity` to installation
to open that installed app after Android reports boot complete. This is an app
launch only: it does not enter passwords, select a Home or dismiss dialogs. The
launcher allows up to three minutes for Android boot and records app launch
separately from Home readiness. The test fixtures must still verify the saved
endpoint, expected Home and required device feedback.
If automatic app launch fails, the launcher retains the private ADB startup
diagnostic and stops its own emulator process tree. It does not leave a QEMU
child running behind a failed Windows task or retry app controls.

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
The installer also accepts `-Cores` and `-MemoryMb` to match an assessed worker's
capacity; the defaults are a starting configuration, not a performance guarantee.
ADB defaults to `platform-tools/adb.exe` in the same Android SDK as the emulator;
use `-Adb` if it is installed elsewhere. The launcher starts the shared ADB server
before redirecting emulator logs, so the server does not inherit handles to those
files and block log rotation after an emulator-only restart.
Log rotation also allows a bounded ten-second retry while closing child-process
handles are released. Only the file rename is retried; the app launch is not.

The task prevents overlapping instances and allows three process restarts, one
minute apart. It does not dismiss Android error dialogs or reset a failing AVD.
It retains current emulator stdout/stderr and one previous pair, plus
`process.json`. These diagnostic logs are private and require normal disk-space
monitoring; rotation occurs at launch, not during a long-running emulator session.
Launcher failures retain a small `launcher-error.json` with the failing stage,
error type and timestamp. Compare its timestamp with the current `process.json`;
an older failure remains historical evidence. Startup does not automatically kill
a shared ADB server. When upgrading an earlier launcher that already left ADB
holding its log open, first establish that every emulator and ADB job on that
computer is stopped, then restart ADB during that maintenance window.

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

Subsequent setup reused the saved Home UI password and displayed populated Home
tiles. The public NUnit `AndroidDevice` capture and `CrestronHomePages` assertions
also passed under the actual LocalService identity, including a populated weather
tile and release of the Android reservation. The public assembly had to be placed
in a service-readable tools location; the earlier interactive-only rehearsal folder
was not readable by that account. Provision tools separately from private credentials
rather than widening permissions on an old working directory.

This service capture is an access/readiness check, not a completed release-bound
fixture run or evidence that every UI behavior passed. Allow bounded startup time:
the initial saved-Home card briefly said offline and the Home heading appeared
before its data. A heading alone does not establish a connected, populated app.
An app-only restart subsequently produced a Crestron Home nonresponse dialog.
That failed recovery remains part of the validation record: the worker must not
silently dismiss repeated dialogs or turn a setup capture into an unattended-test
pass. Rendering-capacity and restart recovery checks remain in progress.

Copyright (c) 2026 Neil Colvin. MIT licensed.
