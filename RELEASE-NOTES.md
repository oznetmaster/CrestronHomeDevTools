# CrestronHomeDevTools 1.18.2

Evidence mapping now accepts the camelCase worker files used by the endurance console, as well as retained PascalCase API worker files. Previously, mapping a completed console run could fail with a generic input error. Both forms retain their original bytes and hashes; strict schema, identity, producer and measured-result validation remain enforced. No collector restart or evidence rewrite is needed.

Successful endurance scheduler and notification checks now remove temporary diagnostics after saving current status and compact history. History rotates at 1 MiB with one previous file retained. Failed and interrupted attempts keep their original output; actual collector samples and evidence criteria are unchanged. Repeated healthy notification observations update the current checkpoint without creating duplicate event files. Delivery transitions and unresolved-send safeguards remain retained.

Completed-run retention documents disabling the exact terminal collector and watcher tasks and waiting for their current invocations to finish before copying evidence. Leave active collectors on their recorded tooling; use the new mapper separately against retained evidence.

Validation covers both worker formats without byte rewriting, original identity and evidence rejection, 1,440 successful diagnostic-retention cycles, interruption recovery and notification transitions. See [evidence mapping](docs/submission/EvidenceMapping.md) and [completed-run retention](docs/submission/WindowsEnduranceWorker.md#retain-a-completed-run).

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
