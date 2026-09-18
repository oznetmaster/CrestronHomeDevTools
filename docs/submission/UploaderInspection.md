# Crestron uploader: observed behavior and delivery work

The [official submission instructions](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Submit-a-Driver/Submit-a-Driver.htm) direct developers to the [Crestron file uploader](https://uploader.crestron.com/index.php) and publish its shared login. Read-only inspection on 17 September 2026 was followed by an explicitly authorized transport test on 18 September.

The test uploaded a 285-byte archive containing only a transport-validation note, then downloaded it and verified identical bytes and SHA-256. It contained no driver, credentials, device settings or signature. No email or certification submission was sent. This validates one observed service flow; it does not establish a released adapter or a stable public API.

## Verified upload and retrieval flow

| Step | Observed behavior |
| --- | --- |
| Form and terms | HTTPS Basic authentication succeeded. The displayed form and terms matched the previously reviewed copies before the test accepted the terms. |
| Upload | One multipart POST to `/upload.php`, with file field `upfile`, returned HTTP 200 and an HTML success message naming the file. |
| Upload receipt | The response contained a download link and a separate delete link. Both were retained privately. HTTP 200 alone was not treated as sufficient proof. |
| Download page | The returned `/download.php?file=...` URL served an HTML page, not the archive bytes. Its Download button selected a same-origin HTTPS `/download2.php` URL with `a` and `b` query parameters. |
| Archive retrieval | Following that observed button target returned HTTP 200, `application/octetstream`, and exactly the original 285 bytes. The SHA-256 matched. |

The tested multipart request preserved the observed form controls: `from` with its displayed placeholder, both hidden `operation` values (`1` and `2`), and checked `agreecheck`. These are observations of that form, not permission to assume that later service versions accept the same fields. Reinspect changed forms rather than silently inventing replacements.

The delete link used `/download.php` with `file` and `del` parameters. It was **not invoked**; deletion behavior remains unverified. Error responses, interrupted transfers, large-file handling, link expiry, redirects and replay behavior were not established by this successful small-file test. No automatic POST retry or redirect was used.

The [FAQ](https://uploader.crestron.com/index.php?page=faq) advertises a 2048 MB limit and removal after 35 days without a download. Those are provider statements, not limits exercised by this test. Keep an independent durable copy of the approved package, evidence and receipts.

## Adapter requirements and remaining work

The [delivery journal](DeliveryJournal.md) persists upload/send intent and stops after uncertain outcomes. The [preparation stage](DeliveryPreparation.md) validates the signed review and prepares exact file hashes. The [process bridge](DeliveryProcessBridge.md) can revalidate that handoff immediately before each external step. These checks do not themselves implement an uploader or mail provider. The journal currently limits each file to 64 MiB.

A production adapter must:

- Restrict authentication and every retrieved link to the approved HTTPS origin; do not forward credentials through redirects.
- Submit the verified package bytes once, retain the actual response privately, and distinguish the download page from the final archive.
- Validate the returned links and downloaded package hash before confirming its upload receipt for email delivery.
- Preserve an uncertain result for reconciliation rather than retrying the POST or creating a new journal.
- Keep shared login credentials, download/delete tokens and raw provider responses out of public logs and artifacts.

Review the service's [terms](https://uploader.crestron.com/index.php?page=tos) before an authorized upload. Do not embed the published login or personal delivery credentials in source code or examples.

The supported production adapter, email-provider configuration and a controlled email test remain unfinished. An actual submission email must follow Crestron's published procedure, including the download link and signed self-test PDF. Private evidence archives and signature source images must not be attached.

Provider acceptance or successful retrieval does not establish Crestron review, certification or approval. Ordinary driver and library releases remain independent of this optional submission stage.
