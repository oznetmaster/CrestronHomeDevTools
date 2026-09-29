# CrestronHomeDevTools 1.23.0

Manual app tests can now wait overnight for an operator without starting a timed physical action or holding a test host, processor reservation or app reservation.

- New ordered `InstalledAppSteps`, `PreEnduranceAppSteps` and `PostEnduranceAppSteps` bind every selected test exactly once. Each manual step contains one test and explicit readiness instructions. Pending readiness and completed-step evidence survive worker restarts; already attempted hardware tests are never automatically replayed.
- The desktop readiness window offers **I'm ready**, **Do this later** and **Cannot perform this action**. Readiness has no expiry. Cannot perform requires an explanation, retained with the exact request and step evidence. The public CLI supports the same explanation through `--reason`.
- After readiness, the worker prepares recording and the fixture issues its precise timed action prompt. An active failure pauses before the next test. A readiness acknowledgement, an inability explanation or a timeout never becomes a pass, an N/A decision or permission to sign or deliver.
- Action windows are centred and foregrounded, with instructions before diagnostic identifiers. Existing frozen runs retain their original prompt behaviour and evidence; adopting ordered steps requires a new plan and updated review evidence paths.

Validation: all 1,739 discovered offline tests passed, including overnight readiness, cancellation/restart, saved explanations, exact test coverage, source-change rejection, no replay after failure, and separate-processor phase recovery. The desktop application builds without warnings. A labelled local synthetic readiness request survived a listener restart and received a real desktop response. These checks do not establish a successful physical-device rehearsal or certification.

See https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/OperatorSteps.md for setup, exact plan fields, evidence layout and operator controls.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
