# Crestron uploader: observed behavior and delivery work

Authenticated read-only inspection on 17 September 2026 confirmed access to the [Crestron file uploader](https://uploader.crestron.com/index.php) using the shared login in the [official submission instructions](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Submit-a-Driver/Submit-a-Driver.htm). No file was uploaded, terms accepted or email sent during this inspection. This is an implementation finding, not a released upload adapter or successful submission.

## What was observed

The HTTPS page accepted Basic authentication. It has a multipart file form, a terms checkbox and JavaScript that selects an upload endpoint. Its [FAQ](https://uploader.crestron.com/index.php?page=faq) describes receiving a unique download URL after uploading, then sending that URL by email. The service advertises a 2048 MB limit and removes files after 35 days without a download. Keep an independent durable copy of the approved package, evidence and receipts.

The page source identifies a file field named `upfile` and a conditional form target of `upload.php`. No POST was made, so exact submission encoding, success/error response format and download-link validation remain unverified. These observations must not be treated as a supported, stable API contract. Recheck the actual service when implementing an adapter; do not guess a success response from the form alone.

Review the service's [terms](https://uploader.crestron.com/index.php?page=tos) before an authorized upload. Do not embed the published login or personal delivery credentials in source code, examples, logs or public artifacts.

## How this fits delivery

The [delivery journal](DeliveryJournal.md) already persists upload/send intent and stops after uncertain outcomes. The [preparation stage](DeliveryPreparation.md) validates the signed review and prepares exact file hashes; it does not transmit them. The journal currently limits each file to 64 MiB, regardless of the uploader's larger advertised limit.

A real adapter must restrict authentication to the intended HTTPS origin, disable redirects that could forward credentials, disable automatic POST retries, submit the verified package bytes once, and retain a confirmed private download receipt. Validate returned URLs against observed service behavior. Where retrieval is supported, compare downloaded bytes with the approved package hash before composing the submission email. Do not claim idempotency or implement a deletion endpoint without evidence that the provider supports it.

A controlled upload is still needed to establish the real response and retrieval behavior. Email-provider configuration and a controlled recipient test are separate prerequisites. The actual submission email must follow Crestron's published procedure, including the download link and signed self-test PDF. Private evidence archives and signature source images must not be attached.

Interrupted upload or email outcomes require reconciliation using retained provider evidence. Lack of a local response does not establish that nothing was transmitted. Provider acceptance also does not establish Crestron review or approval.
