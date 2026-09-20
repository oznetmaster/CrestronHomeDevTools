# CrestronHomeDevTools 1.16.3

Dependency-notice preparation now accepts merged DLL filenames containing spaces. Previously, a valid filename such as `Example Library.dll` failed validation before its reviewed notices could be staged.

The inventory still requires plain filenames and exact dependency hashes. Paths, control characters, missing or additional DLLs, and changed packaged notice text remain rejected. Regression tests cover staging and package verification with a spaced filename, including rejection after the dependency changes.

See [reviewed dependency notices](docs/submission/DependencyNotices.md) for the inventory and packaging workflow. This fixes desktop packaging tools; it does not change driver runtime behavior.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
