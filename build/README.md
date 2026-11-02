# Build and test

This repository uses [the shared Cake build component](https://github.com/oznetmaster/BuildAutomation/tree/ae6f311e530a18dc705971dcc655fc97de03e368) pinned in `component.json`. It is build tooling only; product dependencies and versions are unchanged.

The CI workflow retains its jobs, framework matrix, filters, prerequisites and artifact upload rules. Repository-owned recipes preserve the build/test/package commands. Publication, validation reuse, approvals, runner selection and hardware reservations remain in GitHub Actions.

Run the same task locally with .NET 10, Git and PowerShell:

```powershell
./build/Invoke.ps1 -Task TASK_NAME
```

For a matrix task, set its `BUILD_INPUT_*` environment values as shown in the workflow before invoking it. These are project/framework selections, not credentials. The first local invocation downloads the immutable component into ignored `artifacts/build-components/`.

Available tasks:

- `ci-validate-verify-powershell-prerequisite`
- `ci-validate-verify-complete-discovered-test-coverage`
- `ci-validate-validate-windows-scheduled-endurance-worker`
- `ci-validate-validate-kasa-tapo-read-only-endurance-sample`
- `ci-validate-validate-new-monitoring-directory-permissions`
- `ci-validate-validate-scheduled-endurance-notifications`
- `ci-validate-validate-completed-endurance-snapshots`
- `ci-validate-validate-passive-endurance-health-snapshots`
- `ci-validate-validate-shared-nunit-packages-from-a-fresh-cache`
- `help-tests-offline-install-document-dependencies`
- `help-tests-offline-build-offline-submission-validator`
- `help-tests-offline-run-all-discovered-offline-document-tests`
- `help-tests-offline-validate-public-submission-workflow-templates`
- `help-tests-offline-build-and-test-the-windows-console-download`

The shared component rejects missing/stale/empty/failed declared TRX results and writes one receipt per invocation beneath `artifacts/build-automation/`. It does not publish packages or invoke live hardware tests. Existing specialized scripts remain authoritative.
