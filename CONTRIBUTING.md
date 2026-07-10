# Contributing to canton-dotnet-sdk-mini-demo

Thanks for your interest. This document covers everything you need to send a
patch — from cloning the repo to getting your PR merged.

## Code of Conduct

By participating in this project you agree to abide by the
[Code of Conduct](CODE_OF_CONDUCT.md).

## Getting set up

If you have direct write access to `peacefulstudio/canton-dotnet-sdk-mini-demo`
(i.e. you're a maintainer), clone the upstream repo:

```bash
git clone https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo.git
cd canton-dotnet-sdk-mini-demo
dotnet restore && dotnet build
dotnet test
```

If you're an external contributor, **start by forking the repo** (see
"Opening a pull request" below for the full fork/upstream flow). The
build and test commands inside the cloned tree are the same:

```bash
dotnet restore && dotnet build
dotnet test
```

You'll need the [.NET SDK](https://dotnet.microsoft.com/download) — install the version pinned in this repo's `global.json`.

`dotnet test` runs the unit tests, which need no external services. The
end-to-end round-trip the demo performs (`make run` / `dotnet run --project
src/MiniDemo`) needs a running Canton LocalNet — see the
[README](README.md) for how to bring one up.

## Branching model

| Branch | Purpose                        |
|--------|--------------------------------|
| `main` | Default branch — open PRs here |

All PRs target `main`.

## Test-driven development

Bug fixes and new features must follow red-green TDD:

1. **Red** — write a failing test that describes the desired behaviour.
2. **Green** — write the minimum production code to make it pass.
3. **Refactor** — clean up while keeping tests green.

```bash
dotnet test --configuration Release
```

## Code style

- **.NET / C#** — the SDK version is pinned in `global.json`.
- Every `.cs` source file starts with the two-line SPDX copyright header:<br>`// Copyright 2026 Peaceful Studio OÜ`<br>`// SPDX-License-Identifier: Apache-2.0`
- Follow standard .NET conventions (`dotnet format` for layout; structured-template log strings, no interpolation; xUnit `Theory` + `MemberData`/`InlineData` for parameterised tests).
- Code should be expressive enough to not need comments. Add a comment only
  when the *why* is non-obvious (a workaround for an external bug, a hidden
  invariant). Don't comment on *what* the code does.

## Documentation

This demo is documented primarily through its [README](README.md) and the
inline structure of the sample itself. If your change alters the demo's
behaviour, the commands a user runs, or the on-ledger scenario, update the
README in the same PR.

## Opening a pull request

External contributors usually don't have write access to
`peacefulstudio/canton-dotnet-sdk-mini-demo`, so the PR flow goes through a
fork:

1. Fork `peacefulstudio/canton-dotnet-sdk-mini-demo` to your own GitHub account
   using the **Fork** button on the repo page (or `gh repo fork`).
2. Clone your fork (`origin` is set automatically) and add the
   upstream as a second remote so you can pull future changes:
   ```bash
   git clone https://github.com/<your-username>/canton-dotnet-sdk-mini-demo.git
   cd canton-dotnet-sdk-mini-demo
   git remote add upstream https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo.git
   ```
3. Create a feature branch from `main`:
   ```bash
   git fetch upstream
   git checkout -b feat/<short-description> upstream/main
   ```
4. Commit using the [Conventional Commits](https://www.conventionalcommits.org/)
   format (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`).
5. Push the branch to your fork and open a PR targeting `peacefulstudio/canton-dotnet-sdk-mini-demo`'s `main`:
   ```bash
   git push -u origin feat/<short-description>
   gh pr create --repo peacefulstudio/canton-dotnet-sdk-mini-demo --base main
   ```
6. Fill out the PR template — explicitly call out anything that affects the
   demo's public behaviour or the on-ledger scenario.
7. Make sure CI passes (build and tests).
8. Request review. A maintainer will respond.

(Maintainers with direct write access can skip the fork step and push
branches directly to `peacefulstudio/canton-dotnet-sdk-mini-demo`; the rest of
the flow is identical.)

## Reporting bugs

Open an issue using the "Bug report" template. The more reproducible the
report, the faster the fix.

For security-sensitive bugs, **do not open a public issue** — see
[SECURITY.md](SECURITY.md).

## License

By contributing, you agree that your contributions will be licensed under the
[Apache License 2.0](LICENSE), the same license as the project. No CLA
required.
