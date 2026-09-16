# Building driver help without a desktop

Crestron requires a help PDF inside each submission package with the same basename and case as the package and driver DLL. Build this PDF before assembling the release candidate; adding it after testing changes the package digest and invalidates candidate-bound evidence.

The [official Resources page](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Resources/Resources.htm) links the [driver help template](https://sdkcon78221.crestron.com/downloads/CrestronDriversHelpFileTemplate.docx). The template inspected on 16 September 2026 has SHA-256 `6c01061f86b7d3c52b602997616b5ea3663addd78fa7605992e3c3bfbf449efb`. Download it into the build workspace and verify its digest; do not commit the SDK template into this repository. An upstream revision requires inspection and an explicit pin update.

The template covers driver identification, recommendations, requirements, installation, the end-user experience, limitations, supported features, test environment, models, contacts, version history and licensing. Its end-user section requests sample screenshots of every UI page with descriptions. Generating prose alone will not complete that section. Use approved screenshots that exclude private household/device information and credentials.

## Renderer

LibreOffice supports conversion with no interactive application window. Its [command-line documentation](https://help.libreoffice.org/latest/en-GB/text/shared/guide/start_parameters.html) defines the headless mode, PDF filter and separate user-profile option. A build worker needs LibreOffice, required fonts and a PDF renderer such as Poppler; it does not need Word, BlueStacks or processor access for this document step.

Example conversion with a previously prepared help DOCX:

```text
soffice --headless --nologo --norestore -env:UserInstallation=file:///BUILD_TEMP/unique-profile --convert-to pdf:writer_pdf_Export --outdir BUILD_OUTPUT HELP_DOCUMENT.docx
```

Use `soffice.com` on Windows for console output and the platform's normal `soffice` on Linux. Replace the profile URI and paths with absolute build-local paths. Give each job a unique profile so concurrent conversions do not attach to one another. Apply a bounded process timeout and inspect the output: a successful process exit alone is insufficient. The final build wrapper must check that the expected PDF was newly produced, opens successfully, has the required content and renders correctly.

On this development machine, LibreOffice 26.8.0 was extracted into a task-local directory using Windows Installer administrative extraction (`/a`, `/qn`, `/norestart`, explicit `TARGETDIR`). This is distinct from installing the application with `/i`; it did not require an interactive desktop or a reboot. The official MSI SHA-256 was `4aa6c6e1895f4055104effcb556bd3362d20c6ad707c149543304f395ef9db95`, and its Authenticode signature validated as The Document Foundation. The [official download](https://www.libreoffice.org/download/) provides the package and checksum links; [Microsoft documents the installer options](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/msiexec).

The extracted renderer successfully converted all three pages of the unchanged official help template. All pages were visually inspected. This proves the local rendering dependency works; it does not validate a completed driver help file, a hosted runner installation or the final CI build step.

## Remaining integration

The reusable help builder must consume a driver-specific public content file, retain the template's structure, replace every example/placeholder, add approved UI figures and include accurate licensing and support details. It must pin its template, toolchain and fonts; preserve the original template; render the result; and report the final PDF digest. Each driver then packages that PDF before candidate creation.

The first content profile will be Wiser Heat. Its driver license is MIT with the Commons Clause, unlike DevTools' MIT license; the generated help must preserve that distinction. Public support is `support@marvelous.com`. The separate private submission correspondence address does not belong in help or driver metadata. Exact supported models, minimum firmware, screenshots and candidate-specific test environment must be supported by evidence, not inferred from this renderer check.

See the [submission plan](../CrestronSubmission.md) for the remaining help, evidence, signing and delivery work.
