# Release Checklist

Use this before pushing a new public build to GitHub.

## 1. Decide What Is Ready

- Confirm the changes are complete and user-facing.
- If the activation tool is the main reason for the release, verify it end-to-end before publishing.
- Do not publish partial work unless you want users to receive it.

## 2. Update Versioned Files

- Bump `DungeonMasterCortex/DungeonMasterCortex.csproj`.
- Update both installer scripts:
  - `Installer/DMCortexSetup.iss`
  - `Installer/PlayerCortexSetup.iss`
- Add a matching top entry in `DungeonMasterCortex/CHANGELOG.txt`.

## 3. Build and Validate Locally

- Run the solution build.
- Rebuild both installers.
- Confirm the new versioned EXEs exist in:
  - `artifacts/releases/dm/`
  - `artifacts/releases/player/`

## 4. Activation Tool Gate

Treat this as a hard release check when activation work changed.

- Open the activation request dialog.
- Confirm the request code is generated.
- Confirm SMTP send works, or the manual fallback path copies the request and opens the mail client.
- Confirm the activation tool can generate or handle the response for the current request payload.
- Confirm the app accepts the activation response on the correct machine/edition.

## 5. GitHub Publish

- Commit the version bump, changelog, and code changes.
- Push the commit to `master`.
- Push the matching tag if the workflow uses tags.
- Confirm the GitHub Actions release workflow runs and completes.
- Confirm the release page shows the newest installers.

## 6. Post-Publish Spot Check

- Download the published installers from GitHub.
- Open the updated app and spot-check the changed screen or workflow.
- Confirm the release notes and version number match the built installers.

## 7. Release Asset Verification (Required)

- Run:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-release-assets.ps1`
- Confirm both DM and Player installer assets are present on the GitHub release.
- Confirm both size and hash checks report `YES`.
- If verification fails, rerun:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\upload-release-assets.ps1`
  and then rerun the verifier.

## 8. Updater Smoke Test (Required)

- Start from the previous installed version (do not launch from VS Code output).
- Trigger in-app update and let installer complete.
- Launch from the installed shortcut and confirm file version matches release.
- Character creation smoke checks:
  - Core Rules and Player's Option show different race behavior where expected.
  - Player's Option race/subrace abilities are selectable (not core-only automatic fallback).
  - Class ability option counts look complete for known PO-heavy classes.
- If behavior regresses, collect and attach:
  - `%LOCALAPPDATA%\\DMCortex\\logs\\rules-load.log`

## Recommended Release Order

1. Finish the code change.
2. Validate it locally.
3. Update version and changelog.
4. Build installers.
5. Push to GitHub.
6. Verify release assets (scripted hash check).
7. Run updater smoke test on installed build.
