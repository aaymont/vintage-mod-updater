# Release Process (Maintainers)

This repository includes a GitHub Actions workflow at `.github/workflows/release.yml`.

To publish a release:

1. Update the app version if needed.
2. Commit your changes.
3. Create and push a version tag:

```bash
git tag v0.4.0
git push origin v0.4.0
```

GitHub Actions will:

- Restore and build the app.
- Publish self-contained builds for Windows, Linux, macOS Intel, and macOS Apple Silicon.
- Package the builds as downloadable archives.
- Create a GitHub Release.
- Generate release notes and a changelog from GitHub history.

Tags that include a prerelease suffix, such as `v0.4.0-beta.1`, are marked as prereleases.
