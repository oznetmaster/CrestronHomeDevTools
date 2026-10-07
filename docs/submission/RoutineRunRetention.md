# Routine run retention

Routine retention keeps the current generation and one previous closed generation per driver in a shared private workflow root. Submitted records, prepared reviews, explicit pins, active work and their transitive evidence dependencies have separate lifetimes. A resume keeps its existing checkpoint and baseline; it does not rotate generations.

## New-run startup

`SubmissionWorkflow.Open` registers a genuinely new run in the private root's `.retention` catalogue and rotates eligible older generations. Sequence numbers determine age, not file modification times. `SubmissionReleaseIntake` checks pruning records before downloading candidate files for an old release. An old release event cannot silently recreate a pruned run, and a registered run with a missing checkpoint cannot be recreated.

The configured root may be a trusted Windows mount. Links inside the owned run or metadata tree are rejected. Cleanup takes both intake and run locks, checks the original closed checkpoint and closure record, and retains an outside-run pruning record before removing files. Interrupted removal resumes from that record. An unexpectedly recreated directory after completed removal is preserved and reported.

## Closing and protecting evidence

The owner calls `SubmissionRunArchive.Close(root, release, verifyQuiescent)` when it intentionally retires a run. The callback must verify actual worker termination, restoration and resource release; a stale worker status or failed test alone is insufficient. Running and waiting operations cannot be closed. Closure prevents subsequent execution, recovery or test-boundary migration, but permits historical reads while the archive exists.

Closing does not automatically mark a test passed. A closed routine run whose tests finished but whose document stage never began uses the routine archive slot. A prepared review or submitted packet is protected. Incomplete runs remain available for repair until their owner deliberately closes them.

Before referencing another generation's evidence, use `SubmissionRunArchive.Protect(root, release, dependencyKeys)`; use `pin: true` for an independent retention pin. Dependencies must be registered, present and not pruned. Transitive dependencies remain protected. Evidence copied wholly into the current run does not require a reference to its former storage location. Different private roots have independent catalogues; externally referenced evidence must remain separately retained or be copied into the owning run before its source is retired.

## Existing runs and verification scope

Existing unregistered evidence is never adopted for deletion. The current Kasa/Tapo rehearsal has not been closed, moved, pruned or restarted by this change. Automatic closure of a failed worker is deliberately not inferred from a status file. The normal owner must supply the verified retirement decision.

The implementation has software tests for rotation, resume, protected dependencies, path redirection, lock contention, duplicate release events and interrupted cleanup. This does not establish live rehearsal completion or Crestron acceptance.
