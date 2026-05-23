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

## Recommended Release Order

1. Finish the code change.
2. Validate it locally.
3. Update version and changelog.
4. Build installers.
5. Push to GitHub.
6. Verify the release page.
