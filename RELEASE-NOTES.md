# CrestronHomeDevTools 1.23.1

When an operator chooses **I'm ready**, a failed driver or app-screen preflight now produces a retained, actionable explanation that the physical test never started. Subsequent observations preserve that explanation instead of replacing it with an uncertain test outcome. No failed test or preparation is automatically replayed.

The acknowledged readiness window closes so it cannot cover the next recording prompt. Workflow attention notices come to the foreground and explain known preflight failures in plain language.

Validation: 122 focused offline app workflow and recovery tests passed, including retained preflight failures and no replay. The release workflow runs the complete offline suite and package validation before publication. These checks do not establish a completed hardware rehearsal. Existing frozen attempts and their original outcomes remain unchanged.

See https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/OperatorSteps.md for operator readiness, physical action prompts and retained evidence.

Copyright (c) 2026 Neil Colvin. MIT licensed. Crestron and Crestron Home are trademarks of Crestron Electronics, Inc. This project is independent and is not affiliated with, endorsed by or sponsored by Crestron Electronics, Inc.