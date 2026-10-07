# Rehearsal delivery

Rehearsal uses the normal signed-review preparation, evidence revalidation, delivery
coordinator and SMTP sender. The approved plan identifies the rehearsal environment,
exact test recipient and whether delivery sends email or only records a local mock.
Changing any of these requires a new approval and plan. Existing production plans
retain their original digest and default to Production.

## Preparing the test submission

In the private prepare-delivery settings select environment="Rehearsal",
rehearsalRecipient="YOUR_TEST_MAILBOX", and sendRehearsalEmail=true. The separately
pinned delivery authorization must also contain environment="Rehearsal",
sendRehearsalEmail=true, that exact recipient and
subject="[REHEARSAL] Driver Submission Package". All normal artifact, signed visual
review, expiry and evidence checks still apply. Crestron mailboxes are refused for
rehearsal. Do not put credentials or a personal test address in public configuration.

Create the dispatch setup with schemaVersion=1, environment="Rehearsal",
preparedDirectory, preparationSettingsPath, deliveryReviewSha256, journalDirectory,
attemptsDirectory, destinationDirectory and revalidationTimeoutSeconds. Add:

```json
"mail": {
  "receiptDirectory": "C:/private/rehearsal/smtp-receipts",
  "host": "smtp.example.org",
  "port": 587,
  "timeoutSeconds": 60
}
```

Use absolute private paths. Journal, attempts, local upload destination and SMTP
receipts must already exist, be separate and outside retained inputs. The upload
destination starts empty; reuse it only for the same plan. The settings command
verifies the prepared plan and installed console inventory and returns a hash for
review. It does not grant approval or send anything.

```text
submission-delivery-settings --rehearsal --settings PRIVATE_SETUP_JSON --output NEW_PRIVATE_DISPATCH_JSON
submission-deliver --rehearsal --settings PRIVATE_DISPATCH_JSON --settings-sha256 REVIEWED_SHA256 --execute-approved --credentials PRIVATE_BINDINGS_JSON
```

Use the existing owner-bound SMTP credential binding. Rehearsal resolves only SMTP
credentials, not uploader credentials; it never contacts the Crestron uploader.
Required TLS, sender/host/port binding and private provider receipts use the existing
mail implementation. Credentials must remain on their owner's computer.

New automated rehearsals use the selected GitHub release's package download link
and attach only the signed PDF, matching production email structure. Before SMTP
connects, the worker anonymously downloads that link and requires its SHA-256 to
match the approved package. Downloads are bounded to 64 MiB and 90 seconds;
redirects must remain HTTPS. A missing, private or changed asset stops delivery.

For standalone preparation, set rehearsalPackageDownloadUrl to a recipient-accessible
HTTPS package URL in both the preparation settings and delivery authorization.
The C# complete and qualified plans expose RehearsalPackageDownloadUrl. The URL
is covered by the plan/approval digest; changing it invalidates approval.
The package is still retained locally for rehearsal evidence. The receipt identifies
local retention plus the existing link; it does not claim a new vendor upload.

Old plans without this property retain their original digest, correspondence and
attachment behavior so existing journals are not rewritten or automatically resent.
Use link mode for new rehearsals: some mail paths accept package attachments at
SMTP yet do not deliver the messages to the recipient.
The message is visibly labelled REHEARSAL. SMTP acceptance does not prove inbox
receipt, Crestron acceptance or certification.

Successful output identifies Environment="Rehearsal", State="RehearsalCompleted",
Submitted=false and RehearsalCompleted=true. The durable journal retains the real
SMTP receipt. Completed or uncertain sends are never automatically repeated. For
an uncertain outcome, independently establish the provider result before using
the shared reconciliation operation; preserve original files and receipts.

## Automation integration

The current source uses the same protected signing and delivery stages in both
modes. Install an independent RehearsalPlan in the protected-worker configuration,
with Environment="Rehearsal", owner-bound CredentialBindings, separate
SigningApproval and DeliveryApproval channels, and Delivery.RehearsalRecipient.
The existing Plan remains Production. Run settings cannot override these installed
bindings or select production authority for rehearsal. Setup preserves an explicit
rehearsal plan; it does not convert a production plan into rehearsal authority.

Configuration checks report missing rehearsal delivery bindings before testing.
The evidence worker hands off signing, delivery and retention to the protected
worker. Unsigned review and approval waits are not successful completion and do
not retire the worker. Final retention requires a matching rehearsal delivery
receipt and reports RehearsalCompleted.

## Qualified rehearsal submissions

The current source also carries declared gaps through protected rehearsal signing,
test-mail delivery and retention. A shortened rehearsal does not satisfy the
24-hour requirement: the form keeps that item unchecked, its signed receipt keeps
GapsDeclared, and the reviewed email includes the duration qualification.

For this route the installed rehearsal delivery binding supplies GapSummary and
optionally Correspondence. The same SubmissionReviewDeliveryPlan, independent
SubmissionReviewApproval and SubmissionReviewRequestDelivery coordinator used for
production revalidate the signed packet and evidence before both delivery steps.
Set Environment=Rehearsal and SendRehearsalEmail=true on that qualified plan.
The environment, recipient, declarations, signature disposition and correspondence
are covered by its approval digest. Production defaults retain their old serialized
shape and digests. A production transport cannot execute a rehearsal plan.

SubmissionRehearsalReviewMailTransport retains the upload locally and sends the
reviewed PDF and verified package link through the owner-bound SMTP service. Correspondence
is labelled [REHEARSAL]; any custom body must use that subject and one
{{PACKAGE_DOWNLOAD_URL}} token, which becomes the approved package URL in link mode.
Legacy plans retain their package-attached wording. The generated rehearsal notice is always
included. Qualified rehearsal requires explicit email delivery; it cannot be
completed using the complete-plan local-only mock.

The generic prepared-request C# API and protected automation support this route.
The earlier complete-only prepare-delivery/dispatch commands described above still
require complete evidence; they are not an alternate route for declaring gaps.
Completed and unknown outcomes retain the shared journal and no-resend behavior.

## Local component tests

For software-only tests set sendRehearsalEmail=false in preparation and approval
and omit mail from dispatch setup. SubmissionRehearsalTransport retains package.pkg,
signed-form.pdf and receipts with externalDeliveryAttempted=false, without network
or credentials. A completed local mock cannot be promoted into email delivery:
the selected delivery method is part of the approved plan digest and journal binding.

Packaged tests exercise real PDF creation, synthetic signing, evidence revalidation,
local delivery and refusal of changed approvals or evidence. SMTP regressions use
a controlled session to inspect exact message contents and uncertain-send recovery.
These synthetic tests do not establish driver compliance or complete the physical
rehearsal. Complete phase-two test-suite migration and real driver acceptance remain
unfinished; these commands do not run missing driver tests.
