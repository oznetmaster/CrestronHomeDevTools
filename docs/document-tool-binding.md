# Phase-three document tools

The release and test settings remain frozen after phase two. Document preparation can use a separately selected, complete console installation through an explicit immutable binding. This permits an updated generic document tool to consume completed tests without rebuilding the driver, changing criteria or re-running equipment tests.

Use `--bind-review-tools --settings FILE --settings-sha256 PIN --state-sha256 INSPECTED_PIN --tool-plan FILE --tool-plan-sha256 PIN`. The version-one tool plan contains `Console` (absolute installation directory and every relative file/digest) and `Reason`. The command holds the run lock, verifies the completed final-test boundary, requires a ready PrepareReview checkpoint with no document/sign/delivery activity, and validates the entire console installation. It does not execute the console, prepare documents, sign or deliver anything.

The append-only `document-tooling.json` binds the original console configuration, frozen settings digest, release input identity and final-test receipt to the selected tools. The same request is idempotent; a different request cannot overwrite it. Every subsequent document invocation revalidates the exact tool files. The portable input publication and retained review receipt pin this binding as provenance. The original settings and every test receipt remain unchanged.

This affects only evidence-worker document preparation. It does not grant signing or delivery authority. The protected worker continues to use its independently installed configuration and exact document/delivery approvals.
