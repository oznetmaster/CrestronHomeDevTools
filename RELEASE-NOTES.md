# CrestronHomeDevTools 1.21.0

This release updates the test workflow to NUnit 5 and adds reusable outage evidence recording and optional stored GitHub authentication.

- Owned DevTools tests and the Android sample use NUnit 5.0.0 with CrestronHomeNUnit.TestAdapter 2.0.0. Existing frozen runs retain their original tools and framework pins. Updating the tools does not change an application-driver package or migrate its separate test fixtures automatically.
- A separate installed-driver fixture can run after the main app tests and before endurance. Its deployment identity, input files, retained evidence and interrupted-operation handling follow the normal workflow. Setup reports declared initial evidence gaps before a run begins.
- Outage evidence import checks the selected components, common interruption interval, recovery-clock bounds, required functional observations and original-state restoration. It retains partial and failed evidence. The recorder sequences interruption and restoration, including uncertain interruption outcomes, and gives each attempted component an independent restoration timeout.
- Outage hardware integration remains explicit: the recorder requires a trusted provider with actual control and observation bindings. A timer, open network port or operator reply is not a measured program-load event. The recorder does not make power/network tests unattended merely by being installed.
- Release intake and the unattended release watcher can select a named GitHub API credential from the encrypted private store. Authentication is optional for public repositories. Configured credentials must match `api.github.com:443`; an inaccessible selected entry requires attention instead of falling back silently. The setup form retains the optional evidence-worker binding reference without substituting the developer's signing store.
- Stored GitHub access authenticates API inspection and asset downloads. It does not configure Git checkout credentials or establish complete private-repository workflow support. Tokens are not supplied through command arguments or environment variables.
- Documentation distinguishes Android tests, experimental Apple UI tooling, iOS apps running on a Mac and actual physical-iPhone testing.

Validation: all 1,595 discovered NUnit 5 tests passed. Windows and Ubuntu each passed 266 document tests and 11 workflow-template checks. The Windows console download passed its 22 bundle checks, and the setup application built without warnings or errors. These are tooling checks; they do not establish a new hardware rehearsal or submission. The release workflow separately builds and validates the versioned artifacts before publication.

The corrected full hardware workflow and separate protected signing/delivery handoff still require their own live validation. Existing accepted drivers and earlier assisted submissions are separate evidence. Rehearsal prepares an unsigned review packet and cannot sign or deliver. Exact artifact authorization remains separate from saved credentials. Provider-confirmed delivery is not Crestron acceptance or certification.

Use the public starting document, setup guide and worker documentation matching the selected release. Preserve evidence and active runs; apply changed tooling only to a newly prepared attempt.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
