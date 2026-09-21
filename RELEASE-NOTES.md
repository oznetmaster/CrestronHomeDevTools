# CrestronHomeDevTools 1.17.3

The supplied GitHub workflow templates now support signing and preparing delivery of a submission with explicitly declared gaps. Previously, the review template rejected that signing mode even though the public console supported it, and the dispatcher did not expose its required inputs.

The dispatcher accepts an explicit review mode and independently reviewed declarations digest. Declared-gap delivery preparation verifies the exact signed-review plan and separate correspondence approval; final delivery still revalidates all evidence before upload and email. Complete mode remains the default. No omitted test becomes a pass, and the workflow does not determine whether Crestron will accept a submission.

Copy the matching updated templates together and follow [the declared-gap workflow sequence](docs/submission/WorkflowSetup.md#signed-submissions-with-declared-gaps). The console commands already existed; this patch changes the distributed templates and documentation. It also distinguishes the remote observer's credential input from the combined notification command, with an optional named-credential alternative.

Validation exercises the actual complete and declared-gap template sequences against the packaged console, including rejected mismatched pins, synthetic signatures and simulated upload/email providers. Dispatcher contract checks also pass. These checks do not validate a particular developer's GitHub permissions, service account or real provider delivery. No driver runtime changes or active-monitor upgrades are required.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
