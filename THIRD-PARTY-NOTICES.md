# Third-party notices

Original CrestronHomeDevTools code is copyright (c) 2026 Neil Colvin and distributed under the [MIT License](LICENSE). Third-party components retain their original licenses.

| Component | Version in this source | Use | License |
|---|---|---|---|
| SSH.NET | 2026.0.0 | SFTP deployment, SSH-key validation and confirmed SSH reboot | MIT; [license text](licenses/SSH.NET-LICENSE.txt) |
| Microsoft System.Security.Cryptography.ProtectedData | 10.0.9 | Console Windows DPAPI profiles | MIT; [license text](licenses/Microsoft-ProtectedData-LICENSE.txt) |
| NUnit | 4.6.1 | Offline tests only | MIT |
| NUnit3TestAdapter | 6.3.0 | Visual Studio/VSTest discovery and execution of offline tests | MIT |
| Microsoft.NET.Test.Sdk | 18.9.0 | Offline test infrastructure | MIT |

NUnit and test tooling are not dependencies of the configuration runtime library. NuGet manages the library's SSH.NET dependency separately. A published console carries its runtime dependencies; a self-contained console also carries .NET runtime components and must retain their notices. Microsoft packages retain Microsoft Corporation copyright. SSH.NET retains copyright of Renci, Oleg Kapeljushnik, Gert Driesen and contributors.

Processor discovery was adapted from Neil Colvin's MIT-licensed Crestron Home NUnit project. DevTools has no runtime dependency on that project.

No Crestron SDK assembly, `Newtonsoft.Json.Compact.dll`, Configure Pro binary, decompiled source or captured configuration traffic is distributed by this project. Crestron and Crestron Home are trademarks or registered trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc. Its MIT license does not relicense Crestron's software or documentation.
