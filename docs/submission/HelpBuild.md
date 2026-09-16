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

## Source tools

The source-only Python tools are in `tools/submission`; they are not yet included in the published CLI/NuGet package. Install their pinned Python dependencies from `tools/submission/requirements.txt`. No Word/COM automation or Crestron SDK assemblies are used. The original official DOCX is supplied locally, not redistributed with these tools.

```text
python tools/submission/build_help.py --template OFFICIAL.docx --template-sha256 PINNED_SHA256 --content help-content.json --output Driver.review.docx --draft
python tools/submission/render_help.py --docx Driver.review.docx --docx-sha256 BUILD_REPORT_SHA256 --soffice ABSOLUTE_LIBREOFFICE_EXECUTABLE --output-directory pdf-output
```

`BUILD_REPORT_SHA256` is the `docxSha256` from the first command's JSON output, retained by the build job. Use a fresh output path/directory. Existing documents are never overwritten. The builder checks the template digest and section layout, preserves unchanged ZIP parts byte-for-byte, and supports prose, bullets, headings and PNG figures. The renderer uses an isolated temporary LibreOffice profile, suppresses desktop windows, bounds execution time, stops its renderer process tree on timeout and rejects missing, encrypted, attachment-bearing or text-incomplete PDFs. Its report records the PDF hash, renderer version and page count. Visual inspection remains necessary; source text extraction alone cannot prove that a page is free of clipping or that an image is correct.

The content format has `schemaVersion: 1`, `title`, public `author`, a four-component `version`, `pending` review items, declared `uiPages`, and `sections`. Required section keys, in official order, are `driver`, `notes`, `requirements`, `installation`, `experience`, `limitations`, `features`, `environment`, `models`, `contact`, `history`, and `license`. Each section contains a nonempty block array. Text blocks have `kind` (`paragraph`, `bullet`, `heading2`, or `heading3`) and `text`. Text is inserted as text, not interpreted as markup. Document properties use the supplied author/title and remove the template's stale dates and page/word counts.

An image block in `experience` has `kind: "image"`, `pageId`, `path`, `sha256`, and `caption`. The `pageId` must uniquely match a declared UI page. The PNG is loaded relative to the content file, its approved digest is checked, and its original bytes are embedded. It is scaled proportionally to fit a 5.5-inch figure box and followed by its caption. Paths cannot escape the content directory. Approve/redact images before recording their digests; the builder is not a redaction tool.

Without `--draft`, unresolved `pending` items or declared UI pages without figures fail the build. A draft requires a `.review.docx` name, carries a visible review label and lists the unresolved items. A final-mode build only establishes that the declared content is complete; it does not independently prove the accuracy of firmware/model claims, completeness of the declared page inventory or test evidence. Those facts still need the submission policy and review gates.

`python tools/submission/run_tests.py` runs all discovered offline document tests and rejects empty/skipped runs. The separate `Submission help builder` GitHub workflow runs these tests without a processor, emulator, signature or proprietary SDK template. The workflow has been prepared locally; hosted execution has not yet been validated.

The Wiser review draft was generated from its public content source and rendered by these tools. All four PDF pages were visually checked and matched the canonical document renderer pixel-for-pixel. Only `word/document.xml` and the explicitly updated document-property parts changed; the original template and all other parts were preserved. The draft remains incomplete and is not included in a driver package.

## Remaining integration

Next, complete the driver-specific public content and approved UI figures, verify accurate licensing and support details, and pin the CI renderer/toolchain/fonts. Connect the builder and renderer to each driver's package build before candidate creation. Generation and local rendering are implemented; release packaging and hosted rendering integration remain pending.

The first content profile will be Wiser Heat. Its driver license is MIT with the Commons Clause, unlike DevTools' MIT license; the generated help must preserve that distinction. Public support is `support@marvelous.com`. The separate private submission correspondence address does not belong in help or driver metadata. Exact supported models, minimum firmware, screenshots and candidate-specific test environment must be supported by evidence, not inferred from this renderer check.

See the [submission plan](../CrestronSubmission.md) for the remaining help, evidence, signing and delivery work.
