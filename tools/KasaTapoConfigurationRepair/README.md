# Kasa/Tapo Configuration Repair

Separate customer-test utility, version 1.0.0. This does not change the driver or
the frozen Binding Diagnostics 1.0.0 download. Supports only Home 4.012.0194 and
driver 2.1.002.0004, for wholly missing HS220(US), HS200(US), S515(US) records.

Build using `Build.ps1 -OutputDirectory <new directory>` on Windows. Run the
sibling KasaTapoConfigurationRepair.Tests project with `dotnet test`. This embeds
the local DevTools implementation; it does not publish a DevTools NuGet package.

Inspection pins SSH/HTTPS identities before credentials are submitted. The repair
uses a shared processor lease, DPAPI CurrentUser recovery journals, reviewed exact
bindings, stopped-source replanning, a strict independent candidate diff,
hash-checked staging, two durable rename intents and retained originals. No
mutation is automatically retried. Recovery requires a separate local confirmation
and refuses unknown active-file contents. The UI prevents closing during a run.

Before replacing the stopped manifest, only selected UserAttributes and
ConfigurationNeeded may differ. Post-reboot comparison canonicalizes JSON
reference numbering, allows selected children to become WasDeserializedFromDisk,
and ignores only the payload of Home's exact typed SecureString object under
SecurePersistedValue (ciphertext can change during normal startup). The wrapper's
type/access/layout and all other fields remain compared. Actual plaintext inside
opaque secure strings cannot be verified. Runtime device identities and native
light readiness are checked independently. No lighting commands are issued.

The earlier lab procedure added the same records to a Demo L530 child and verified
them after a full reboot with IDs, scenes and programming preserved. That child
was already online before the patch, so this was not a reproduction of the
customer upgrade failure. The Windows orchestrator has offline failure-injection
tests and read-only live connection/persistence checks; its complete destructive
workflow and recovery have not yet been executed end to end as this executable.
Physical US HS220/HS200/S515 behavior remains customer validation.

Customer instructions are in Instructions.txt. Publication, signing and sending
email are separate actions; Build.ps1 performs none of them. All private journals
and customer correspondence must remain outside public source/downloads.
