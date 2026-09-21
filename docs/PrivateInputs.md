# Reusable private inputs on Windows

The `credentials` commands and `DevToolsPrivateStore` API collect private inputs once and let supported commands refer to them by name. These additions are in development and are not present in the released 1.16.3 console.

The store can hold processor, Windows SSH, SMTP and uploader logins, and a signature image. Creating or populating a store never connects to a processor, sends email, uploads a file or signs a document. Existing processor `configure` profiles and protected standard-input integrations continue to work.

## Create your personal store

Run these commands in a Windows terminal. The configure command prompts for the password without echoing it. Supply a hostname, not a URL, for an endpoint. SMTP entries also record the port and approved sender address; Windows SSH entries record the verified SSH fingerprint.

```powershell
.\CrestronHomeDevTools.Console.exe credentials create
.\CrestronHomeDevTools.Console.exe credentials configure --name mail --kind Smtp
.\CrestronHomeDevTools.Console.exe credentials configure --name monitor --kind Windows
.\CrestronHomeDevTools.Console.exe credentials configure --name uploader --kind Uploader
.\CrestronHomeDevTools.Console.exe credentials list
```

The default directory is `%LOCALAPPDATA%\CrestronHomeDevTools\PrivateStore`. Use `--store ABSOLUTE_DIRECTORY` to select another store. Entries are encrypted using Windows DPAPI for the current user, with restricted NTFS access. Passwords must not appear in command arguments, source files or build logs. Replacing an entry requires `--replace true`.

Save the following *references*, using the actual absolute store path, in a private bindings file. Omit entries the consumer does not need.

```json
{
  "storeDirectory": "C:\\Users\\Developer\\AppData\\Local\\CrestronHomeDevTools\\PrivateStore",
  "smtp": "mail",
  "windows": "monitor",
  "uploader": "uploader"
}
```

The bindings contain entry names, not passwords. Keep them private because their paths can identify your account and environment.

## Use saved entries

`endurance-watch`, `endurance-observe` and `endurance-notify` accept `--credentials ABSOLUTE_BINDINGS_JSON`. Observation uses the Windows entry only for a remote monitor. Notifications bind the SMTP entry to the configured server, port and sender. Remote observation also requires the saved SSH fingerprint to match the observer configuration. Missing or mismatched entries stop unattended execution instead of prompting.

Both `submission-deliver` and `submission-review-request-deliver` accept the option at the end of their existing exact argument sequence:

```powershell
.\CrestronHomeDevTools.Console.exe submission-deliver --settings C:\Private\dispatch.json --settings-sha256 REVIEWED_SHA256 --execute-approved --credentials C:\Private\bindings.json
```

Do this only after the existing delivery review and authorization steps. The named SMTP entry must match the reviewed settings and sender; the uploader entry must be for `uploader.crestron.com`. Credential possession does not replace the independently verified approval. The delivery journal, upload-before-email order and refusal to automatically resend uncertain deliveries still apply.

Omit `--credentials` to retain protected standard-input handoff from another secret manager. The CLI never prints credential values. Application code that receives a `DevToolsStoredCredential` must likewise avoid logging or serializing it into public output.

## Provision a selected service account on this computer

Create a separate store for each service purpose. For example, LocalService has SID `S-1-5-19`:

```powershell
.\CrestronHomeDevTools.Console.exe credentials create --store C:\ProgramData\ExampleMonitor\PrivateStore --service-reader S-1-5-19
.\CrestronHomeDevTools.Console.exe credentials provision --name mail --target-store C:\ProgramData\ExampleMonitor\PrivateStore
.\CrestronHomeDevTools.Console.exe credentials provision --name monitor --target-store C:\ProgramData\ExampleMonitor\PrivateStore
```

Run under an account permitted to create the destination. This is an explicit grant to the chosen service account. The destination uses machine-scoped DPAPI with restricted NTFS permissions: confidentiality from other local accounts depends on those permissions. The creating user, Administrators and SYSTEM retain full access; the selected service receives read access. Do not broaden the folder permissions. Verify a synthetic entry can be read under the actual service identity before provisioning real credentials.

Only named entries are copied. The provision command neither registers a task nor changes a running collector. Point that service's bindings at its service store. Replacing your personal entry does not silently replace its provisioned service copy; provision the update deliberately.

This command is **local** provisioning. Copying a personal DPAPI store to another computer or Windows account does not transfer access. For another computer, use the selected-entry SSH transfer below.

## Transfer one entry to another Windows computer

Install the trusted complete DevTools console on the destination and run `credentials create` there under the Windows account that will own the entry. For a service, create a restricted service store as described above, using an account permitted to create it. Do not send a credential to an untrusted console executable or share the complete store. Remote provisioning neither installs tooling nor creates the destination store automatically.

On the source computer, save a Windows SSH login for that destination with `credentials configure --name worker-login --kind Windows`. Independently verify its SSH fingerprint. Prepare a private destination file containing references and the verified destination details, not passwords:

```json
{
  "Host": "worker.example.test",
  "Port": 22,
  "SshFingerprint": "VERIFIED_SSH_SHA256_FINGERPRINT",
  "ConsolePath": "C:\\Tools\\DevTools\\CrestronHomeDevTools.Console.exe",
  "StoreDirectory": "C:\\Users\\Developer\\AppData\\Local\\CrestronHomeDevTools\\PrivateStore",
  "EntryName": "mail",
  "Replace": false
}
```

```powershell
.\CrestronHomeDevTools.Console.exe credentials provision-remote --name mail --windows-entry worker-login --destination C:\Private\worker-destination.json
```

The source selects exactly `mail`. It verifies the saved Windows login's host, port and SSH fingerprint against this destination, then pins the actual SSH connection to that fingerprint. Entry bytes travel only in the encrypted SSH channel to the receiver's standard input. The receiver applies its own store encryption; no plaintext transfer file, shared DPAPI key, password argument or password log is produced. A private signature can be transferred the same way by explicitly selecting its name, without authorizing signing.

The public C# API is `DevToolsPrivateStore.ProvisionRemoteAsync(name, destination, windowsCredential, cancellationToken)`. Its receiver is `ReceiveAsync`, exposed for protected-stream integrations. `credentials receive` is the destination command used by the transfer; developers do not need to invoke it by hand. The transfer is bounded and never automatically retried. If the response is lost, import may already have succeeded: inspect the destination before deciding whether to set `Replace` to true. A saved credential still grants no approval to send mail, sign a form or operate a processor.

Validation includes a real two-computer Windows SSH rehearsal with a synthetic credential, destination decryption and automatic removal of the temporary rehearsal folder. This validates user-store transfer, not every service identity or network configuration; verify the intended service account separately before provisioning production credentials.

## Signature and processor entries

```powershell
.\CrestronHomeDevTools.Console.exe credentials signature --name signature --file C:\Private\Signature.jpg
.\CrestronHomeDevTools.Console.exe credentials configure --name processor --kind Processor
```

These entries are available through the public `LoadSignature` and `LoadCredential` APIs. Their creation does not automatically migrate existing processor profiles or replace the signing command's current private image input. Those command integrations are still pending. A signature must only be applied after approval of the exact declaration being signed; saving it is not standing signing authorization. Clear returned signature bytes after use.

## Inventory and workstation setup

The public `DevToolsResourceInventory` API represents any number of processors and Windows computers with permitted roles, named credential references and capability evidence. A planned resource cannot be selected. Selecting among several matching resources requires a name. Inventory selection does not acquire an execution lease or authorize an operation.

Use [Windows resource assessment](WindowsResources.md) for the new read-only inspection and selection commands. Prerequisite installation is still pending. A logged-in Windows desktop must not be confused with an unlocked, usable desktop. Desktop automation needs verification after remote-control disconnects and Windows restarts; background monitoring and builds do not establish that capability. A physical monitor and the full Visual Studio IDE are not prerequisites in the inventory model. Emulator acceleration and actual workload suitability still require checks on the chosen computer.
