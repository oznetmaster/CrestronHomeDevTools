# Compare a package with processor files

Available in version 1.9.0. It supports checking an unchanged candidate before later tests without deploying another version.

Using a saved private processor profile:

```powershell
CrestronHomeDevTools.Console.exe compare-payload --profile development --package "C:\Candidates\Example.pkg" --package-sha256 YOUR_TRUSTED_PACKAGE_SHA256 --driver chdriver.manufacturer.model.control.developer.1.002.0003.0000
```

Use the exact catalogue ID reported by `drivers`. Obtain the expected SHA-256 from the independently retained build or release receipt. A hash computed from an untrusted replacement file does not establish that it is the intended candidate.

The command acquires the same processor reservation used by the deployment and test tools. It refuses a busy processor, including one reserved for endurance monitoring. It uses the saved SSH host-key pin and an SFTP connection; it does not open a configuration-management session, install, reload, update, or delete driver files. Temporary reservation files are its only processor writes.

The local package remains open against replacement while its identity, file inventory and file contents are checked. From version 1.13.1, the checker resolves a full `chdriver.<driver key>.<four-part version>` catalogue ID to `/user/Data/UsedThirdPartyDrivers/<driver key>/<package version>` and rejects a catalogue version that differs from the candidate. Existing callers supplying the unversioned storage key remain supported. Earlier releases incorrectly treated the full catalogue ID as a folder name.

The comparison requires all candidate files to exist with exactly matching names, sizes and SHA-256 digests, and refuses extra files, symbolic links, unsafe paths and ambiguous names. Empty directories are not compared. File counts and expanded sizes are bounded. Default comparison timeout is 120 seconds; `--timeout` permits up to 600 seconds. Reservation acquisition and final cleanup have their own bounded waits.

Standard output is a JSON receipt containing package identity, candidate hash, catalogue ID, inspected directory, per-file lengths and hashes, and observation time. It contains no file contents or credentials. Preserve it privately: file paths and catalogue identifiers may still describe the installation. Exit zero means comparison and reservation release completed. Always check the exit code before treating emitted JSON as success; a final reservation-release error returns a nonzero code. A mismatch returns 1, invalid arguments 2, a busy processor or unconfirmed cleanup 3, and cancellation 130.

## What this establishes

The receipt is an observation of extracted files. It does **not** establish which installed device uses that directory, attest a running process's memory, or prove that the driver remained loaded continuously. A caller testing an installed driver must separately check its explicitly selected device ID, model, name, room, version and loaded state before and after execution, and retain the trusted association between that instance and the candidate. Endurance monitoring also needs its independent driver-lifetime observation.

The reservation coordinates cooperating tools. It cannot prevent an administrator or unrelated software from changing files; SFTP is not an atomic filesystem snapshot. A failed check never repairs or redeploys the candidate. Inspect the mismatch and retained evidence before deciding how to proceed.

## C# use inside an existing reservation

`DriverPayloadInspection.CompareAsync` performs the same bounded file comparison and returns `DriverPayloadMatch`. The caller supplies the host, `NetworkCredential`, verified SSH fingerprint, package path, trusted package hash, catalogue ID, timeout and cancellation token. This API deliberately does not acquire or release a reservation: a larger workflow must hold its existing reservation throughout instance verification, comparison and tests. Do not invoke the standalone console command from a workflow that already owns the processor reservation.

The implementation uses read-only SFTP operations. Offline tests cover exact matches; altered, absent and unexpected files; case differences; unsafe paths and links; growing/truncated streams; cancellation; and argument rejection before profile or connection access. Hardware validation of this new shared command remains pending.
