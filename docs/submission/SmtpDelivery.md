# SMTP submission delivery

`SubmissionSmtpMailer` is a current-source API in CrestronHomeDevTools; it is not in the published 1.7.0 package. It sends the approved signed PDF and verified package download link through an explicitly configured SMTP account. Outlook does not need to be installed or running.

## Configuration and use

Supply the sender, SMTP host, port, username and password from private configuration. Port 465 requires TLS immediately; port 587 requires STARTTLS before authentication. Neither plaintext nor opportunistic encryption is supported. System certificate validation remains enabled. Providers requiring OAuth instead of password authentication need a separate authentication implementation.

For example, [easyMail documents](https://kb.easydns.com/knowledge/easymail-settings-and-specifications/) `mailout.easymail.ca`, port 465 with SSL/TLS, and the full mailbox address as the username. Its mailbox password is distinct from the easyDNS administration password. This is a configuration example, not a dependency on that provider; other developers supply their own service and authorized sender.

```csharp
var mailer = new SubmissionSmtpMailer(
    smtpHost, 465, senderAddress, privateSmtpCredential,
    protectedMailReceiptDirectory, TimeSpan.FromSeconds(90));
var transport = new CrestronSubmissionTransport(uploader, mailer);
```

Pass this transport to the [authorized delivery journal](DeliveryJournal.md), with its fresh approval callback and private journal. The [revalidation bridge](DeliveryRevalidation.md) checks the current authorization before each external step. Constructing the transport does not upload or send anything. The journal's authorization and reconciliation rules still apply; do not call the provider directly to bypass them for a real submission.

The selected sender must equal the approved plan's sender. The service account must be allowed to send as that address; receiving forwarded mail does not establish permission to send from it. Keep credentials in the private worker's secret store, outside source and public artifacts. Do not pass passwords through command-line arguments or enable SMTP protocol logging with credentials. Provisioning and an actual controlled send are separate deployment steps.

## Message and receipt

The message contains one sender, one recipient, a subject derived from the approved package filename, a plain-text download link and package SHA-256, and exactly one PDF attachment. The PDF is read from the stream's current position, limited to 64 MiB, and checked against the approved signed-form hash before connecting. The message ID must match the journal's deterministic ID for that plan. No source signature image, private evidence archive, package binary or additional recipient is included.

A private `mail-*` attempt retains the plan digest, exact composed message, durable send intent and SMTP completion response. Protect this folder: the `.eml` contains the signed form and private download link. The provider never records the password or a raw authentication exchange. The timeout covers form reading, connection, authentication and sending.

MailKit's successful send completion establishes SMTP server acceptance. The free-form server response is retained even when it is empty. Acceptance does not establish delivery to the recipient's inbox, reading, review or certification. A connection disposal failure after saved acceptance cannot turn the confirmed send into an apparent failure.

## Uncertain outcomes

There are no automatic send retries. An error after send intent may mean that the server accepted the message but its acknowledgement was lost. The private attempt records `RequiresReconciliation`; the outer journal retains `OutcomeUnknown` and prevents automatic replay. A connection/authentication failure before send intent records `NotSent` locally, but the journal still requires explicit reconciliation before retrying an uncertain external step.

A protocol rejection retains the SMTP status, failure category and server explanation in the private failure record, with the configured password redacted. The caller still receives a generic error. A successful login does not establish permission to send from another address or domain; some servers enforce that policy when accepting the recipient.

Preserve original attempts. Inspect the provider's acceptance receipt, delivery logs, recipient evidence or support response before explicitly reconciling the journal. SMTP generally does not save a copy into an IMAP Sent folder, so absence from Outlook's Sent folder is not proof of non-delivery. This implementation does not automatically append a Sent copy, query provider delivery logs, detect bounces or guarantee exactly-once delivery. A deterministic Message-ID is useful for correlation, not a universal duplicate-suppression mechanism.

## Validation status

Offline tests use a simulated SMTP session with real MIME generation and parsing. They verify exact recipient/attachment/Message-ID, private acceptance records, preserved success on disposal failure, invalid-input rejection, connection failure, lost acknowledgement and timeout without a retry. This does not validate real credentials, a provider's TLS/authentication behavior, inbox delivery or a final Crestron submission. Those controlled live checks remain required before production use.
