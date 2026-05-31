# Security Policy

## Scope

This project updates locally installed Vintage Story mods by:

- Scanning a configured `Mods` directory.
- Querying the official ModDB API at `https://mods.vintagestory.at`.
- Downloading compatible updates.
- Creating and restoring backups.

This document summarizes current security controls, known residual risks, and how to report vulnerabilities.

## Security controls in place

### Network and download trust boundaries

- Download and API URLs are restricted to `https://mods.vintagestory.at`.
- Final request URIs are revalidated after redirects before data is trusted.
- Download and API requests use explicit timeout limits.
- Download and API responses are size-limited while streaming.
- Downloaded archives are validated against expected mod identity from `modinfo.json`.

### Filesystem safety and path guardrails

- Destructive operations are allowed only for directories named `Mods`.
- Writes to filesystem root paths are blocked.
- Paths are normalized and enforced to remain inside expected roots.
- Backup payloads/manifests are validated for containment before restore.
- Reparse-point (symlink/junction) checks are applied across update, backup, and restore write flows.

### Backup and restore integrity

- A backup is created before each update operation.
- Restore re-reads `backup.json` from disk and validates freshness against selected backup metadata.
- Restore verifies manifest, backup payload, and target paths before modifying files.

### Scan hardening

- Mod scan enforces `modinfo.json` size limits and zip entry count limits.
- Ambiguous zip metadata (`multiple modinfo.json`) is rejected.
- Scan paths include reparse-point protections.

### Build and CI supply chain

- GitHub Actions are pinned to immutable commit SHAs.
- SDK version is pinned in both `global.json` and workflow configuration.
- CI runs tests before publish in release workflow.
- NuGet sources are restricted to `nuget.org` via `NuGet.config`.

## Residual risks (known limitations)

These are currently accepted limits given available upstream metadata and desktop local threat model:

- **No signed release digest from ModDB install metadata:** the updater cannot fully verify cryptographic artifact authenticity without a trusted checksum/signature source from upstream.
- **Local TOCTOU race windows:** reparse-point checks are performed before file operations and cannot fully eliminate local race attacks.
- **Hardlink aliasing:** hardlinks are not represented as reparse points and can influence scan visibility in hostile local environments.

## Security reporting

If you discover a vulnerability:

1. Do **not** open a public issue with exploit details.
2. Report privately to project maintainers with:
   - Affected version/commit
   - Reproduction steps
   - Impact assessment
   - Suggested fix (if available)

If no private channel is configured yet, open a minimal public issue asking maintainers for a private disclosure contact and avoid sharing sensitive details.

## Hardening roadmap

Planned/desired future improvements:

- Support upstream release checksum/signature verification when available.
- Expand automated security regression coverage for path/redirect edge cases.
- Continue reducing local attack surface where practical for desktop workflows.
