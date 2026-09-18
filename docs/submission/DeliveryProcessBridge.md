# Guarded delivery process integration

DevTools 1.8.0 adds `SubmissionDeliveryRevalidation.CheckAsync` and `SubmissionDeliveryRevalidationSettings`. Use the matching reviewed source tag for the external Python validator and its pinned dependencies.

The bridge connects the [offline signed-handoff revalidator](DeliveryRevalidation.md) to [guarded dispatch](DeliveryJournal.md#authorization-immediately-before-each-step). It starts no uploader or mail client. The caller supplies its separately reviewed `ISubmissionDeliveryTransport`:

```csharp
await SubmissionDelivery.ExecuteAuthorizedAsync(
    privateJournalDirectory, plan, packagePath, signedFormPath, transport,
    (step, token) => SubmissionDeliveryRevalidation.CheckAsync(
        revalidationSettings, plan, step, token),
    cancellationToken: cancellationToken);
```

Each pending external step gets a new check. A resumed `Uploaded` journal checks only the pending email. An already `Submitted` journal returns its retained historical receipt without starting another revalidator or provider operation. An uncertain provider outcome still requires reconciliation; a new check cannot bypass it.

## Prepare protected tooling and settings

The trusted orchestration job constructs `SubmissionDeliveryRevalidationSettings` after reviewing its inputs. Do not accept these settings or their hashes from untrusted pull requests or from the worker that produced the evidence.

| Fields | Required content |
| --- | --- |
| `PythonPath`, `PythonSha256` | Absolute path to the reviewed Python executable and its lowercase SHA-256. |
| `DotnetPath`, `DotnetSha256` | Absolute path to the reviewed .NET executable and its lowercase SHA-256. |
| `ToolsDirectory`, `ToolFiles` | Private immutable directory containing `revalidate_delivery.py` and its sibling Python modules; a complete relative-path/SHA-256 manifest. |
| `ValidatorDirectory`, `ValidatorFiles` | Complete immutable published or built validator directory and its complete file manifest. |
| `PreparationSettingsPath`, `PreparationSettingsSha256` | Original private delivery-preparation settings and their separately reviewed hash. Its `dotnet` and `validator` paths must select the pinned runtime and validator. |
| `PreparedDirectory`, `DeliveryReviewSha256` | Original completed preparation directory and independently approved delivery-review receipt hash. Signed-review and authorization pins come from the approved delivery plan. |
| `AttemptsDirectory` | Existing protected mutable directory for these attempts, separate from the immutable tooling, prepared artifacts, runtime executable and settings paths. |
| `Timeout` | Child-process deadline, from one second to ten minutes; allow time for the real offline validator. |

Each manifest uses `SubmissionEvidenceFile` with forward-slash relative paths and lowercase SHA-256 values. It must include every file under its directory. Missing, extra, duplicate or linked files fail. Use a clean copy of the Python modules without generated caches, rather than a working directory whose contents keep changing. Bytecode writing is disabled during invocation. Resolve runtime symlinks to their reviewed real executable paths before constructing settings, and use that same .NET path in the original preparation settings.

The bridge holds the checked tool/settings files open without write/delete sharing on Windows while the child runs. The executable hashes do not cover every Python/.NET runtime dependency or installed Python package; protect and review those complete environments separately. Required Python dependencies are described in [ReviewStage.md](ReviewStage.md). Hashes are not a sandbox against a malicious administrator. Protect input directories against replacement, including on systems whose filesystem sharing semantics differ from Windows.

The bridge ignores Python environment overrides, disables user-site imports, and removes inherited `PYTHON*`, `CRESTRON_HOME_*` and `DOTNET_*` overrides from the child. It does not provide complete process isolation or grant any account permissions. Its account still needs authorized read access to the original private review and approval files. Neither the command nor this bridge requires processor or email passwords.

## Execution and retained results

The bridge takes an exclusive lock in `AttemptsDirectory`, rejects unfinished or malformed prior attempt records, then checks the complete tooling and original settings pins before starting Python. These filesystem checks precede the child-process deadline. An invocation writes a distinct `Upload-<id>` or `Send-<id>` intent, request settings and process identity privately. It passes no credentials on the command line and creates no visible window.

Stdout is limited to 1 MiB and stderr to 64 KiB. Stderr content is discarded rather than forwarded into diagnostics. Cancellation, timeout or oversized output stops the child process tree and waits for it to exit before finishing the attempt. If the operating system will not terminate the child, the bridge keeps waiting and holds its lock; investigate the recorded process rather than starting a replacement. Killing the parent leaves an unfinished attempt that blocks a later invocation.

Success requires all of the following:

- Zero process exit status and valid, nonduplicated JSON fields.
- A matching completed private revalidation receipt and stdout result.
- The exact original review/approval pins and `SubmissionDelivery.PlanDigest` for the requested plan.
- A revalidation timestamp from this invocation, with approval still unexpired.
- Matching retained plan and validation-report file hashes, and a retained plan that also matches the requested plan.
- Both `deliveryAttempted` and `submissionReady` remain false: this child has only performed offline validation.

Only then does the bridge return `SubmissionDeliveryAuthorization` to the dispatch guard, which checks expiry and cancellation again before the external intent. Failures record a terminal failed attempt after the child exits, retain private evidence, and authorize no transport call. A later check may recover from a known offline failure if the same plan is still authorized; it cannot replace an unfinished child or an uncertain delivery journal.

The attempts directory and delivery journal are separate. Do not recursively access the delivery journal inside its callback. Do not delete unfinished attempts, create a fresh journal or change approval hashes to bypass an uncertain outcome. Keep paths, forms, sender information and raw result files out of public CI summaries.

## Verified scope and remaining work

Synthetic child-process tests cover pins, output limits, cancellation, timeout, malformed/stale/conflicting results, completed-file consistency and interrupted attempts. An integration test runs the actual Python revalidator and .NET evidence validator through this bridge before each of two **simulated** transport steps. It also verifies that a repeated completed dispatch performs no more validation or sending.

These tests use synthetic forms and approvals. The 1.8.0 [protected delivery command](DeliveryCommand.md) composes this bridge with the uploader, SMTP provider and durable journal. A real protected worker still needs reviewed settings, account permissions and real signature/approval inputs. These synthetic bridge tests establish no actual delivery or Crestron acceptance. Ordinary driver/library releases remain independent of this optional submission stage.
