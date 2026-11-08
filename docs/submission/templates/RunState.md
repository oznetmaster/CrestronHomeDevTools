# Current submission state — replace placeholders before use

Operator notes only; not executable settings or an authorization document.
Use one current copy. Archive old copies separately. Keep this record short and
link detailed evidence instead of pasting historical logs into it.

- Updated UTC: [timestamp]
- Driver / run identity / responsible operator: [values]
- Latest human instruction and source: [scope, message reference, date]
- Authority state: [preparation authorized / waiting for scoped approval / user-stopped]
- Stage: [intake / preparation / initial checks / endurance / final checks / review / signing / delivery / closeout / blocked]
- Candidate: [source commit, package path+SHA256, GUID/version, tool/fixture pins]
- Active operation: [none OR host, operation ID, PID/task and start identity, receipt path]
- Deadlines: [main/cleanup/endpoint UTC; next check = earlier applicable endpoint or interval; none if stopped]
- Resources: [each exact owner, last live receipt/time, held/released/unknown; never infer from age]
- Latest verified result: [scope, actual version, time and original receipt; not a claim of current health]
- Physical actions: [authorized target/method/count; dispatched count; unknown count; remaining allowance; restoration scope]
- Coverage: [register path; applicable passed / N/A / qualified / failed; exact unresolved scope]
- Blocker: [none OR specific fact/action needed, owner, first observed time and evidence]
- Next action: [one exact authorized operation, prerequisites, expected exit, failure branch OR wait for user]
- Repair budget: [fault, diagnosed change, attempts used, next escalation boundary]
- Artifacts: [latest reviewed package/help/form/annex/message paths and hashes; unsigned/signed]
- Approval: [signing/delivery pending or exact authority reference, artifact identity and expiry]
- Delivery: [not attempted OR same journal, upload verification and SMTP acceptance/uncertainty receipts]
- Restoration: [original state, remaining obligation or final verification + releases]
- Supporting handoff: [environment bindings, evidence index, historical record locations]

On user stop: set authority to user-stopped, next action to wait for user, stop
pending dispatch/wakes, and retain any outstanding cleanup/uncertainty explicitly.
Do not mark incomplete submission work complete to close this record.
