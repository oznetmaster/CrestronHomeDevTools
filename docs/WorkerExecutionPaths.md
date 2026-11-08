# Worker file access and private terminal evidence

`WorkerExecutionPaths` is a file contract used by an existing worker. It does not
launch a process, connect to a processor, acquire a reservation, grant readiness,
or establish a test result. Choose the intended host/account and its existing
authenticated transport before constructing it.

Declare each immutable input, including nested credential and restoration inputs,
with its host role and SHA-256; retain its expected byte length when supplied by
the producing manifest. Worker inputs must be below the explicit local worker
root. Controller inputs must be below the explicit UNC share root. Controller
response directories and cancellation signals have separate dynamic read roles;
their existing scoped publication protocol still validates content and freshness.
The worker never writes those channels through this contract.
Pinned worker tool or private-store files may declare a separate explicit local
`WorkerInputRoot`. This read-only exception still requires the file digest; it
never permits outputs outside the mutable worker root or changes store access.

Declare every mutable output as a new worker-local file or directory. Readable
UNC source is not writable output storage. `ReadOnlyCapture` binds host-before,
host-after, the complete capture directory, and a private terminal file to one
worker-local run directory. Acquisition/release records, inventory, locations,
catalogue, detail files, final inventory and result/failure records all belong
below that declared capture directory. No capture output belongs on the
controller share. The existing authenticated file-collection route retrieves them
after the operation; it does not need a new remote launcher or share permission.

Before resource acquisition, call `InspectAccess` **on the actual worker**. It
verifies host identity, layout and all immutable inputs; dynamic read channels
must be accessible. It refuses existing outputs and checks each nearest existing
output parent with a unique create/write/flush/readback/delete probe. It does not
change ACLs or create the future result tree. This is an access observation, not
a reservation or a promise that later IO cannot fail. Nested output parents may
inherit permissions when created. Later writes remain fail-closed and CreateNew.
The caller must verify account/SID and any native/resource identities separately.

Use `WriteJson` for private originals. Retain the primary exception and separate
cleanup exceptions with `RetainTerminal`; it records the actual calling worker
PID/start identity, time, phase, exit code and full private chains. The returned
object keeps those diagnostics if saving the terminal itself fails. Never print
private chains or decrypted inputs in public output. A successful terminal write,
exit zero or stopped caller is not independent cleanup/release proof. Active
redirected streams are not forced open; the established owning wrapper retains
their originals after settlement.

`CollectionManifest` hashes every produced file in declared output directories
and reports missing declared files explicitly. The existing collection route must
preserve relative paths and verify downloaded bytes with `VerifyCollected`.
Completeness is required by default. An explicitly partial failed-run collection
can use `requireComplete: false`; retain its missing list and never relabel it a
complete operation or passing test. No automatic replay or overwrite is supplied.

The Overkiz consumer's `GatewayInstalledRun.InspectPathContract` closes its actual
plan/settings/profile/source/runtime/package/credential/restoration/channel/output
edges without launching the fixture. Its existing worker should call this before
acquisition; `RunAsync` also rechecks it after the reviewed machine-owner guard.
All attended physical, human and restoration waits retain their no-timeout
contract. These file APIs add no elapsed session watchdog or automatic action.

Local tests use actual temporary files and exclusive handles, plus an injected
access denial to represent an inaccessible/read-only parent. They do not change
real ACLs and do not prove HPNEIL permissions. A separately reviewed worker-only
access/write/collection check is still required before another processor attempt.
