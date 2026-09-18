# Crestron upload provider

Current source includes `CrestronSubmissionUploader`. It is not included in the published DevTools 1.7.0 package. The provider implements the [observed uploader flow](UploaderInspection.md), returning a `SubmissionUploadReceipt` only after the retrieved package matches the uploaded bytes. It does not send email, sign forms or establish Crestron acceptance.

## Configure and connect

Construct the provider with:

- An authorized `NetworkCredential` for the uploader, loaded from private settings.
- The lowercase SHA-256 of the reviewed upload form and of the accepted terms page. These refer to the exact response bytes. Changed pages stop the operation before the POST; review the change before updating the pins.
- An existing protected local directory for private provider receipts. Restrict access before invoking the provider. It does not configure filesystem permissions for you.
- A total operation deadline between one second and ten minutes, including preflight reads, upload and verification download.

```csharp
using var uploader = new CrestronSubmissionUploader(
    privateUploaderCredential,
    reviewedFormSha256,
    acceptedTermsSha256,
    privateProviderReceipts,
    TimeSpan.FromMinutes(5));
```

Your `ISubmissionDeliveryTransport.UploadAsync` implementation delegates to `uploader.UploadAsync(package, filename, cancellationToken)`. Its `SendAsync` implementation must use your separately configured mail provider. Invoke that composed transport through [guarded dispatch](DeliveryProcessBridge.md), which revalidates the exact plan before each external step. Do not call the upload method from an automatic retry policy: the provider is upload-only, and duplicate prevention belongs to the delivery journal.

Keep uploader login values, accepted-terms authorization, original approval files and mail credentials outside repositories and public artifacts. A settings hash is an integrity check, not proof that an authorized person accepted the terms or approved delivery.

## Provider behavior

The provider snapshots up to 64 MiB from the input stream's current position, validates a plain `.pkg` filename, and saves an attempt identity. Filenames may contain ASCII letters, digits, dots, underscores and hyphens. The outer journal independently verifies the package against the approved plan before passing its stream.

It checks the form and terms pages, writes a durable POST intent, and submits the observed multipart controls once. Its HTTP handler disables automatic redirects and default Windows credentials. Basic authentication is attached only to requests to the expected HTTPS origin. There is no application-level request retry.

The upload response must identify the exact filename. A new-upload response contains one download link and one matching delete link; the observed already-uploaded response contains one download link. Both require identical-byte verification, so a filename match alone cannot establish success. The download link serves an HTML landing page. The provider reads the observed Download button target, validates its HTTPS origin/path/query, then retrieves and hashes the archive. Unexpected markup, response status, duplicate fields, redirects, foreign links or different package bytes fail the operation. Link validation does not execute page scripts. The delete link is retained but never invoked.

The timeout covers the complete operation. HTML responses are limited to 2 MiB; the archive download is limited to the original package length. Response content is retained only in the private attempt directory, including provider links and the verified package copy. Protect this storage: the returned links can grant access to the upload. The receipt returned to the delivery journal identifies the private attempt and verified package hash. Do not publish that journal, raw responses or private receipt directories as general CI artifacts.

## Failures and reconciliation

An attempt that reached POST intent but did not finish verification records `RequiresReconciliation`. A failed download does not establish that the upload failed. The outer delivery journal retains an unknown outcome and blocks automatic replay, including when a later invocation would otherwise pass. Inspect retained provider responses and establish the actual outcome before invoking the journal's explicit reconciliation operation. `VerifyRetainedUploadAsync(package, filename, originalAttemptId, cancellationToken)` verifies the original intent against the supplied package, then follows the saved response's links using GET requests only. It writes a separate verification attempt and preserves the original failure. Use its confirmed receipt as evidence for explicit journal reconciliation; it does not change the journal or resume email by itself.

An error after the provider confirmed upload but before the delivery journal saved its receipt also requires reconciliation using the provider's retained evidence. Never delete the journal or its private attempts to force a retry. This provider does not supply a remote lookup by submission ID, automatic deletion, or an exactly-once guarantee from Crestron.

## Verified scope

Offline tests exercise the normal multipart flow, changed form/terms, redirects, HTTP errors, lost responses, invalid/ambiguous links, changed filenames, download mismatch/overflow, timeout and the journal's refusal to replay an uncertain upload. A private offline replay of the actual service responses from the authorized transport test also verified the original package bytes through this provider.

A live test of this provider reached the service's already-uploaded response. The original attempt stopped because that response differed from the earlier new-upload response; the failure remains retained. After adding that observed response variant, read-only verification of the saved response downloaded and verified the exact test bytes without another POST. Fresh-file creation through the provider, broader provider recovery cases and mail integration still need live validation. Production submission also requires current authorization and the final driver evidence/form. Ordinary releases remain independent of this optional submission stage.
