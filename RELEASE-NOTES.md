# CrestronHomeDevTools 1.22.0

This release connects required physical-action tests to the release workflow before endurance, and supports sequential test phases on explicitly selected processors.

- Durable operator requests appear in a Windows desktop inbox and resume the waiting fixture when answered. A persistent listener discovers new runs for selected trusted profiles and stays quiet between requests. Operator acknowledgements never substitute for functional test results or authorize signing or delivery.
- Separate initial-processor tests use their own explicit credential bindings and trust pins, with the same candidate package and source. Main-processor credentials are not an implicit fallback. Shared worker and fixture inbox identities are checked before hardware access.
- Coordinated manual outage recording observes endpoints independently, retains interruption and recovery bounds, and preserves restoration work after cancellation. A pinned default-program uptime reader provides a program clock; it does not establish driver lifetime or completed initialization.
- Both main and separate initial phases must provide their required passing evidence before endurance starts. Rehearsals follow the same ordering. Failed, incomplete and uncertain evidence remains retained.
- The complete Windows bundle includes the operator listener and watcher installation scripts. Automation uses CrestronHomeNUnit 2.1.0, including explicit saved-Home selection and value-based Android profile comparison; tests remain on NUnit 5.0.0.

Validation: all 1,680 DevTools regression tests passed. A live desktop diagnostic delivered and acknowledged two successive synthetic requests through one persistent listener and verified cleanup of its validation tasks. A separate Android navigation diagnostic switched between two saved processor Homes, checked both saved local addresses and ports, and returned to the original Home. These diagnostics sent no device commands and are not a completed submission rehearsal. Coordinated physical outage/sensor fixtures and the full protected-stage handoff still need validation in the complete workflow.

Update only newly prepared attempts; frozen runs retain their original tools and evidence. Rehearsal prepares unsigned review material and cannot sign or deliver. Saved credentials do not grant artifact authorization. Provider-confirmed delivery is separate from Crestron acceptance.

See https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/OperatorSteps.md for physical-action prompts and the persistent listener.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
