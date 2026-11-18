# One approval for signing and submission

This is the default **operator procedure** for a prepared submission. It is not a
Crestron requirement to ask separately for signing and sending. Use separate
human approvals only when requested or when a selected protected integration
requires them; establish that limitation before requesting approval.

## Scope to approve once

Freeze and present the actual unsigned signing copy, package, technical evidence
review, exact sender/recipient/subject and cover letter. Disclose every incomplete
test and interpretation consistently. Review the current uploader form and terms.
The letter contains one `{{PACKAGE_DOWNLOAD_URL}}` token, not an invented URL.
Run package preflight and reconcile the actual source/runtime/help identity before
freezing these pins; the scope guard checks integrity, not package correctness.
Approve the signer, signature identity, two official fields and the exact signing
date. State the expiry explicitly; do not silently extend it in a stage adapter.

The approval question must explicitly authorize **both signing and upload/send**.
For example: apply the stored signature/date to this exact unsigned PDF; upload
this exact package under these terms; email this letter and the resulting signed
PDF to this recipient, replacing only the verified package URL. An approval to
sign alone or to prepare correspondence cannot be expanded into that authority.

Use the private [packet](templates/CombinedApprovalPacket.example.json),
[authority](templates/CombinedAuthority.example.json),
[technical review](templates/TechnicalEvidenceReview.example.json), and
[signed inspection](templates/SignedInspection.example.json) templates. They are
intentionally non-executable placeholders, not approvals or evidence. Provide
every official inventory row and actual digests; keep the filled files private.

## Executable scope guard (unreleased source)

`submission check-combined-approval` is a new, offline source command. DevTools
1.29.0 does not contain it; use a complete console built from the reviewed source,
or run `tools/submission/combined_approval.py` with the document dependencies.
It reads no signature or credentials and performs no network operations.

```text
submission check-combined-approval --packet PRIVATE_PACKET --packet-sha256 PACKET_PIN --authorization PRIVATE_AUTHORITY --authorization-sha256 AUTHORITY_PIN --output NEW_PRE_SIGN_CHECK
```

The guard rechecks the independently pinned packet and explicit combined
authority, all referenced evidence/files, full row/field mapping, checkbox values,
correspondence, provider pins, signing date and unchanged expiry. `Checked` is the
reviewer's stated disposition, not a newly authenticated or automatically passed
test. It preserves `ManualTechnicalReview` versus `AutomatedEvidenceReview` and
reports `producerAuthenticationPerformed: false` and `testOutcomesReassessed: false`.

## Signing a manual technical review (unreleased source)

For the explicitly selected manual review route, the new source command is:

```text
submission sign-reviewed-packet --packet PRIVATE_PACKET --packet-sha256 PACKET_PIN --authorization PRIVATE_AUTHORITY --authorization-sha256 AUTHORITY_PIN --output NEW_ABSOLUTE_DRIVER.signed.pdf --report NEW_PRIVATE_SIGNING_REPORT --credentials PRIVATE_BINDINGS
```

The final `--credentials` pair uses the existing encrypted named-signature store.
Packet/authority pins and explicit combined permission are checked before opening
that store; signature bytes travel through protected stdin, not a temporary image
or log. The Python stage rechecks the complete packet before applying the approved
signature/date, makes the fields read-only and preserves printed content and marks.
It refuses `AutomatedEvidenceReview`: use the existing revalidating signer for that
route, rather than silently downgrading a failed automated review to manual.

The report remains explicitly manual, with no producer authentication or newly
passed tests. A stored signature grants no authority. Existing output files are
not overwritten; inspect any retained partial output before resuming. This command
signs only and requires the post-signing inspection and scope check below. It
does not produce an authenticated automated evidence bundle or send anything.

## Inspect and bind the signed result

After authorized signing, render/inspect the result. Record the actual signed PDF
hash, reviewer/time, legible signature/date and unchanged content outside those
fields. Use the same command with all four extra inputs:

```text
--signed-form ACTUAL_SIGNED_PDF --signed-form-sha256 ACTUAL_SIGNED_PIN --signed-visual-review PRIVATE_INSPECTION --signed-visual-review-sha256 INSPECTION_PIN
```

The result binds the actual signed output to the original authority. It checks
canonical fields/widget appearances and unchanged printed streams/notes; the
responsible operator's rendered review remains necessary. It neither invents a
future human-reviewed signed hash nor asks for a second human decision. Re-run
the check before pending delivery. The result remains private; it is not a vendor
form or permission supplied by the evidence worker.

## Stage records and integration limits

Human authority and technical stage records are different. A trusted coordinator
can derive signing and then delivery records from the one independently approved
scope after verifying the actual output. Retain the parent approval digest,
original deadline and exact output digests in those records. Never represent the
derived record as a second user response, reset its expiry or invent prior review
of a not-yet-generated PDF.

The existing `sign-self-test-form`, `prepare-signed-review`, complete delivery and
declared-gap delivery retain their own evidence schemas and checks. The guard's
JSON is **not** a drop-in authorization or replacement for those validators.
A protected adapter must explicitly map the checked combined scope to its own
records and retain the parent authority. The existing CI stage templates do not
automatically do that conversion. Do not set `ProducerAuthenticationConfirmed`
when only manual technical review was possible, or label the guard's success as
an automated evidence pass. Follow [retained evidence](RetainedEvidenceAndApprovals.md)
when a custom collector lacks the native producer contract.

For a new task, choose and validate that adapter during preparation. Reuse public
uploader/SMTP APIs and a durable journal. Do not build a new driver-specific
mailer, decrypt a signature into a temporary image, or change a frozen sender at
the send step. This guard adds a tested authority boundary, not an end-to-end
automated manual-evidence submission adapter or a new live provider result.

After a verified signed result, upload the unchanged package, download and verify
its hash, substitute only that uploader URL, validate the outgoing MIME/body and
attachment bytes, and send once. Changed terms, package, content, disclosures,
destination, additional attachments, expired approval or an uncertain send require
stopping/reconciliation and, when the approved scope changes, fresh authority.
