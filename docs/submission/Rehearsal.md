# Validate a complete submission rehearsal

A successful assisted submission proves that a packet was prepared and delivered.
It does not prove that the automatic controller can perform every handoff, or that
an operator can complete the job from the starting document alone. Record these
as different validation results.

Use the public [automation worker](AutomationWorker.md), in **Rehearsal** mode,
with a saved private profile. Rehearsal runs the permitted hardware operations
and prepares an unsigned review packet. It stops before signing, upload and
submission email. Validate those protected operations with synthetic authority
and test transports separately; never resend a real submission merely to test
the controller.

## Prepare once

1. Select the driver repository, published release and test equipment. Record the
   release ID, resolved source commit and independently verified package digest.
2. Complete the saved stage bindings: Windows and processor tests, app fixtures,
   endurance producer/policy, document templates, evidence mapping and review
   tools. Use `--check-settings` to list omissions before reserving equipment.
   Binding presence alone does not establish that the configuration is correct.
   The [setup app](SetupApp.md#prepare-a-controller-rehearsal) can combine a
   frozen factual snapshot with a reviewed settings template and tooling
   manifest to prepare pinned release profiles and an empty registry. It lists
   missing bindings without starting work or exporting saved secrets.
   The public [WeatherLink functional producer](../../samples/WeatherLinkEnduranceProducer/README.md)
   supplies that driver's metric/local-station endurance checks and automatic
   lifetime initialization; its settings and published inventory must still be
   prepared for the selected candidate before freezing the plan.
3. Verify the actual worker account can use its named processor credentials and
   the selected emulator. Source/test workers must not have signing or mail
   secrets. Record existing machine and driver state before permitted changes.
4. Pin the reviewed settings and tools, and register this rehearsal separately
   from any completed real submission. Do not edit a previous attempt's frozen
   inputs or overwrite its evidence.
5. Use the normal endurance requirements when claiming a complete submission
   rehearsal. A deliberately shortened development exercise must be identified
   as such and cannot establish the full endurance requirement.

## Run through the controller

Start through release discovery or the registered GitHub rehearsal dispatch.
Let the ordinary Windows worker advance and resume the attempt. Do not manually
copy evidence between stages, repair checkpoint files or supply hidden inputs
and then call the run unattended. Do not use AI heartbeat polling to carry the
workflow forward.

For every intervention, keep a private record containing:

- UTC time, stage and retained operation ID.
- What stopped the controller and the original diagnostic/evidence reference.
- Whether it was planned human participation, missing setup, a documentation
  gap, a tool defect or an external service/device failure.
- The exact action taken, the public API/document used, and any changed revision.
- Whether resumption consumed existing evidence or required a fresh attempt.

Preserve failed attempts. Fix tooling on the development branch and rerun only
the affected validation first. Do not automatically restart long tests or
replace the candidate. Reuse completed evidence only through the documented
identity/provenance rules; separated segments are not uninterrupted endurance.

## Report what actually completed

The retained result should state which of these outcomes was achieved:

- Individual stages validated independently.
- An assisted rehearsal reached the unsigned review packet, with interventions
  listed.
- A controller-driven rehearsal reached that packet from the saved profile
  without unplanned intervention.
- Protected signing/upload/email sequencing passed separate test-transport
  validation, including exact authority and duplicate-send prevention.
- A later authorized real submission confirmed provider delivery.

Keep the official checklist dispositions and material limitations in the review
packet. Missing evidence is not a pass, and N/A follows applicability. Neither
rehearsal completion nor delivery asserts Crestron acceptance.

Retain the entry command/profile revision, tool revisions, stage receipts,
intervention record, cleanup/reservation results and final packet identity.
This is the evidence for any claim that another developer or a less capable
model can operate the workflow without a supervising assistant.

Copyright (c) 2026 Neil Colvin. MIT licensed.
