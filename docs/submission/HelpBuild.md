# Building driver help without a desktop

Command examples use the [bundled submission console](ConsoleTools.md); see that guide for source/release availability and setup.

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

Use the [bundled console](ConsoleTools.md); its internal document runtime needs no separate installation. No Word/COM automation or Crestron SDK assemblies are used. The original official DOCX is supplied locally, not redistributed with these tools.

```text
CrestronHomeDevTools.Console.exe submission build-help --template OFFICIAL.docx --template-sha256 PINNED_SHA256 --content help-content.json --output Driver.review.docx --draft
CrestronHomeDevTools.Console.exe submission render-help --docx Driver.review.docx --docx-sha256 BUILD_REPORT_SHA256 --soffice ABSOLUTE_LIBREOFFICE_EXECUTABLE --output-directory pdf-output
```

`BUILD_REPORT_SHA256` is the `docxSha256` from the first command's JSON output, retained by the build job. Use a fresh output path/directory. Existing documents are never overwritten. The builder checks the template digest and section layout, preserves unchanged ZIP parts byte-for-byte, and supports prose, bullets, headings and PNG figures. The renderer uses a short private system-temporary working directory and isolated LibreOffice profile so deep MSBuild output paths are not passed to LibreOffice. It copies only the verified result to the requested destination, suppresses desktop windows, bounds execution time, stops its renderer process tree on timeout and rejects missing, encrypted, attachment-bearing or text-incomplete PDFs. Its report records the PDF hash, renderer version and page count. Visual inspection remains necessary; source text extraction alone cannot prove that a page is free of clipping or that an image is correct.

The content format has `schemaVersion: 1`, `title`, public `author`, a four-component `version`, `pending` review items, declared `uiPages`, and `sections`. Required section keys, in official order, are `driver`, `notes`, `requirements`, `installation`, `experience`, `limitations`, `features`, `environment`, `models`, `contact`, `history`, and `license`. Each section contains a nonempty block array. Text blocks have `kind` (`paragraph`, `bullet`, `heading2`, or `heading3`) and `text`. Text is inserted as text, not interpreted as markup. Document properties use the supplied author/title and remove the template's stale dates and page/word counts.

An image block in `experience` has `kind: "image"`, `pageId`, `path`, `sha256`, and `caption`. The `pageId` must uniquely match a declared UI page. The PNG is loaded relative to the content file, its approved digest is checked, and its original bytes are embedded. It is scaled proportionally to fit a 5.5-inch figure box and followed by its caption. Paths cannot escape the content directory. Approve/redact images before recording their digests; the builder is not a redaction tool.

Without `--draft`, unresolved `pending` items or declared UI pages without figures fail the build. A draft requires a `.review.docx` name, carries a visible review label and lists the unresolved items. A final-mode build only establishes that the declared content is complete; it does not independently prove the accuracy of firmware/model claims, completeness of the declared page inventory or test evidence. Those facts still need the submission policy and review gates.

`python tools/submission/run_tests.py` runs all discovered offline document, form and MSBuild integration tests and rejects empty/skipped runs. Build the DevTools console in Release first for the [form tests](FormGeneration.md). The integration tests require the .NET SDK (`dotnet` on PATH, or `SUBMISSION_TEST_DOTNET` pointing to it); `SUBMISSION_TEST_VALIDATOR` can select an explicit built console DLL. They exercise the real build engine with a synthetic template, renderer and package compiler, plus the real offline evidence validator; they never deploy a package. The separate `Submission help builder` GitHub workflow installs Python and .NET and runs these tests without a processor, emulator, signature or proprietary SDK template. Hosted execution of those offline checks has passed. This validates the synthetic document/build tests; final driver-specific rendering and package inclusion still need their own evidence.

A development review draft was rendered and visually inspected against the pinned template. Printed layout and untouched template parts were preserved. This validates the exercised generation/rendering path, not the completeness or approval of a consuming driver's help.

## Package build integration

`submission package-help` and `CrestronSubmissionHelp.targets` connect help generation to a new-driver submission build. Set `SubmissionConsole` to the complete console executable and import the targets from its `scripts/submission` directory. The source build also retains the older explicit-interpreter route for maintainer compatibility. Do not add a modern .NET DevTools dependency to the processor driver's `net472` project.

The sequence is enforced as follows:

1. Validate submission settings before the driver's version preparation. Require Release configuration and reject options that ignore merge or ManifestUtil errors.
2. Before `CoreCompile`, compare the help's four-component version with the prepared manifest, verify the configured public support email and/or website and developer filename component, and build final-mode help in a fresh `obj/submission-help/<unique-id>` directory. Pending content or missing declared screenshots stops the build. There is no draft bypass in this path.
3. After copying ordinary `IncludeInPkg` assets, recheck source/DOCX/PDF hashes and stage the generated PDF. A competing source PDF is an error. Remove only the expected old candidate `.pkg` before invoking ManifestUtil so an earlier package cannot stand in for a failed build.
4. After ManifestUtil succeeds, normalize Windows archive separators to forward slashes. Reject traversal, colliding names, file/directory conflicts, encrypted entries and links. Preserve and recheck every payload byte before replacing this fresh build output. Record the original/final hashes and renamed entries in `package-paths.json`. Already normalized archives retain their exact bytes.
5. Read the resulting package and require exact root DLL/DAT/PDF basenames and case, the expected GUID/version/support metadata, and byte-for-byte equality with the rendered PDF. Write `packaged-help.json` with the final package, PDF and help-receipt hashes.

Keep the DOCX, PDF, `help-receipt.json`, `package-paths.json` and `packaged-help.json` as candidate build artifacts. Only the PDF goes into `IncludeInPkg`. The reports contain hashes and public driver identity, not local input paths. The trusted candidate/evidence producer must consume the recorded package digest and the same package bytes; rebuilding or modifying a package requires new evidence. These reports establish build consistency, not visual approval, authenticated test evidence or permission to sign/send.

| Property | Value |
|---|---|
| `CrestronSubmission` | `true` to opt in; ordinary builds do not require document tools |
| `SubmissionToolsDirectory` | Complete console download's `scripts/submission` directory |
| `SubmissionConsole` | Absolute path to `CrestronHomeDevTools.Console.exe`; no interpreter setting required |
| `SubmissionSoffice` | Absolute LibreOffice console executable: `soffice.com` on Windows, `soffice` on Linux |
| `SubmissionHelpTemplate` | Local copy of the official help DOCX |
| `SubmissionHelpTemplateSha256` | Reviewed template digest |
| `SubmissionHelpContent` | Driver's public content JSON; supplied by the driver project |
| `SubmissionDeveloperToken` | Approved developer filename component |
| `SubmissionDependencyNotices` | Optional reviewed merge inventory supplied by the driver. See [dependency notices](DependencyNotices.md). |
| `SubmissionSupportEmail` / `SubmissionSupportWebsite` | Approved public contact; supply one or both. Website support requires the 1.6.0 source tools or later. |

Paths are build-local settings, not values to commit to a driver repository. Use explicit arguments, environment-backed properties or a privately excluded local targets file. `BuildForTests=true` and design-time builds skip the integration. A submission build must not automatically deploy before its package checks complete.

To add another driver, import `CrestronSubmissionHelp.targets` only when explicitly selected, reject a missing import before the version bump, call `StageSubmissionHelp` immediately after the asset copy and before ManifestUtil, and call `VerifySubmissionHelp` immediately after ManifestUtil. Merely attaching a staging target with `BeforeTargets="PackageDriver"` is incorrect if `PackageDriver` then clears `IncludeInPkg`. Preserve the driver's GUID and prepare a new candidate version before eventual hardware testing. The current helper enforces the new-submission developer filename rule; adapting it for an existing portal driver's naming exemption remains separate work.

A consuming project evaluation rejected incomplete content before compilation or manifest-version changes, while its ordinary test build remained usable. Offline MSBuild fixtures verify preparation/staging/packaging order, fresh receipts, rejected packaging errors and ordinary/test/design-time compatibility. A final Release package built with actual ManifestUtil remains to be verified.

## Remaining integration

Next, complete the driver-specific public content and approved UI figures, verify accurate licensing and support details, and pin the CI renderer/toolchain/fonts. Validate a complete Release candidate with the actual ManifestUtil and connect its retained receipts to the trusted candidate/evidence producer. Validate the hooks for each consuming build layout. Hosted rendering and the final submission CI job remain pending; ordinary publication is unaffected.

The help must preserve the consuming driver's actual license and third-party notices; do not assume they match DevTools' MIT license. Public support may use a repository and issue tracker. Keep private submission correspondence out of public help and metadata. Supported models, firmware, figures and candidate test environments require evidence, not inference from renderer checks.

See the [submission plan](../CrestronSubmission.md) for the remaining help, evidence, signing and delivery work.

### Archive path correction in 1.6.0

The source checkout now includes `normalize_package.py` in the opt-in packaging hook. It corrects the raw backslash names emitted by ManifestUtil before the final candidate hash and any hardware tests. Use the 1.6.0 tag or a later reviewed source revision containing the script and targets together. Do not run it on an already published or tested candidate; changed archive bytes require a new candidate and new evidence. Each build must own its output directory exclusively.

Archive normalization was checked on a private copy of an actual package: payload bytes were preserved, separator defects were corrected and original release bytes were unchanged. That structural correction does not fix missing submission metadata or help. Offline MSBuild integration also checks normalization order and rejects collisions before successful packaged-help evidence is emitted.
