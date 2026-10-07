# How the repo stays honest

The CI workflows that keep the README and the guides re-proven rather than taken on trust.

Back to the [README](../../README.md).

Five GitHub Actions workflows run on GitHub-hosted runners, so what the README and these guides claim is re-proven
by CI rather than taken on trust, and every release is cut from a green run:

- **[`ci.yaml`](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/.github/workflows/ci.yaml)** — builds and tests the solution on every push to `main`
  and every pull request, delegating to the shared `peacefulstudio/github-actions` reusable C# CI. It
  runs a **two-OS matrix — Linux (`ubuntu-latest`, with code coverage) and Windows
  (`windows-latest`)** — so the solution is proven cross-platform on every change.
- **[`public-gate.yaml`](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/.github/workflows/public-gate.yaml)** — on every pull request, on
  `ubuntu-latest`, runs a leak check and a no-AI-workflows audit over the tree, then restores, builds
  (Release) and runs the unit tests against nuget.org alone.
- **[`localnet.yaml`](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/.github/workflows/localnet.yaml)** — runs the real demo end to end on
  `ubuntu-latest`, on demand (`workflow_dispatch`) and on every push to `main`. It starts
  `peacefulstudio/canton-localnet` v0.8.4-1 with PQS, runs `REQUIRE_PQS=1 make run`, and tears
  LocalNet down (`down --volumes`) before and after. Its runs are on the
  [LocalNet workflow page](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/actions/workflows/localnet.yaml).
- **[`auto-tag-release.yaml`](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/.github/workflows/auto-tag-release.yaml)** — after `CI` and `LocalNet`
  are both green on the tip of `main`, tags `v<Version>` (the version in
  [`Directory.Build.props`](../../Directory.Build.props)) once and starts the release. An open pull request
  labelled `hold-release` pauses it.
- **[`release.yaml`](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/.github/workflows/release.yaml)** — drafts a prerelease for the tag and publishes
  it with the matching [`CHANGELOG.md`](../../CHANGELOG.md) section as its notes.

The generated C# under `src/MiniDemo.Contracts/Generated/` is committed. To check it against the Daml
source yourself, run `make codegen` (needs `dpm` and a JDK, see [Prerequisites](quickstart-details.md#prerequisites)) and
confirm `git status` shows no change under that folder.
