# Submit a driver: the normal path

This is the entry point for a developer or an assistant using the public workflow. Use the current stable DevTools release (1.16.1 or later for the first-page driver identification). Follow the linked settings schemas; capitalized command arguments are placeholders to replace.

Submission is optional and applies only to actual Crestron drivers. Library, client and processor-test releases do not use it. Passing these checks means complete against our interpretation of Crestron's requirements, not accepted or certified by Crestron. The protected stages have tooling acceptance tests, and the published console has completed real authorized upload and signed-form email delivery. See [validation status](ValidationStatus.md) for the distinction between this local run, CI rehearsals and the consuming developer's environment validation.

## Set up once

1. Extract the entire Windows console ZIP from the [release](https://github.com/oznetmaster/CrestronHomeDevTools/releases). Keep its files together. Run the commands below from that directory.
2. Follow [workflow setup](WorkflowSetup.md) to copy the five templates into your own trusted private orchestration repository, pin an audited tooling commit, and configure the protected worker, environments and private settings. One Windows PC and one processor are sufficient; see [development configurations](DevelopmentConfigurations.md).
3. Configure the driver-specific verification plan, C# fixtures, actual devices and Android environment where required. Review the complete applicable official requirements. The delivery templates do not create tests or supply missing evidence.
4. Keep private settings, raw evidence, credentials and the signature outside public source and release assets. Use [named encrypted inputs](../PrivateInputs.md) when reusing saved credentials and signatures; the [workflow setup](WorkflowSetup.md) describes the optional binding variable for signing and delivery. Configure access for the account that actually runs the worker. Leave delivery disabled until provider settings and exact authorization are ready.

```powershell
.\CrestronHomeDevTools.Console.exe submission runtime-check
.\CrestronHomeDevTools.Console.exe submission --help
```

A successful runtime check validates this console installation, not the processor or driver. No Python or Linux knowledge is required. Help rendering has separate renderer/font prerequisites documented in [help generation](HelpBuild.md).

## Prepare each candidate

Freeze the actual Release package and its source commit. Retain the candidate declaration, reviewed policy, official form inventory and mapping. Run the applicable automated and manual checks against that candidate, including endurance and its final functional checks. Preserve failed attempts and original results. Changing the candidate requires reassessing which evidence remains valid.

Use [evidence mapping](EvidenceMapping.md) and, when phases produce separate observation documents, [evidence composition](EvidenceComposition.md). Neither tool infers passing requirements from test names or authenticates a producer. Review coverage and evidence provenance before preparing the form.

If the candidate changed after testing, [review prior evidence explicitly](PriorEvidence.md) for individual unchanged assertions. Retain original results and assess affected dependencies; do not relabel an earlier run as a fresh execution. This is optional and does not determine whether Crestron will accept that evidence.

Record exact input hashes and completed stage receipts in a private handoff record. This allows another person or assistant to resume without a conversation history. A candidate declaration hash identifies the JSON declaration; it is **not** the package hash.

## Run the four protected stages

Open **Actions → Crestron submission → Run workflow** in your orchestration repository. Select its configured branch and enable the requested stage. Run only the next stage after checking the preceding result. Repository enablement and environment approvals also apply; a skipped green job is not completion.

| Stage | Inputs to supply | Result to inspect before continuing |
|---|---|---|
| `review` | Full source commit; candidate declaration, official inventory and mapping hashes; independent Android pins hash when applicable. | Unsigned signing-copy PDF, evidence bundle, reports, `review-receipt.json` and `COMPLETE`. Review every page, full coverage and producer provenance. [Review details](ReviewStage.md). |
| `sign` | Unsigned review receipt hash and separate exact signing-authorization file hash. | Signed PDF, signing report and signed review receipt. Verify all rendered pages and the intended signature. [Signing details](SigningStage.md). |
| `delivery-plan` | Signed review receipt hash and separate exact delivery-authorization file hash. | Frozen outgoing package/PDF, delivery plan, receipt and `COMPLETE`. Check sender, recipient, correspondence and expiry. Nothing has been sent. [Preparation details](DeliveryPreparation.md). |
| `deliver` | Uses protected dispatch settings and their independently retained hash. | Durable upload/email receipts and journal state. Confirm both provider outcomes. Delivery is not Crestron's acceptance decision. [Delivery details](DeliveryCommand.md). |

Before `deliver`, generate and review the private dispatch settings using [delivery setup](DeliverySetup.md), configure the environment's settings path/hash, and supply provider credentials through the protected mechanism. Do not put credentials in workflow inputs. Signing approval does not authorize sending, and an expired approval must not be silently extended.

You can also run each underlying public console command locally using its linked settings and command guide. Preparation commands do not send anything. Do not use local dispatch to bypass the configured approvals or journal.

The table describes complete mode. For a signed submission with disclosed gaps, select `review_mode: declared-gaps` and follow the [declared-gap CI sequence](WorkflowSetup.md#signed-submissions-with-declared-gaps). It retains the review/sign/delivery-plan/deliver stages, but uses different delivery-plan settings and verifies an explicitly approved review plan. The final delivery command still revalidates all evidence. Do not feed a declared-gap review into the complete-only preparation command or silently switch modes after a failure.

## Resume after an interruption

| Last observed state | Next action |
|---|---|
| Endurance collector still running | Observe that existing run using [endurance monitoring](EnduranceNotifications.md). Do not restart it merely because observation timed out. |
| Preparation failed, or output has no valid `COMPLETE` receipt | Keep the failure for diagnosis. Correct the input and prepare a new output directory; do not consume partial output. |
| Completed review or signing stage | Verify its retained receipt, files and exact approval, then continue with the next stage. Do not rebuild the candidate to resume. |
| Approval expired or approved bytes changed | Obtain approval for the current exact artifacts before continuing. |
| Upload/email outcome is uncertain | Inspect and reconcile the existing [delivery journal](DeliveryJournal.md). Never start a new journal or resend blindly. |
| Provider delivery completed | Retain receipts and wait for Crestron's actual response. Record that response separately. |

If a required test or document cannot be supplied, the normal path stops. A developer can deliberately choose the separate [declared-gaps route](DeclaredGaps.md), with each unmet requirement and reason disclosed. Do not substitute that route without their decision, mark missing tests as passed, or assume Crestron will accept the request.

For a useful handoff, record the selected driver and candidate identity, tooling version/commit, private settings locations, last completed stage and receipt hashes, any active run handle, unresolved failures, and the exact next action. Keep secrets out of that summary. No step should depend on remembering a prior chat.
