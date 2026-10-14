# Releasing

Every Pug.Sal package shares one version: `<Version>` in `Directory.Build.props`. You start a release
by pushing a tag. The release workflow then:

1. runs the completion gate, the same as CI;
2. packs every package, with symbol packages;
3. pushes them to nuget.org;
4. attaches them to a **draft** GitHub release, which you publish yourself.

## Continuous integration

`.github/workflows/ci.yml` runs on a push to any branch. It runs `build/build.ps1 -Stage test`:

- a Release build of `Pug.Sal.slnx` with `-warnaserror`;
- `tests/Pug.Sal.Tests`;
- `tests/Pug.Sal.Postgres.Tests`, against a Testcontainers PostgreSQL. Docker is present on the
  GitHub runner.

## Tags

| Tag | Builds | Publishes |
|---|---|---|
| `sal-v1.2.3` | version `1.2.3` | nuget.org, then a draft GitHub release |
| `sal-pre-v1.2.3` | version `1.2.3-pre.<yyyyMMddHHmm>` (UTC build time) | nuget.org (as a pre-release), then a draft GitHub release marked *pre-release* |

- **Version check.** The tag's `X.Y.Z` must equal `<Version>`, or the workflow fails before it
  builds anything. Bump `<Version>` in a commit, then tag that commit.
- **Pre-release ordering.** Tagging the same `X.Y.Z` again with `-pre-` gives a newer stamp. So
  pre-releases sort in build order, and before the final `X.Y.Z`.
- **Publishing runs only after the gate passes.** The push to nuget.org waits for the tests. It runs
  in the `release` GitHub environment, so you can require a reviewer to approve it. A pushed version
  cannot be deleted from nuget.org, only unlisted.
- **Re-running a failed release.** Re-running the workflow for the same tag is safe:
  - versions already on nuget.org are skipped;
  - an existing draft release has its files replaced;
  - a release you already published is never touched.

## Dry run

Go to *Actions → Release → Run workflow*. This tests and packs `<Version>-dev.<stamp>` and keeps the
packages as the `packages` workflow artifact. It publishes nothing, even when dispatched from a tag.
Use it before tagging.

## What a release contains

- One `.nupkg` and one `.snupkg` for each of these packages:
  - `Pug.Sal.Models`, `Pug.Sal.Abstractions`, `Pug.Sal`;
  - `Pug.Sal.Data.Abstractions`, `Pug.Sal.Data.SQLite`, `Pug.Sal.Data.Postgres`;
  - `Pug.Sal.Importers.Csv`;
  - `Pug.Sal.WebApi.Models`, `Pug.Sal.WebApi`, `Pug.Sal.Client`;
  - `Pug.Sal.DependencyInjection`.
- `SHA256SUMS` covering them.

The packages reference each other at that exact version.

## Bumping the version

The `pug-sal` Claude Code plugin, at `claude/pug-sal`, documents the packages and is versioned with
them (DECISIONS D24). **Every package version bump also bumps the plugin's version:**

1. In one commit, set `<Version>` in `Directory.Build.props` and `version` in
   `claude/pug-sal/.claude-plugin/plugin.json` to the same `X.Y.Z`. Update the skills under
   `claude/pug-sal/skills` in that commit too if the release changes anything they describe.
   `build.ps1`'s `identify` and `test` stages fail when the two versions differ, so CI catches a
   missed bump and a release tag cannot publish one.
2. Tag the commit as usual.
3. After the release, update the plugin's entry in the `pug-claude-plugins` marketplace
   (`NDWX/claude-code-plugins`, `.claude-plugin/marketplace.json`). Set its `version` to `X.Y.Z` and
   its source `ref` to the new tag, so users get the skills that match the packages:

   ```json
   { "name": "pug-sal",
     "source": { "source": "git-subdir", "url": "https://github.com/NDWX/Pug.Sal.git",
                 "path": "claude/pug-sal", "ref": "sal-vX.Y.Z" },
     "version": "X.Y.Z" }
   ```

Users install it with `/plugin marketplace add NDWX/claude-code-plugins`, then
`/plugin install pug-sal@pug-claude-plugins`.

## Building locally

All logic lives in `build/build.ps1`, which needs PowerShell 7. The workflows only call it, so the
same stages run on a workstation:

```sh
pwsh build/build.ps1 -Stage identify -Ref refs/tags/sal-pre-v0.1.0
pwsh build/build.ps1 -Stage test -DockerViaSg          # -DockerViaSg: run docker through 'sg docker -c'
pwsh build/build.ps1 -Stage pack -Version 0.1.0-pre.1
pwsh build/build.ps1 -Stage checksums
pwsh build/build.ps1 -Stage push -DryRun
pwsh build/build.ps1 -Stage release -Tag sal-pre-v0.1.0 -Version 0.1.0-pre.1 -Prerelease -DryRun
```

Without `-Version`, `pack` builds `<Version>-dev.<stamp>`. Output goes to `artifacts/dist`.
`push` reads the API key from `NUGET_API_KEY`, and `release` uses the `gh` CLI with `GH_TOKEN`.
`-DryRun` prints their commands instead of running them.

## One-time setup

0. **Docker Hub login for the tests.** CI logs in to Docker Hub before the tests, because anonymous
   pulls are capped per IP and GitHub's shared runners exhaust the cap. Two settings hold the
   login:
   - the `DOCKERHUB_USERNAME` repository **variable**;
   - the `DOCKERHUB_TOKEN` repository **secret**, a Docker Hub personal access token with
     read-only access to public repositories.

1. **NuGet trusted publishing.** The workflow stores no API key. Its `nuget` job exchanges its
   GitHub OIDC token for a nuget.org API key that expires after an hour (`NuGet/login`). Set it up
   once:
   - On nuget.org, under your account's **Trusted Publishing**, add a GitHub Actions policy:
     owner `NDWX`, repository `Pug.Sal`, workflow file `release.yml`, environment `release`. The
     policy's owner (you or an organization) must own the `Pug.Sal*` package IDs.
   - Add the `NUGET_USER` repository **variable**: the nuget.org profile name (not the email
     address) that owns the policy.

   Renaming `release.yml` or the `release` environment breaks the policy match, and the login
   step fails. The `NUGET_APIKEY` secret is no longer used: delete it, and revoke the key on
   nuget.org if nothing else uses it.
2. **`release` environment (optional).** GitHub creates it on first use. Add required reviewers to
   it to approve each push to nuget.org.
