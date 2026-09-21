# CrestronHomeDevTools 1.17.1

Fix an unhandled exception in passive Windows endurance observation. When the task-status reader failed or returned malformed output, its `InvalidDataException` escaped the observer and could leave a Windows application-error dialog. The observer now returns a fresh, run-bound `observer-query-failed` attention report and exit code 3. It does not reuse old healthy status, retry the query, restart a collector or contact the processor.

The console also handles otherwise uncaught invalid-data errors with a concise nonzero result. Regression coverage includes the actual local observer command, failed and malformed snapshot parsing, and delivery of an attention report through a simulated notifier. An independent synthetic reproduction of the original failure now terminates normally.

The included GitHub signing and delivery workflow examples now support the named encrypted inputs introduced in 1.17.0. The optional GitHub environment variable `CRESTRON_SUBMISSION_CREDENTIAL_BINDINGS` selects this saved-input mode in the supplied workflow templates. Set it to the absolute path of the private bindings file on the runner; it is not required by DevTools itself. The variable contains only a file path, never passwords, tokens or signature data; the bindings select entries in the local encrypted store. Direct CLI callers can pass the path with `--credentials` instead. Existing image-file and protected-stdin inputs remain supported. Mixed delivery credential sources are refused. See [workflow setup](docs/submission/WorkflowSetup.md) and [private inputs](docs/PrivateInputs.md).

Update the independent observer installation after stopping and inspecting any failed observer invocation. Keep a running collector on its existing pinned bundle; this fix does not require restarting its endurance period. Windows task permissions, service credential access and provider delivery still need validation in the consuming environment.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.
