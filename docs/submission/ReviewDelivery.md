# Delivery of a reviewed request with declared gaps

**Source availability:** these C# APIs are additions after 1.12.0 and are not yet released. The existing signature and final-delivery preparation commands still require their complete signed-review chain. The new [unsigned request preparation command](ReviewRequest.md) handles declared form/signature omissions. The [approval and prepared-request coordinator](ReviewApproval.md) connects those requests to this delivery API in C# and through a protected console command.

`SubmissionReviewDeliveryPlan` identifies the exact candidate, review, authorization, package and PDF attachment. It also records the verification mode, verification status, declaration digest, public gap summary, document omissions and attachment kind. These are separate facts:

- `CompleteAgainstInterpretedRequirements` means complete against our interpretation of Crestron's published submission requirements, including testing, evidence and documents. It does not mean an arbitrarily reduced developer checklist is complete.
- `GapsDeclared` means the full inventory has unmet requirements whose outcomes and explanations remain disclosed. Failed, partial, inconclusive and untested observations are not changed to passed.
- Provider delivery state records upload/email progress. `Submitted` means the providers confirmed those operations; it does not prove inbox delivery or a decision by Crestron.

Only actual correspondence from Crestron can establish acceptance, publication or certification. Neither verification status nor a delivery receipt supplies such a decision.

## Documents and correspondence

The attachment can be `SignedSelfTest`, `UnsignedSelfTest` or `DisclosureOnly`. The latter contains a disclosure report when the official form is omitted. Unsigned or omitted forms require an explicit explanation in `DocumentOmissions` and declared-gaps mode. A signed form can accompany declared testing gaps only when the signer has separately authorized its exact, truthful contents. This API neither creates nor applies a signature.

There is always one reviewed PDF attachment; an absent form is not replaced with an invented signature or a claim of successful testing. The preparation layer must generate that attachment, reconcile every document omission with the full interpreted requirements, and verify its content. Selecting an enum does not prove that the supplied PDF is the claimed document. Missing package help is still rejected by the current package validator; this transport API does not bypass that check.

`SubmissionDelivery.ReviewCorrespondence(plan)` returns the exact subject and body for review. The subject is `Driver Submission Package`. Declared-gap correspondence explicitly says that not all interpreted requirements are met, includes the public gap summary and document/signature omissions, and explains the actual attachment type. Complete correspondence expressly limits its claim to our interpretation. Both reserve all acceptance, publication and certification decisions to Crestron. The confirmed provider download URL is appended after upload.

`ReviewPlanDigest(plan)` hashes the plan **and the generated correspondence**. Changed explanations, attachment bytes, document status, verification mode, recipients or generated wording invalidate the previous approval. Review all public text before authorizing it; do not include credentials, private file paths or raw test evidence. The providers attach only the reviewed PDF, not the evidence archive or original signature image.

## Authorized execution through C#

Call `SubmissionDelivery.ExecuteReviewAuthorizedAsync` with the private durable journal directory, reviewed plan, exact package and attachment paths, an `ISubmissionReviewDeliveryTransport` and a trusted revalidation callback. `CrestronSubmissionTransport` and `SubmissionSmtpMailer.SendReviewAsync` implement the provider path. C# developers do not need to edit Python code to use these APIs.

The callback must independently revalidate the candidate, full requirement inventory, original outcomes, all declarations and document omissions, attachment content, current approval and intended recipients before **each** external step. It returns `SubmissionDeliveryAuthorization` with `ReviewPlanDigest(plan)` and the expiry from that actual approval. Returning a matching digest and a newly calculated future time is not an authorization system. No review API authenticates an arbitrary caller's claim that these upstream checks passed.

The returned `SubmissionReviewDeliveryReceipt` keeps `ReviewMode`, `VerificationStatus`, `AttachmentKind` and `DeclarationsSha256` alongside the separate `Delivery` record. Retain the exact plan and approval with the receipt. `ReadReview` and `ReconcileReview` provide the same status and independently evidenced recovery boundary without synthesizing a Crestron decision.

The implementation shares the existing [delivery journal](DeliveryJournal.md). It verifies and freezes file bytes, persists intent before provider entry, rechecks approval immediately before each operation, and never retries an uncertain upload or send automatically. The same package/PDF bytes and sender/recipient pair use the same journal key across the old and new APIs. Changing mode or approval cannot create a fresh attempt for those same bytes. Cooperating workers must use the same durable journal directory; separate directories are not a distributed delivery lock.

## Verification and remaining integration

Offline tests exercise all three attachment kinds, preservation of gap status, exact MIME content, expired or changed approval, altered attachments, lost provider acknowledgements, reconciliation, completed-run idempotency and conflicts with the legacy journal. An integrated journal-plus-mailer rehearsal uses a synthetic uploader and in-memory SMTP session; it sends no email and uploads nothing.

The source [unsigned request preparation command](ReviewRequest.md) produces a revalidated outbound unsigned form or disclosure-only report with a pinned document disposition. Integration tests exercise actual preparation through the [protected console parser and C# coordinator](ReviewApproval.md) with simulated providers, including changed evidence or revoked approval after upload. Remaining work includes other required-document omissions, gap-aware signature authorization, convenience dispatch-settings generation and deployment validation of the protected CI stage. Existing complete-only delivery commands deliberately reject the new review state. No real driver submission is established by these tests.
