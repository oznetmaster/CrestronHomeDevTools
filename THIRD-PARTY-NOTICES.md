# Third-party notices

Original CrestronHomeDevTools code is copyright (c) 2026 Neil Colvin and distributed under the [MIT License](LICENSE). Third-party components retain their original licenses.

| Component | Version in this source | Use | License |
|---|---|---|---|
| SSH.NET | 2026.0.0 | SFTP deployment, SSH-key validation and confirmed SSH reboot | MIT; [license text](licenses/SSH.NET-LICENSE.txt) |
| Microsoft System.Security.Cryptography.ProtectedData | 10.0.9 | Library private-input stores and console Windows DPAPI profiles | MIT; [license text](licenses/Microsoft-ProtectedData-LICENSE.txt) |
| MailKit | 4.18.0 | Optional submission SMTP sending | MIT; [license text](licenses/MailKit-LICENSE.txt) |
| MimeKit | 4.18.0 | Submission MIME composition | MIT; [license text](licenses/MimeKit-LICENSE.txt) |
| BouncyCastle.Cryptography | 2.7.0 | Transitive MimeKit cryptography dependency | MIT; [license text](licenses/BouncyCastle-LICENSE.txt) |
| NUnit | 4.6.1 | Offline tests only | MIT |
| NUnit3TestAdapter | 6.3.0 | Visual Studio/VSTest discovery and execution of offline tests | MIT |
| Microsoft.NET.Test.Sdk | 18.9.0 | Offline test infrastructure | MIT |

NUnit and test tooling are not dependencies of the configuration runtime library. NuGet manages the library's SSH.NET, MailKit, ProtectedData and transitive dependencies separately. MailKit and MimeKit retain copyright of the .NET Foundation and contributors; Bouncy Castle retains copyright of Legion of the Bouncy Castle Inc. Transitive Microsoft cryptography packages retain Microsoft's MIT license. A published console carries its runtime dependencies; a self-contained console also carries .NET runtime components and must retain their notices. Microsoft packages retain Microsoft Corporation copyright. SSH.NET retains copyright of Renci, Oleg Kapeljushnik, Gert Driesen and contributors.

Processor discovery was adapted from Neil Colvin's MIT-licensed Crestron Home NUnit project. DevTools has no runtime dependency on that project.

The Windows submission console built from this source bundles an isolated CPython3.13.15 runtime (Python Software Foundation license and included component notices), lxml6.1.1 (BSD and its included libxml/libxslt notices), Pillow12.3.0 (HPND and included dependency notices), pypdf6.10.0 (BSD-3-Clause), ReportLab4.4.9 (BSD), and charset-normalizer3.5.1 (MIT). These components retain their original copyrights. The complete vendor notices are preserved under `submission-tools/runtime/LICENSE.txt` and each `submission-tools/runtime/packages/*.dist-info/licenses` directory, including transitive notices supplied by the wheels. Exact download hashes and versions are recorded in `tools/submission/runtime-lock.json` and copied into the console download. They are not dependencies of the NuGet configuration runtime library. Source-maintainer tests may separately use `tools/submission/requirements.txt`.

Official Crestron help/form templates are supplied separately and retain Crestron's rights; none is included in this source repository.

No Crestron SDK assembly, `Newtonsoft.Json.Compact.dll`, Configure Pro binary, decompiled source or captured configuration traffic is distributed by this project. Crestron and Crestron Home are trademarks or registered trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc. Its MIT license does not relicense Crestron's software or documentation.
