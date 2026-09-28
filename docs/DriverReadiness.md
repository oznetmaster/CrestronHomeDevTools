# Check an installed driver before testing

Development, rehearsal and submission can share processors at different times. A previous pass does not establish that the currently selected processor, driver version and installed children are still ready. A platform can be online while its children require reconfiguration or a native light has no controls.

Before a development test, run the read-only command with the saved credentials/profile for the intended processor and the expected installed root identity:

```powershell
CrestronHomeDevTools driver-readiness --profile development --device ROOT_ID --model MODEL --version VERSION
```

Use the usual `--credentials`/`--processor` options instead of a profile when using credential bindings. Keep concrete hosts and credentials in private configuration. Select the appropriate profile for each processor; never substitute a previously passing result from another processor.

Exit code 0 means the inspected tree is ready; 1 means a readiness issue was found. Connection or authentication failure is also a failure to establish readiness, not a passing check. Save the JSON report with the test evidence. It contains device IDs, names and reasons, but no configuration values. `DriverReadiness.InspectAsync` also accepts a map of expected device IDs and required commands for test-specific controls; a replaced/missing child ID fails that check.

The check walks actual installed descendants, not discovery advertisements. It requires loaded/configured/ready/online status for ordinary drivers, checks expected versions, and reports configuration items flagged new or requiring review. Native light wrappers and loads use their appropriate status and dimmer-control/state checks instead of requiring extension-only configuration fields.

The production submission adapter checks the installed tree before initial and post-endurance app tests, and checks it again before starting new deployment-bound endurance collection. Endurance checks additionally verify retained managed-child IDs and planned commands. A failure retains a fresh report and prevents test startup. It does not change configuration, redeploy, restart hardware or restart an existing collection. After an explicit repair, take a new readiness observation; preserve the failed one.

This is a point-in-time preflight, not a replacement for execution reservations, exact package verification, functional tests or post-outage recovery checks. Existing external development scripts must invoke the command/API themselves. This source change does not update already-installed workers until their normal tool deployment and pin update.
