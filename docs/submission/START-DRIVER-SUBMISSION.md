# Execute a Crestron driver submission

Give this document and the driver's repository URL to the assistant that will perform the work. No supervising assistant, previous conversation, private helper repository or author-specific environment is required by these instructions. The assistant must obtain missing developer inputs itself and consult the linked public documentation itself.

**Validation status:** this starting document is a draft. The underlying tools have validation and real delivery evidence described in the public validation guide, but an independent run beginning with this document has not yet demonstrated the entire procedure. Do not describe that independent validation as complete.

The [release automation controller](ReleaseAutomation.md) is a source preview. Its [Windows worker](AutomationWorker.md) connects release discovery, tests, endurance, documents, authorized signing/delivery and retention. A configured rehearsal has now completed actual deployment, Windows/processor/live/app tests, temporary-test cleanup, shortened endurance, export and unsigned review preparation without intervention after startup. Signing/delivery/retention use separate synthetic-authority and test-provider validation. This is not an independent operator validation of this document or a newly published release followed by real submission. See [the precise validation boundaries](ValidationStatus.md). Intake alone does not start tests; the installed watcher advances registered runs. The [setup form](SetupApp.md) can reduce repeated questions, but check availability in the selected published version before prescribing it to another developer.

In this source preview, saved setup offers **Prepare rehearsal profile** and
**Prepare submission profile**. Select the intended mode before intake. Actual
submission preparation requires a Submission-purpose snapshot, explicit protected
stage bindings and the separately installed protected worker. It does not approve
signing or delivery. Do not turn a completed rehearsal into a submission by editing
its frozen settings.

**How to start:** attach this document and supply the driver repository URL. An existing private submission-state file is optional for resuming an attempt. No other prepared plan is required from the developer. The assistant must read the public references below itself; this is one starting document, not a requirement to fit every API schema into one file.

## Assignment

You are the operator. Starting with the supplied driver repository, prepare, build, test and submit the actual driver using released public tools and their public documentation. Continue through the next executable step without waiting for a supervisor to tell you what to do. Ask the developer directly for missing product facts, hardware actions or required authorization. Do not assume they know the workflow, Python, Linux or any programming language other than C#.

Use the developer's stated delivery route. If the request is for GitHub CI, implement and exercise that route through its final submission stages; a successful local console run alone does not satisfy that request. Otherwise the documented direct Windows console route is sufficient, and GitHub orchestration is optional. Normal driver releases must remain possible without available test hardware or a submission. Libraries, clients and processor-test packages are never submission candidates.

The outcome is a correctly identified package, evidence, help, reviewed and authorized signed documents, a verified uploaded download URL and authorized correspondence delivered to Crestron, with retained receipts. Submission delivery is not acceptance, publication or certification. "Complete" means complete against our documented interpretation of the applicable Crestron requirements. Never predict Crestron's decision.

### Release-to-submission automation acceptance criterion

For the author's configured drivers, the eventual operational target is: **publish a GitHub release, then reach a delivered Crestron submission with virtually no intervening human work**. A local console success, saved-input app, or manually dispatched sequence of GitHub stages is useful progress but does not by itself meet this target.

One-time setup selects the private submission profile, protected worker, equipment, test restrictions and credential access. Each opted-in release must automatically hand off its exact identity and assets to private orchestration, select and freeze the applicable saved inputs, run the required tests and endurance collection, prepare evidence and documents, and advance through authorized signing, upload, verified download and email. Keep driver-specific submission workflow entities private. Do not make a human move files, re-enter saved facts, choose each routine next stage, or remind an assistant to continue after endurance completes. Missing hardware may defer submission without preventing an ordinary product release.

Consolidate genuinely required final artifact review and signing/delivery approvals into a clear review experience where supported by the existing authorization contracts. Do not treat a saved signature or this automation objective as permission to bypass exact-artifact authorization. Escalate actual failures, missing inputs, changed scope and unavoidable physical actions; resume from retained state without duplicating operations. Public documentation and tooling must make this executable by a less capable assistant without a supervising model. This criterion remains unvalidated until the GitHub release-triggered route has actually been exercised end to end.

## Public sources and version selection

Read the [normal-path runbook](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/Runbook.md) and [validation status](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/ValidationStatus.md). Follow their public links as each phase requires; do not ask the developer to assemble or explain those documents for you.

Obtain the current stable [DevTools Windows release](https://github.com/oznetmaster/CrestronHomeDevTools/releases). Verify published artifact hashes where supplied, extract the complete bundle, and retain its exact version and corresponding documentation commit. Run its documented `submission runtime-check`. Pin a working version for the attempt. Update only for an identified need, recording the reason and affected verification. Never silently switch a running collector to different tool binaries.

Use public [CrestronHomeNUnit](https://github.com/oznetmaster/CrestronHomeNUnit) APIs and published Android setup/test documentation for C# fixtures. Inspect the driver's actual build and test configuration instead of assuming target frameworks, compiler versions, UI routes, device capabilities or test counts.

Use the official Crestron submission instructions and template links referenced by the public tools. Confirm the applicable current requirements and template identity. If the public inventory does not match the official form, report the mismatch rather than filling a different form or inventing field mappings.

Read the repository's applicable development instructions. Keep driver-specific submission settings, evidence, signatures, correspondence and progress in a private workspace; do not add them to the public driver repository or release assets. Ordinary product help, licensing and release notes belong with the product. Generic submission instructions belong with the shared tools.

### Optional saved input profiles

If the developer provides a DevTools setup-store path and a run or snapshot name, use the setup app's public APIs and `submission-setup check` before requesting factual information. The source-preview guide is `docs/submission/SetupApp.md` in DevTools; verify that the selected released version actually includes this feature before using it. Reuse the stored developer identity, support contacts, driver facts, hardware restrictions and named credential bindings. Ask only for missing or changed facts. Frozen snapshots preserve prior input revisions; they do not authorize signing/delivery or establish a test result. Do not print decrypted profiles or credentials into logs.

For a snapshot, `submission-setup prepare --snapshot NAME --store DIRECTORY` creates private help/release-note drafts and operation defaults. Use the returned encrypted `CredentialsPath` directly with existing `--credentials` commands. Review and complete help content, actual test-environment details and UI figures before final generation. Form identity, sender and endpoint defaults come from the saved profile; artifact hashes, evidence, approval and provider receipts must come from the actual workflow. No password or signature image needs to be exported.

### Your first session

1. Open the supplied repository, inspect its instructions and working-tree state, and identify the actual driver project, build/release workflows and test projects. If the repository contains several drivers, ask which driver is intended before deploying anything. Preserve existing work.
2. Look for an explicitly supplied private submission-state record. If present, verify its candidate and live operation handles and resume from its next step. Do not begin a duplicate submission or endurance run.
3. Select the public tools and matching documentation. Inspect the available Windows environment and existing authorized profiles. Produce a short list of missing inputs; ask only for facts you cannot establish from source, documentation or existing configuration.
4. Create the private state record and initial coverage plan. Explain the intended hardware changes and obtain any missing scope authorization together, before running them.
5. Proceed with source inspection, local build/tests and help preparation while waiting for genuinely independent inputs. Do not finish the task merely by presenting a plan.

Do the work in the executing task. Do not assume another model will interpret failures, choose the next command or approve engineering decisions. Self-review against the documented criteria is part of your job; the developer reviews the declarations and external actions that require their authority. If a tool requires a distinct human approval, follow that contract rather than approving on their behalf.

### Public guide and command map

Read each linked guide before first using that stage. Use the pinned release's examples and schemas for full arguments. Command names below are discovery aids, not commands to run without inputs. Do not invent a CLI command from a C# method name.

| Phase | Public entry point | What must be retained before advancing |
| --- | --- | --- |
| Installation and preparation commands | [Console tools](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/ConsoleTools.md); `submission runtime-check` | Working complete console bundle and version/hash record. |
| Coverage | [Coverage planning](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/CoveragePlanning.md); `submission coverage-plan` | Reviewed blueprint, policy, form mapping and execution contract; generation is not a test pass. |
| Build and processor tests | [Processor test workflow](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/ProcessorTestWorkflow.md) | Actual release package/source identities, test results, installed-candidate verification and cleanup evidence. |
| Android setup and tests | [Emulator setup](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidEmulatorSetup.md), [UI testing](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidUiTesting.md), [evidence audit](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/AndroidEvidence.md); `submission audit-android` | Pre-execution producer pins, selected tests, raw captures/results and actual restoration outcome. |
| Help and dependency notices | [Help](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/HelpBuild.md), [notices](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/DependencyNotices.md); `submission build-help`, `submission render-help`, `submission dependency-notices` | Reviewed help and notices verified inside the exact production package. |
| Endurance | [Collection](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/EnduranceCollection.md), [Windows worker](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/WindowsEnduranceWorker.md), [observation](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/EnduranceNotifications.md) | Bound probe/worker/schedule, real terminal result, released ownership and verified export. |
| Review | [Review stage](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/ReviewStage.md), [forms](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/FormGeneration.md) | Full criterion disposition and the actual unsigned review artifacts. |
| Signing | [Signing stage](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/SigningStage.md) | Exact signing authorization, inspected signed files and signed-review receipt. |
| Delivery preparation | [Preparation](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/DeliveryPreparation.md), or [declared-gap approval](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/ReviewApproval.md) | Approved outgoing packet/correspondence and correctly pinned dispatch settings. |
| Actual delivery | [Delivery command](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/DeliveryCommand.md) and [journal](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/DeliveryJournal.md) | Verified download URL, confirmed upload/mail results and retained journal. |

These phases have different validation boundaries. Installing tools, generating a policy, building a package, passing fixtures, preparing a form and sending a submission are distinct outcomes; do not treat one as evidence that the others completed.

## 1. Establish inputs and authority

Inspect available local tools and supplied settings first. Ask one concise, grouped set of questions for facts that remain missing. Do not ask for passwords or signature images to be pasted into public files or logs. Follow [private input setup](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/PrivateInputs.md).

Record:

- Driver repository and intended branch/release, developer identity, approved public support website or email, and whether Crestron has already accepted this driver. A GitHub release does not establish portal acceptance.
- Available Windows machines, processors, actual devices and Android app environment; which are shared with normal home/office use; permitted tests, temporary changes, interruption methods and restoration requirements.
- Existing private credential/profile locations and the execution account that can read them. Use a secure interactive setup procedure when inputs are absent. A service account cannot be assumed to decrypt another user's saved inputs.
- Intended release and submission permissions, sender account and delivery provider, signature availability, and who authorizes final declarations and delivery.

Keep the development and submission resource assignments explicit. Verify the
selected processor, emulator Home, credential endpoint and actual worker account
agree before a rehearsal. A working development setup does not authorize moving
submission tests to that processor. Record the approved service identity with
the saved setup so later stages reuse that decision rather than asking again.

Use [development configurations](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/DevelopmentConfigurations.md). One Windows PC and one suitable processor are enough; extra PCs, runners and processors are optional. Assess prerequisites before installation. Reuse authorized setup rather than reprovisioning it merely because a candidate version changes. An emulator needs the Crestron Home app installed and connected to the intended processor, not just a working emulator process.

For new runs using current tools, follow their [PowerShell prerequisite](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/PowerShell.md), including PowerShell 7.6 or later where required. Keep existing active collectors on their recorded shell and scripts. Check the selected processor supports the required DevTools commands; processor generation is separate from driver SDK/version. Use the published Android emulator setup as the normal fixture, rather than assuming an old alternative emulator setup remains validated. Install the build toolchain required by the actual projects; a full Visual Studio installation is not automatically a prerequisite.

Before hardware tests, agree on a concrete scope of operations, targets and restoration. Reuse that authorization throughout its scope. Device names alone do not authorize disruptive operations; a naming rule must be explicitly configured by the developer. Preserve household settings and unrelated installed devices. Respect shared resource reservations across jobs and machines, and processor connection limits. Obtain separate permission for physical interruption or other operations outside the agreed scope.

For GitHub execution, follow [workflow setup](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/WorkflowSetup.md). Configure the consuming developer's protected stages using public templates. Protect credentials and signing material from ordinary build jobs. Do not depend on the tools author's private CI repository. Distinguish a skipped stage from an executed, successful stage.

## 2. Make the work resumable before running tests

Create a private `SUBMISSION-STATE.md` and an evidence directory. Keep these current at every completed phase and before waiting. Record no secret values.

The state document must contain the repository/source commit, release and package digest, tooling versions, documentation commit, official template/inventory identities, candidate and policy identities, private input locations and execution accounts, authorized scope, completed receipts with hashes, unresolved failures, and the exact next executable action.

For an active operation, also record its real job/task/run handle, machine, plan identity, resource owner, latest verified state, expected duration, output locations and the documented observation/recovery command. Retain failed attempts separately. Another assistant must be able to resume by reading this record and verifying current state, without this conversation.

Track phases as not started, running, waiting for a named dependency, failed, or verified complete. Planning a step is not completing it. Do not repeatedly run setup or tests that already have sufficient retained evidence.

Use this minimal structure in the private state file, filling in real values rather than leaving a second planning document for someone else:

```text
Driver repository / project / source commit:
Execution route and workflow run IDs, if applicable:
Private workspace and input-profile locations (no secret values):
Tool versions / documentation commit:
Candidate package path / SHA-256 / version:
Candidate declaration / policy / inventory / mapping identities:
Machines, execution accounts, processor and device bindings:
Authorized operations and restoration obligations:
Completed phases: result, receipt path and SHA-256:
Active operation: host, handle, plan, resource owner, last observation:
Observe/recover command and expected completion window:
Unresolved errors and retained original error files:
Outstanding developer question: exact text, review links, authorization scope:
Automatic follow-up: actual mechanism, or explicitly none:
Next executable action and prerequisite:
Last updated UTC:
```

At each resume, verify what is current before trusting this record. Keep immutable receipts and prior state snapshots; a summary is an index to evidence, not a replacement for it.

## 3. Prepare the driver and requirement coverage

Inspect the source, supported device models, public APIs, UI definitions, configuration fields, dependencies, licensing, build hooks and released packages. Build and run appropriate existing tests. Derive expected behavior from the product and actual devices, not assumptions about similarly named products.

Resolve dependencies for the actual target frameworks and check deployed compatibility, licensing and known material issues. Do not turn submission preparation into an unrelated dependency migration or upgrade every package merely because a newer version exists. Keep product fixes and infrastructure changes out of each other's release notes.

Before freezing the package, verify that both the bundled help and the release notes contain the developer's approved support contact. A general repository link is not necessarily a contact channel; label the support page and repository/documentation link separately when both are supplied. Review these in the actual release assets, not just an input profile or submission form.

For PDF rendering, inspect existing authorized tools and previously extracted vendor dependencies before requesting installation. Follow the public HelpBuild guide's task-local LibreOffice extraction route where appropriate; absence from PATH or Program Files is not proof that no renderer is available. Use the public render-help command with the executable's explicit path, retain its version and inspect the resulting PDF.
Inspect every applicable official checklist item. Create a coverage table with the exact item identifier/text, applicability, planned method, required observation, candidate binding and eventual evidence file. Distinguish API behavior, actual configuration UI, app presentation and physical-device behavior. An API result does not by itself prove what an app displayed. Retain the applicability justification for N/A in the evidence; wholly non-applicable checklist items need no numbered endnote. Do not infer all checkboxes passed from an aggregate test result.

Use the public coverage/evidence guides linked by the runbook. Write necessary driver-specific fixtures in C# against released APIs. Reuse repository fixtures where appropriate. The developer must not need to author or maintain Python workflow code.

Store the submission blueprint and generated settings privately even where a generic example uses a `DRIVER/submission/` path. Use supported explicit input paths and recorded source bindings; do not alter the tool's schema or omit source verification. Preserve any required approval boundary around the reviewed policy. A fixed expected test count is not a substitute for discovering and checking the intended test identities and exclusions.

Prepare the required product help and third-party notices with [help generation](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/HelpBuild.md). Include accurate installation, configuration, UI, limitations, tested environment and support information. Obtain unknown product facts from the developer. Generate, render and inspect the help before embedding it through the documented build hooks.

Build a production Release package from a recorded source commit using the repository's actual toolchain and supported packaging tools. Verify filename rules, metadata, identity, included help and notices. Keep the processor-test package distinct. A required product fix requires a tested product change and new candidate; never silently edit an already tested package.

Freeze the actual candidate package, declaration, source and policy hashes before candidate-bound testing. Declaration hashes and package hashes are different identities. Record the public release/workflow provenance where applicable.

## 4. Run functional, configuration and app tests

For unattended release deployment, configure the controller's actual-driver release
route rather than treating an already-installed candidate as deployment evidence.
Use its [deployment-to-app and endurance bindings](AutomationWorker.md#deployment-followed-by-app-tests)
to carry the verified new instance into later stages without a manual device-ID edit.
The currently documented hardware rehearsal of an installed candidate does not prove
this fresh-deployment route; retain that validation distinction.

Run the coverage plan through released tools. Acquire shared resources before use. Verify the actual installed package and target identity before changing settings. Record raw results, screenshots/hierarchy evidence when appropriate, timestamps, restoration and cleanup outcomes, including failures.

Bind each UI observation to the correct Home or Room tile and actual page. Inspect repeated controls within their labelled rows. Give navigation and execution their own appropriate deadlines. Ensure failures still run restoration and record its observed result. Do not report failed restoration merely because the main assertion threw, or claim restoration without observing it.

Test settings and feedback using reversible changes and verified restoration. Where permitted by the checklist, issue a state change through a public control API while observing the app on the page being assessed; distinguish that from testing an actual app button press. Observe both offline and recovery behavior for each applicable presentation. Capture the actual event and timestamps required by the criterion; disclose any timing interpretation rather than quietly moving the starting point.

Coordinate physical/network interruptions directly with the developer only after recording is ready. Verify the intended connectivity change actually happened. A router setting named Block is not proof that local traffic stopped. Do not equate a network outage with physical power loss. Retain the actual scope and limitations.

Do not demand identical values from independently sampled changing telemetry. Define justified synchronization/tolerance or validity criteria before execution, retain source timestamps and raw values, and distinguish a test defect from a driver defect. Never change criteria retroactively to turn an existing failed sample into a pass.

Clean up temporary test instances and CI-imported test packages through the public workflow after successful use, respecting existing ownership and explicit manual deployments. Retain diagnostics when an operation fails. Verify cleanup; do not delete unrelated catalog items or installed devices.

## 5. Run and finish endurance

Use the public endurance setup and [monitoring documentation](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/EnduranceNotifications.md). Configure the selected Windows monitor, execution account, resource ownership, startup recovery, collection schedule, retention and operational notifications. Exercise the probe and its failure/cleanup behavior before starting the credited run. Use documented public permission/setup helpers instead of inventing a private remote-execution framework.

Verify credentials and required file access under the account that will actually collect unattended. Bind the worker and probe to the frozen candidate and reviewed policy. Record the first credited sample, required duration, maximum gap and schedule handle. A successful task launch is not a passing endurance run.

For the released scheduler contract, deliberately invoke `endurance-start` once for the reviewed run and schedule only `endurance-tick`. Use the published Windows worker scripts to configure repeating/startup triggers, non-overlap, persistent storage, machine power settings and service-account access. Test restart recovery before credited collection where required by the setup guide; do not reboot a running candidate merely to validate an untested setup assumption. Use `endurance-observe` for passive scheduled-run health; opening the collector journal with concurrent status operations can interfere with collection.

A functional probe must check the candidate and justified driver-specific behavior against an independent observation. A reachable port or an online flag alone is not functional endurance evidence. Keep producer binaries immutable and fully pinned, and keep mutable credentials outside that inventory. Test its JSON input/output and failure diagnostics before deploying it; stdout must contain the documented result, not incidental console logging.

While it runs, do independent preparation without mutating the reserved processor or candidate. Prefer the Windows collector and operational failure/completion notifications over frequent AI wake-ups. Do not emit routine healthy reports. Record whether automatic assistant follow-up exists; never promise automatic export if no such continuation is configured.

On resumption, observe the existing run through the public command. A timeout, missing chat history or expired assistant session does not justify restarting it. On failure, inspect the retained original probe error and journal as well as health flags. Preserve all segments; a replacement run must not erase the original failure or create a claim of uninterrupted evidence across segments.

After terminal completion, verify the actual result and resource release, export using the public workflow, check retained files and identities, and map the evidence to the original candidate/policy. Do not retire the collector or discard source records before the export is verified. If candidate bytes changed, follow [prior-evidence review](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/PriorEvidence.md) for individually eligible assertions, and run required fresh checks. Do not automatically restart 24 hours for every documentation edit, or automatically exempt runtime changes.

## 6. Reconcile and review the submission

Use public [evidence mapping](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/EvidenceMapping.md) and [composition](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/EvidenceComposition.md). Independently check that every claimed criterion has relevant evidence. Mapping files and green validators cannot make a screenshot or test prove something it did not observe.

Every official checklist item must have an explicit disposition: evidenced pass, justified N/A, or disclosed unmet/qualified criterion. Show the developer the reason for each unmet item. Do not keep repeating completed tests to fill a paperwork gap; first locate the existing evidence. If a fresh observation really is needed, identify it precisely.

If full coverage cannot be supplied, ask the developer whether to resolve the gap or use the supported [declared-gaps route](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/DeclaredGaps.md). That is their choice, not an automatic fallback after validation failure. Follow the matching review/sign/delivery path consistently.

Generate the required forms identifying the driver, version and developer. Put numbered, correctly linked checklist notes after the checklist in the same document. Render and inspect every page. Provide the actual review files through working links before requesting approval. Verify required submission declarations and help as well as the test checklist; do not assume one signature authorizes every document.

## 7. Sign, upload and deliver

Execute the runbook's review, sign, delivery-plan and deliver stages through the selected public route. Retain each stage receipt and required hashes. Use the documented schema for the chosen complete or declared-gap mode; do not invent settings based on command names.

Use matching review-preparation and signing stages from the outset so the delivery coordinator has the receipt chain it expects. A PDF signed by a standalone signing API is not automatically a signed-review directory. Do not apply the signature again just to manufacture that directory; use a documented public route compatible with the existing authorized artifact, or report the integration gap. See [review delivery](ReviewDelivery.md) for exact correspondence and signed-file handling. When using an approved correspondence override, preserve its approved wording and substitute only the verified package URL.

Ask the developer directly to authorize the exact final document(s) and signature application. Name and link the files, versions and identifying digests, and summarize material omissions/qualifications. Do not apply the signature to revised bytes under an earlier approval. Signing approval alone is not sending approval.

Prepare the exact outgoing package, signed forms, recipients and email text. Obtain the applicable explicit upload and email authorization. Follow [delivery setup](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/DeliverySetup.md) and the [delivery command](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/DeliveryCommand.md). Upload first, retain and verify the returned download URL, then include that URL in the authorized email. Do not send a placeholder link or confuse a successful rehearsal with real delivery.

Avoid redundant approval prompts: one concrete request can cover upload and subsequent email if it identifies both actions, their recipients/artifacts and insertion of the verified returned URL, and the selected tool contracts support that authorization. Respect separate exact signature and delivery approvals. Additional prompts are needed for materially changed scope or artifacts, not simply because another phase or conversation resumed.

If a provider outcome is uncertain, reconcile the existing [delivery journal](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/DeliveryJournal.md) before retrying. Never create a fresh journal to bypass uncertain prior delivery. Verify and retain both upload and mail outcomes. Report what was actually delivered, any disclosed gaps, and that Crestron's response remains pending.

## When something goes wrong

- Diagnose environment/input errors using documented prerequisites and retained errors. Continue independent work while a specific input is pending. Do not ask for the same authorization again merely because context was lost.
- If a public capability or documentation is missing, retain a minimal redacted reproduction, exact version, failed command/result and needed capability. Tell the developer what is blocked. Do not import the author's private scripts or assume a supervising model will supply the missing implementation. Resume from a published fix or documented supported alternative when available.
- If a driver defect is exposed, preserve the failed evidence, follow the repository's authorized engineering/release process, and reassess affected coverage against the resulting candidate. Do not silently lower the test's expectation.
- If approval cannot be requested through the interface, state the exact question visibly with the review links. Never say an approval is pending unless the question and artifact have actually been presented.
- If there is only an external wait, save the state and next action. Do not spend repeated assistant turns restating healthy status. Do not mark the submission completed or stop the collector because the assistant is waiting.

At the end, retain enough private evidence for another person to verify the candidate, performed tests, qualifications, authorization and delivery independently. Leave public driver documentation free of pending-submission claims. Record any actual Crestron acceptance separately if and when it arrives.
