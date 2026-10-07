# CrestronHomeDevTools 1.26.2

New automated rehearsals email the signed PDF with a download link to the selected release's exact driver package, matching the production submission email structure. The worker downloads the package anonymously and verifies its SHA-256 before connecting to SMTP. Unavailable or changed downloads stop delivery.

Standalone complete and qualified rehearsal plans also support an approved HTTPS package link. The link is included in approval and plan digests. Existing plans, completed journals and production delivery keep their previous identity and behavior; no completed submission is resent. Declared gaps and shortened-endurance disclosures remain in the signed form and correspondence.

Regression coverage includes PDF-only messages, exact package verification, redirects, invalid links, cancellation, preserved disclosures, approval changes and duplicate-send prevention. No driver, client or physical-test behavior changes.

Requires Crestron Home NUnit 2.3.0.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
