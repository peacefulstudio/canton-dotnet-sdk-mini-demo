# How the repo stays honest

The CI workflows that keep the README and the guides re-proven rather than taken on trust.

Back to the [README](../../README.md).

Five GitHub Actions workflows run on GitHub-hosted runners, so what the README and these guides claim is re-proven
by CI rather than taken on trust, and every release is cut from a green run:

- **[`ci.yaml`](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/.github/workflows/ci.yaml)** — on every push to `main` and every pull
  request, on `ubuntu-latest` only, with `dpm` `1.0.22` and Daml SDK `3.6.0` installed. Four jobs:
  `package-name` fails when the content-addressed package name in `daml/daml.yaml` is stale;
  `build (Standalone)` builds and tests the solution (Release) with no JDK reachable;
  `build (Dpm)` builds it with JDK 17 and `-p:DamlCodegenMode=Dpm`; and
  `both modes generate the same bindings` diffs the two sets of generated C#.
- **[`public-gate.yaml`](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/.github/workflows/public-gate.yaml)** — on every pull request, on
  `ubuntu-latest`, runs a leak check and a no-AI-workflows audit over the tree, then installs `dpm` and the Daml SDK and
  restores, builds (Release) and runs the unit tests against nuget.org alone.
- **[`localnet.yaml`](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/.github/workflows/localnet.yaml)** — runs the real demo end to end on
  `ubuntu-latest`, on demand (`workflow_dispatch`) and on every push to `main`. It boots LocalNet
  v0.9.4-1 with PQS through the `peacefulstudio/canton-localnet` GitHub Action, builds
  the demo with `dotnet build src/MiniDemo/MiniDemo.csproj`, runs
  `dotnet run --project src/MiniDemo -- --require-pqs`, and the action tears LocalNet down (`down --volumes`) before and after. Its runs are on the
  [LocalNet workflow page](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/actions/workflows/localnet.yaml).
- **[`auto-tag-release.yaml`](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/.github/workflows/auto-tag-release.yaml)** — after `CI` and `LocalNet`
  are both green on the tip of `main`, tags `v<Version>` (the version in
  [`Directory.Build.props`](../../Directory.Build.props)) once and starts the release. An open pull request
  labelled `hold-release` pauses it.
- **[`release.yaml`](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/.github/workflows/release.yaml)** — drafts a release for the tag and publishes
  it with the matching [`CHANGELOG.md`](../../CHANGELOG.md) section as its notes, marked a prerelease only when the
  version contains `-`, so `v0.6.0` publishes as a normal release.

The C# bindings are generated at build time, so no generated source is committed and none can drift from the Daml.
`ci.yaml` builds with both `Standalone` and `Dpm` and fails when the two generate different bindings. To run the same
check locally, run `make build` and `make build-dpm` (the second needs a JDK, see
[Prerequisites](quickstart-details.md#prerequisites)) and compare the two `DamlGenerated` folders under
`src/MiniDemo.Contracts/obj/`.
