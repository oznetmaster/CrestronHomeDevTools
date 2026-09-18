# Remote processor logging

Remote syslog can supplement saved processor logs during development and endurance monitoring. This is a processor console facility, separate from the Home configuration-management API. DevTools does not currently provide a syslog collector or configure one automatically.

## Inspect before configuring

Use a trusted SSH console or Crestron Toolbox. These console queries do not change settings:

```text
remotesyslog
remotesyslog ?
```

The first reports the current configuration; the second displayed command syntax on the tested CP4-R running Crestron Home. `help remotesyslog` returned an unknown-parameter response on that processor, so that response alone does not establish that the command is unavailable. Both successful queries were verified on 18 September 2026; remote logging was left disabled.

The current [4-Series Message Logging manual](https://docs.crestron.com/en-us/8559/Content/Topics/Reference/Message-Logging.htm) documents these options:

| Option | Purpose |
| --- | --- |
| `-S:ON` / `-S:OFF` | Enable or disable forwarding |
| `-I:HOST` and `-P:PORT` | Collector address and listening port |
| `-T:TCP`, `-T:UDP`, `-T:SSL` | Transport |
| `-E:LEVEL` | Minimum severity: OK, INFO, NOTICE, WARNING, ERROR or FATAL |
| `-V:ON` / `-V:OFF` | Server verification when using SSL |
| `-A` | Include audit records when audit logging is enabled separately |

The tested processor's own help listed all three transports. The [Toolbox Syslog help](https://help.crestron.com/toolbox/Content/System_Info/Functions/Syslog.htm) describes System Info > Syslog and includes a TLS-server requirement. The current 4-Series manual makes that requirement conditional on TLS being selected. Match the collector to the actual firmware settings; neither page proves successful delivery in a particular installation.

## Validate a collector

Before enabling forwarding, retain the original processor settings and prepare a private collector with bounded retention and disk monitoring. For TLS, configure the applicable certificates and verify the server. Keep audit records and credentials out of public CI artifacts.

Verify an identifiable fresh event at the collector, including its processor identity, timestamp and severity. Separately establish whether the required Home application and driver diagnostics are forwarded: system syslog, the persistent error log and Home diagnostic files are not proven interchangeable by the existence of this command. Test collector interruption/reconnection and host restart before relying on unattended collection. Preserve missing intervals and report collection failures.

A remote collector does not establish that local log limits are bypassed, and an empty collection is not proof of a healthy or quiet driver. Continue independent functional observations from the [endurance worker](submission/WindowsEnduranceWorker.md). Reduce excessive repeated driver diagnostics at their source even when remote retention is available.

Only command availability, help and the settings query have been validated here. Collector delivery, diagnostic coverage and unattended recovery remain to be demonstrated; this guide does not claim completed endurance evidence.
