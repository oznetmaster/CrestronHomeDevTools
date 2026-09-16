# Associate an Android tile with an installed driver

`DriverNameChallenge.RunAsync` temporarily assigns a unique name to an exact installed driver, asks the caller to observe that name in the app, and restores the original name. This library API is introduced in DevTools 1.5.0. It is useful for ordinary UI tests as well as optional submission testing; it does not submit or certify a driver.

A saved processor address and a familiar tile label alone do not prove which installed instance the app is showing. A fresh unpredictable name observed through both the configuration connection and the app provides stronger association. This is an operational check, not cryptographic proof of the app's network route or package provenance.

## Required caller responsibilities

- Hold the processor reservation and Android reservation throughout the challenge, observation and cleanup. Pass an ownership callback that throws if either reservation or its coordinator is no longer valid. The helper does not acquire or release reservations.
- Establish the candidate's package/GUID/source identity independently. Supply the exact installed device ID, model and loaded version in `DriverInstanceReady`; its lifecycle action label does not authorize a mutation.
- Use an existing private evidence directory and a bounded timeout of at most two minutes. Retain that directory after failures. Names, instance IDs and screenshots can reveal local configuration.
- Explicitly opt this test into a development workflow: it changes the visible tile name temporarily. Do not run it against a production home merely because read-only testing was enabled.
- In the observation callback, require the expected Home screen, the uniquely matching tile and absence of the previous name. Capture the screenshot and hierarchy with the workflow identity. During the Challenge phase, open that exact tile, check the expected page title and perform the intended tests.
- Restore app navigation even if an assertion fails. Physical commands, if added by a fixture, require their own device-specific restoration and evidence; this helper only restores the name.

## Sequence and failure handling

The helper checks loaded identity, the advertised `deviceName:setName` command, a nonempty original name and uniqueness in the processor inventory. It records the original parent and room and refuses a change in either during the operation.

The observer runs in three phases: `Before` sees the original name, `Challenge` sees the generated name, and `Restored` sees the original name again. Each observation has an expected name and an absent name. The helper writes and flushes an immutable intent before the first rename and a separate result afterwards. It sends each name change once and polls reads for confirmation; it does not retry commands.

An assertion or cancellation still enters bounded restoration using an independent cancellation token, provided ownership remains valid. Restoration sends the original name only when the current name equals this challenge. Another name is preserved and reported as a failure. A lost reply with no observed challenge remains uncertain even if the original name is currently visible, because the request could still be outstanding. Lost ownership stops further commands. The original failure is preserved alongside cleanup or evidence-write failures.

`Passed` in the result requires the entire operation and observations to succeed. `ChallengeObserved` records the processor-side name observation; it does not mean the UI callback passed. `NameRestored` means no name mutation was attempted, or restoration and the caller's Restored observation completed. After an exception, inspect the exact operation's durable records and app restoration before releasing reservations. Missing or uncertain records must not be treated as successful restoration.

There is no automatic crash recovery or force-retry API. On process interruption, retain the reservations and intent, inspect the installed instance under a recovered reservation, and reconcile before a later workflow starts. Do not delete evidence or a lease to make an uncertain operation appear complete.

## Verified scope

On 16 September 2026, a private pilot using this source API passed against an already-installed Wiser Heat Debug gateway on a V2 MC4-R. It observed the temporary name on the processor and in the minimized Google Android emulator, opened the Wiser page, and restored the original name and Home screen. The inventory's IDs, names, models, parents and rooms were unchanged afterwards, as were the observed Hot Water and Away labels. Both reservations were released. No physical control command was sent.

The Wiser Android fixture subsequently consumed released DevTools 1.5.0 and TestAdapter 1.7.1 with optional name binding enabled. Both cases passed against the existing Debug gateway, with fourteen capture pairs verified, unchanged inventory/state, original name/Home restored and both reservations released. This verifies integration in the fixture, separate from a complete deployment workflow.

Offline tests cover successful phases, unsupported or mismatched targets, duplicate names, callback failures, cancellation, lost replies, external renames and lost ownership. These checks do not establish a complete install/update workflow using this helper, exact Release-candidate testing, other drivers, unattended Android service operation, physical control behavior or process-crash recovery. Those remain tracked in the [submission plan](CrestronSubmission.md).
