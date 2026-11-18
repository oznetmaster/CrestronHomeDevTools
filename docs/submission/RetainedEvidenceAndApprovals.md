# Retained evidence, timing and final approval

These procedure corrections follow the completed RainPoint initial-submission
work in November 2026. They apply to future drivers; they do not alter any sent
package, signed declaration, original test result or Crestron decision. Keep
driver-specific reports, credentials, signatures and correspondence private.

## Separate initial submissions from updates

Record whether Crestron has actually accepted the driver before selecting an
initial or update route. A public GitHub/NuGet release or a pending email is not
portal acceptance. An initial submission uses the full applicable plan, including
its endurance row. For an update, prepare a change-impact assessment: repeat the
affected controls, protocol/state feedback and recovery checks; reuse unchanged
prior assertions only at their demonstrated scope. Do not automatically repeat
24 hours or the entire UI suite for a documentation/metadata-only update, or
automatically exempt a runtime change from endurance. Document the rationale and
any unconfirmed update-process assumptions for Crestron's review.

Keep the public release and submission package identities distinct. Submission
filenames/metadata and embedded help can differ while the runtime remains frozen;
verify that equivalence rather than rebuilding to repair a checklist. Submission
help/support documents are not a reason to add redundant PDFs to public releases.

## Verify identities and avoid displacing existing sessions

Keep app-facing device IDs, cloud IDs, RF addresses, parent/child IDs, room IDs and
hardware identifiers separately labelled. Similar numeric values are not evidence
of the same identifier or device. Verify the physical model and current instance
before acting; preserve older hardware records as historical.

Use the established dedicated test/support account and saved fixture where
available. A fresh cloud login can evict the user's app or another driver session;
do not repeatedly log in just to observe an already connected test. Coordinate
competing instances and retain their configuration/restoration obligations.

## Select a working evidence route before collecting

The normal runbook supports direct collection as well as the automated worker.
Before a long test, try a harmless complete observation through the selected
export, review, form and signing route. Check the schema and producer requirements
of the **installed** console, not a source-preview document. A custom C# collector
can retain useful measurements without producing the NUnit context, discovery,
selection, completion and TRX records required by the automated Android auditor.
File hashes cannot supply those missing records retrospectively.

If genuine retained work cannot enter that automated route:

1. Preserve every original, failed attempt, timestamp and original outcome.
2. Reconcile every official row in one named technical review, recording exact
   source files, candidate/version, measured scope and any interpretation.
3. Identify the review as **manual technical review**, not authenticated automated
   producer validation. Missing schema and missing functional testing are different.
4. Use an explicitly selected, reviewed direct signing/delivery adapter that can
   accept that review method. Do not set `validationChecksPassed`,
   `ProducerAuthenticationConfirmed`, or protected-stage receipts to fabricated
   values; do not relabel Android evidence to avoid its auditor.
5. If the installed adapter cannot accept the truthful record, establish the
   adapter before asking for signing approval. Do not weaken its existing checks
   or improvise transport and credential access at the final send step.

The [combined-approval scope guard](CombinedApproval.md) preserves the review
method and binds artifacts; it does not authenticate a producer, reassess a test,
make an unsupported adapter compatible, apply a signature or send anything.

## Decide every official row explicitly

Use the full official text. N/A means the shipped feature/widget is absent, with
a capability-based reason. A supported feature that was not physically tested is
incomplete, not N/A. Software isolation checks do not establish independent control
of two physical devices. One representative device need not imply live tests of
every supported hardware model; state the actual coverage.

Complete authorized feasible checks before asking to submit with limitations.
Only the developer chooses a disclosed incomplete submission. Keep those rows
unchecked and explain the same limitations in the form notes and letter.
Crestron decides whether that is acceptable. A later successful observation adds
evidence; it does not delete a prior failure or change its original outcome.

Reconcile totals from that register, including all N/A and incomplete rows. Do not
copy RainPoint's counts into another driver. Once new evidence closes a gap,
produce a new revision; preserve the earlier form and its approvals.

## Measure the right clock, without inventing causation

For a cloud integration, distinguish at least:

| Boundary | Evidence needed |
| --- | --- |
| Physical network interruption | Instrumented event, or clearly labelled operator confirmation |
| Provider first exposes a new state | Timestamped provider observation, if available |
| Driver publishes confirmed state | Timestamped installed-driver property/state capture |
| Home visibly updates its native tile | Actual timestamped frames, with the icon/text and enabled state inspected |

An operator reply is not the physical event time. The first driver read is not
the provider's first availability time. If a 15-second criterion is interpreted
as driver-publication-to-tile for a cloud integration, name the reviewer and
interpretation and disclose the separate interruption-to-status delay. This is
our submitted interpretation, not evidence Crestron accepted a different rule.
Do not claim a 15-second end-to-end response or attribute the preceding delay
solely to the cloud when its receipt/availability clock was not observed.

Use conservative bounds: for publication within a read interval, the earlier
possible publication time to the completed first qualifying frame gives an upper
bound. Keep clock source, synchronization uncertainty, read start/end and capture
start/end. Retain the raw frames. A screenshot of a later state establishes no
response deadline on its own. A hierarchy label supports state, not visual layout.

Offline and online recovery are distinct assertions. A real disabled/red OFFLINE
tile and subsequent automatic normal tile recovery can support both with one
sequence. Do not add a numerical limit to an official row that specifies none.

## Keep network, reboot and power predicates separate

A Wi-Fi-only hub has no Ethernet cable: a scoped access-point block can exercise
its actual network path. The exact block/unblock target and both confirmations
must be recorded. Blocking the hub and disconnecting the processor Ethernet are
separate tests when performed separately; do not describe them as simultaneous.
Compare the full official wording and obtain an explicit interpretation if needed;
do not invent a simultaneous-outage requirement or assume Crestron accepted ours.

Disconnecting a PoE processor cable interrupts both network and power. Use an
independently powered test processor for a network-only test when necessary,
verify uptime stayed continuous, and record its firmware/version. A later cleanup
reboot is not a network recovery observation or a physical power-loss test.
Network tests do not close an unperformed coordinated power-interruption row.

Cloud transport reconnecting alone is not a driver or device failure. Retain the
transport status and observed availability/function separately. Conversely, an
unproven cloud timestamp cannot establish that a required response passed.

## Protect endurance and finish cleanup independently

During the retained interval, leave the candidate and collector unchanged. Finish
non-disruptive installer inspection and document preparation alongside it; schedule
interruptions and removal after the endpoint checks. Verify the remote process
and original samples rather than inferring health from a local launch wrapper.

Record actual first/last samples, elapsed time, maximum gap, candidate/payload and
process continuity, sampled states, real baseline and endpoint functionality.
Transient transport reconnects, collector faults and device failures remain
distinct. Preserve an incomplete collector attempt separately; do not combine it
with a later full interval or call an export error a product failure.

Restore original configuration, child identities, duration/state selections,
pairings, app location and reservations separately. A capture wrapper's missing
default screenshot filename does not erase real nested frames; inspect them and
retain the wrapper's original result rather than rewriting it. Installed-driver
removal, stored package removal and clearing an in-memory catalogue listing are
different operations. Reboot only when needed and authorized, and do not count
that cleanup as outage-test evidence.

## Prefer one combined approval, then verify internally

Use [combined approval](CombinedApproval.md) once the exact unsigned PDF, package,
review, sender/recipient, subject, letter and provider terms are concrete. Explicit
authorization can cover signing **and** sending, with only the approved signature,
date and verified uploader URL substitutions. Different technical stages may need
different integrity-bound records; they do not inherently require another human
decision. Signing-only permission still grants no delivery authority.

Inspect the signed output internally and bind its actual hash to the original
approval. Do not pretend its future hash was known at review time. If content,
marks, limitations, package, recipient or terms change, stop for a new approval.
Keep separate signing/delivery approvals available when the developer requests
them or the selected protected integration cannot consume combined authority.
Explain such an integration restriction before preparing the approval copy.

Upload once, download/hash-check the exact package, replace only the reviewed URL
token, then verify the actual MIME body and PDF attachment before sending. Keep
intent, upload verification and SMTP acceptance records in one durable journal.
Reconcile uncertain outcomes instead of replaying them. Record `submitted awaiting
response`, not `accepted by Crestron`. SMTP acceptance proves neither inbox delivery
nor portal publication; it may not create an Outlook Sent item.
