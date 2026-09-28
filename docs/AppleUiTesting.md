# Apple UI testing: scope and integration status

DevTools coordinates processor configuration and test evidence. The released
CrestronHomeNUnit UI integration controls Android through ADB. Apple UI automation
is a separate integration: the current public tools do not yet provide a Mac or
iPhone counterpart to the installed-driver Android test phase.

| Test surface | Automation route | Current validation |
| --- | --- | --- |
| Crestron Home on Android | CrestronHomeNUnit Android library and installed-driver runner | Released integration; see the [Android guide](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidUiTesting.md). |
| Crestron Home iPhone/iPad app running on an Apple Silicon Mac | Appium Mac2 with XCTest on the Mac | Separate setup experiments verified connection and Home/Rooms navigation. Not a packaged DevTools phase or physical iOS test. |
| Configure Pro on macOS | Appium Mac2 with XCTest | Separate inspection experiments; complete configuration coverage remains unvalidated. |
| Crestron Home on a physical iPhone or iPad | A separately configured iOS automation host, such as Appium XCUITest | Not yet validated by this workflow. |

The Mac validation used Crestron Home 4.12.11, macOS 27.0 and Xcode 27.0.
These are recorded test versions, not minimum-version or future compatibility
guarantees. Running the iPhone/iPad app on Apple Silicon provides useful app-level
coverage, but does not establish identical device layout, input, permissions or
lifecycle behavior on iOS. Installing an iOS simulator runtime alone does not
establish that the vendor app can be installed or tested there.

## Roles and prerequisites

NUnit supplies test discovery, assertions and results. Appium/XCTest supplies
Apple UI control. A new NUnit version does not add an Apple UI adapter, and the
Windows runner does not become a native macOS application by updating NUnit.
Use NUnit 5 for new owned fixtures, while matching the processor host and fixture
versions where processor tests are involved.

Follow the upstream [Mac2 setup instructions](https://appium.github.io/appium-mac2-driver/)
or [XCUITest documentation](https://appium.github.io/appium-xcuitest-driver/)
for the selected target. App access, automation permissions and any device trust
or signing setup must be established before scheduling unattended tests.
SSH access is useful for remote setup and job control; it is not itself UI access.

Keep app credentials and machine-specific settings private. Scope automation
sessions to a job, retain screenshots and UI state with the app/OS/device identity,
and stop owned automation processes on exit. The setup experiments verified
session cleanup, but monitor-disconnected operation and restart recovery remain
separate checks; do not claim headless readiness from an SSH connection alone.

Before Apple tests can satisfy an automated workflow stage, the public integration
must bind the selected app and processor, execute the declared cases, verify
restoration and retain authenticated results through the normal evidence path.
Do not relabel Android or Mac observations as physical iPhone evidence.
