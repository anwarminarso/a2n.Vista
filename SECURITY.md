# Security Policy

## Supported versions

Vista is in **pre-alpha** (`0.x`). Only the latest commit on the default branch
receives security attention; there are no long-term supported releases yet.

| Version | Supported |
|---------|-----------|
| `0.x` (latest `main`) | ✅ |
| anything older | ❌ |

## Reporting a vulnerability

**Please do not open a public issue for security vulnerabilities.**

Report privately through GitHub's
[private vulnerability reporting](https://github.com/anwarminarso/a2n.Vista/security/advisories/new)
("Report a vulnerability" on the repository's **Security** tab). If that is
unavailable, contact the maintainer ([@anwarminarso](https://github.com/anwarminarso)).

Please include:

- a description of the issue and its impact,
- steps to reproduce or a proof of concept,
- affected version/commit, and
- any suggested mitigation.

## What to expect

- Acknowledgement of your report as soon as practical.
- An assessment and, if confirmed, a fix tracked through a private advisory.
- Credit for the disclosure, unless you prefer to remain anonymous.

## Scope notes

Vista is **secure-by-default** by design: Views are explicit (no auto-expose),
writes require an explicit typed DTO with a `MapWritable` whitelist, and
authorization is centralized in `IViewAuthorizer`. Note that **without a
registered authorizer the runtime fails open** (default allow) and logs a startup
warning — configure an authorizer before exposing Views in production.
