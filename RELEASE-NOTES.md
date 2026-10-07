# CrestronHomeDevTools 1.26.1

Final test assessment now accepts uppercase or lowercase SHA-256 digests from evidence producers. Previously, unchanged endurance samples could be rejected solely because their recorded hexadecimal digest used uppercase letters. The verifier still rejects changed evidence and preserves original producer records.

Regression coverage exercises finalization and recovery with uppercase digests, rejection of altered evidence, and the separation between tests and document preparation. No driver or device protocol changes are included.

Requires Crestron Home NUnit 2.3.0.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
