# Portable review inputs

The generic submission preparation commands consume retained evidence without accessing a processor or rebuilding a driver. The normal `submission prepare-review` command first retains an input snapshot beside its output (`<output>.inputs`) and prepares the form from those bytes. The same snapshot can be copied to another private directory or machine for phase three.

To retain inputs separately:

~~~text
CrestronHomeDevTools.Console submission freeze-review-inputs --settings PRIVATE_SETTINGS --output NEW_PRIVATE_DIRECTORY --candidate-sha256 CANDIDATE_SHA --inventory-sha256 INVENTORY_SHA --mapping-sha256 MAPPING_SHA --source-commit COMMIT
~~~

Use the existing pinned Android and declared-gap arguments when applicable. Retain the returned `inputsSha256` independently in the trusted job or local run record. This is a file inventory hash, not producer authentication or test approval.

Copy the complete directory, including `COMPLETE`, then prepare the unsigned signing copy:

~~~text
CrestronHomeDevTools.Console submission prepare-frozen-review --inputs COPIED_PRIVATE_DIRECTORY --inputs-sha256 RETAINED_SHA --output NEW_REVIEW_DIRECTORY --prepare-for-signing
~~~

Only the installed console is needed. The manifest contains relative input paths and no compiler, validator, credential or source-checkout binding. Existing tool validation chooses the installed validator. The command verifies every inventoried file and rejects unexpected files, missing completion, links, changed evidence, and output inside the input snapshot. A partially published snapshot remains incomplete and cannot be reused automatically.

Original observation documents and their referenced evidence are copied byte for byte. Independently pinned Android runs are audited, and every raw file read by that audit is retained, including the complete producer inventory, discovery and selected test results. The relocated inputs undergo the same raw audit and submission-policy checks during review. Unreferenced files from the worker directory are not copied. Keep snapshots private: evidence can contain internal device names and paths.

Freezing does not run tests, convert outcomes, waive requirements, authenticate a producer, sign, or send mail. Failed, omitted or insufficient evidence still prevents a complete review. Approved declared-gap review remains a separate, explicit mode. Signing and delivery retain their existing approval and revalidation requirements.

The controller still assembles initial review settings from its completed producer receipts. Exposing every phase-two fixture through the same NUnit suite in IDE and CI remains separate work; this handoff does not claim that migration is complete.
