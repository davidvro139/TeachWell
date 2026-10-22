# ADR 002: Native development and audited course sharing

Accepted October 15, 2026.

## Development workflow

Per the project owner's instruction, defer Docker until the end of project creation. Keep existing deployment files for later, but remove Docker from required development and CI checks. This supersedes Phase 1's Docker requirement and ADR 001's Caddy requirement for local development.

Use native PostgreSQL 17, preserving the database architecture and EF migrations. Windows scripts can use the installed PostgreSQL bin directory or a workspace-local portable distribution. The local cluster listens only on 127.0.0.1:55432, uses SCRAM authentication, and persists under ignored `.local`. No system service is installed. The native development cluster uses a dedicated local superuser to create developer/test databases; use separate least-privilege application/migration accounts for deployment.

Local database and demo passwords are stored as Windows-user-bound DPAPI SecureStrings in ignored `.local/*.xml`. The first normal application start prompts for a demo password; no fixed password is committed or automatically printed. Do not copy these encrypted files to another user/machine. Native scripts are Windows-specific; users on other systems can supply PostgreSQL and environment configuration directly.

ASP.NET Core hosts the built SPA over its development HTTPS certificate. Secure session and CSRF cookies stay enabled. Trust the local certificate explicitly if needed; no trust-store change is automated. Existing Docker configuration is retained but not developed or verified in this phase.

## Course sharing slice

Only a course owner with Instructor role may list reviewer candidates, list existing shares, grant Review access or revoke access. CTL role and SystemAdmin role do not grant these abilities. Candidate directory is limited to active CTL reviewers in the owner's tenant and exposes names and immutable IDs only. Owners cannot share with themselves.

Existing shares are read on every course request; revocation takes effect on the next request. Information already viewed cannot be recalled. Grant and revoke are idempotent, and each actual change is saved with its audit event in one EF transaction. Audit events record actor, target reviewer, course, action and UTC time. A concurrent grant conflict yields HTTP 409 and an actionable refresh message.

The API permits Review only; no client-supplied permission string or owner change is accepted. Same-origin CSRF protection applies to POST and DELETE. This slice operates on demo/imported local data; it neither grants Canvas permissions nor contacts Canvas.

Add a nullable audit subject column using a forward EF migration. Original audit rows remain valid. Extend integration tests to optionally use PostgreSQL, applying real migrations to randomly named isolated test databases and deleting only those test databases after execution.

Phase 2 remains incomplete: encrypted PAT lifecycle, approved-host Canvas client, course listing/import, safe normalization, and authorized live validation are still pending.
