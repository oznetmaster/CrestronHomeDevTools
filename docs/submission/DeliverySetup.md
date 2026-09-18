# Prepare delivery settings with the packaged console

**Availability:** this command requires DevTools 1.10.0 or a complete console built from source containing it. It is not present in the 1.9.0 download. Use [the documented build command](ConsoleTools.md#building-the-console-from-source) when building from source.

Use `submission-delivery-settings` to generate the private settings consumed by [protected delivery](DeliveryCommand.md). The console locates and checks its own bundled tools and records their complete file inventory. Developers do not configure an interpreter, a separate .NET runtime or validator paths.

This command sends nothing and grants no approval. Start with a completed [delivery preparation](DeliveryPreparation.md), including its independently authorized signed form and delivery plan. Download and verify a trusted console distribution before generating settings; a hash of arbitrary local software does not establish trust.

1. Extract the complete console to a protected directory. Keep private inputs and writable outputs outside it. The service identity needs read/execute access to the console and read access to retained evidence and approvals.
2. Create four separate private directories for the journal, revalidation attempts, upload receipts and mail receipts. Grant the service identity write access to those directories. Preserve them across restarts and failed delivery; they must not overlap or contain the console, approvals, prepared files or reviewed settings. Protect the directories and their ancestors from changes by evidence-producing workers.
3. Copy [the setup example](delivery-setup.example.json) into private storage. Fill in the paths, SMTP server and independently reviewed delivery-receipt, uploader-form and accepted-terms hashes. Use the exact preparation settings that created the delivery directory. Those settings must omit `dotnet` and `validator` when using the bundled console. The setup file contains no credentials.
4. Run the command from the extracted console. Use absolute paths and a new output file:

```powershell
.\CrestronHomeDevTools.Console.exe submission-delivery-settings --settings C:\Private\Submission\setup.json --output C:\Private\Submission\dispatch.json
```

The output is a compact receipt with `settingsSha256`, `approvalRequired: true` and `deliveryAttempted: false`. The generated JSON uses schema 2, derives the package, form and plan from the pinned preparation, and inventories the console. It rejects incomplete or expired preparation, changed plans, runtime overrides, overlapping storage and replacement of an existing settings file.

5. Independently review the generated settings, exact artifacts, sender, recipient, accepted uploader terms and tooling. Record the approved settings hash in the protected orchestration environment, separately from files supplied by an evidence worker. Keep the console distribution immutable for that approval. An update to the distribution requires new settings and review.
6. Use [the final-stage workflow template](submission-delivery.yml.example), setting the protected environment variables `CRESTRON_SUBMISSION_DISPATCH_SETTINGS` and `CRESTRON_SUBMISSION_DISPATCH_SETTINGS_SHA256`. The template verifies the complete tooling inventory already included in these generated settings before starting the console; there is no separate manifest to hand-author. Supply upload and SMTP credentials through protected standard input as described in [Delivery command](DeliveryCommand.md). Never put credentials in this setup JSON, command arguments or build logs. Environment approvals, runner isolation and ACLs must be configured by the operator; this preparation command does not provision them.

The delivery command revalidates the retained evidence and authorization through the pinned bundled console immediately before each pending external step. If approval changes after upload, it preserves that upload and refuses the email. A completed journal is returned without sending again. An uncertain provider outcome requires independent reconciliation; do not delete the journal, choose new receipt directories or retry automatically to bypass it.

## C# integration

Advanced hosts can obtain a `SubmissionBundledRevalidationSettings` snapshot using `SubmissionDeliveryRevalidation.PrepareBundledSettingsAsync`, then retain and independently review it. Pass that record to the `CheckAsync` overload used as the callback to `SubmissionDelivery.ExecuteAuthorizedAsync`. The factory inventories a distribution supplied by the host; the host remains responsible for obtaining trusted software. The console setup command additionally checks its embedded runtime manifest before preparing the settings.

The older explicit-runtime `SubmissionDeliveryRevalidationSettings` API and schema-1 command settings remain supported for existing integrations.

## Verification limits

Automated package acceptance uses synthetic forms, approvals, credentials and provider substitutes in a separate test executable. It exercises the production setup command, command parser, both real bundled revalidation processes, upload preservation after revoked approval, changed-console refusal and completed replay. It does not establish a real provider delivery, service-account access or Crestron acceptance. Validate the protected account's read/execute and receipt-directory access before enabling a real submission.
