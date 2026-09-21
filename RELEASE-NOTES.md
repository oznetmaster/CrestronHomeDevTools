# CrestronHomeDevTools 1.17.5

Self-test review PDFs now retain the explanations attached to verified passing observations. Previously, checked items could omit important context such as whether configuration was verified through an API or by visually inspecting a dialog. The numbered notes now include that context in both complete and declared-gaps reviews, printing repeated identical explanations once per official item.

Checkbox decisions, evidence validation and signing/delivery approvals are unchanged. A claimed pass that fails verification remains incomplete and its explanation is not presented as a verified observation. Existing signed forms are not modified. Review any new form and its explanations before authorizing it.

Regression tests reproduce the missing text in generated PDFs, then verify its preservation, deduplication and unchanged checkbox values. They also confirm that an insufficient-duration claim stays unchecked and is not presented as verified evidence. See [form generation](docs/submission/FormGeneration.md) and [declared-gaps reviews](docs/submission/DeclaredGaps.md).

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
