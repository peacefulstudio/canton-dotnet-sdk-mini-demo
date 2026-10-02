# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Each release tracks the Canton .NET SDK version it showcases; a demo-only fix
after a release takes a fourth identifier, for example `0.6.0-preview.3.1`.

## [Unreleased]

## [0.6.0-preview.3] — 2026-10-01

The two-step transfer now has an **audience**: after bob accepts, the demo watches the ledger emit the transaction on the **update stream** — over **both** transports — instead of only reading state back. The dependency line moves to the **Canton .NET SDK 0.6.0-preview.3**, and the README is re-checked against a real run.

**TL;DR** — the same zero-config `dotnet run` still issues a keyed `GOLD` instrument, mints 42 GOLD to **alice**, has **alice** propose a transfer to **bob** over **gRPC** and **bob** accept it over **JSON/REST** with the locked holding **disclosed**. New: it records the ledger end before the proposal and after the accept, then opens `SubscribeAsync<Asset>` on each transport over that offset window and requires bob's `Created` event to arrive inside it. Built on the **Canton .NET SDK 0.6.0-preview.3** (every `Daml.*` / `Canton.Ledger.*` package and the `dpm-codegen-cs` component) and the Splice **1.0.0.15-preview.3** token-standard bindings, with Daml SDK **3.5.2** targeting Daml-LF **2.3**, against **LocalNet 0.8.4-1** or later. 🎉

### 🎯 What changed

- **The transfer is observed on the update stream** — the runner reads the ledger end with `GetLedgerEndAsync` before alice's proposal and again right after bob's accept, then opens the typed `SubscribeAsync<Asset>` as bob on **each** transport over the half-open window `(windowStart, windowEnd]`. Bob's accepted holding must arrive as a `Created` event at an offset inside that window — both transports deliver the same event, and on one participant they report the same offset. 📡
- **Bounded by offsets and a deadline** — because `toOffset` is set, the stream completes on its own, and a 30-second deadline cancels it should the participant stall. A stream that ends without the event, delivers one outside the window, carries a `StreamError`, or misses the deadline fails the demo with exit `65`; a Ctrl+C still exits `130`. ⏱️
- **Two new `observe` lines in the console output** — section 3 prints one per transport, with the offset each delivered the event at. 🖥️
- **Canton .NET SDK 0.6.0-preview.3** — every `Daml.*` and `Canton.Ledger.*` package, the `dpm-codegen-cs` component (still pinned by digest), the Splice `holding-v2` and `transfer-instruction-v2` C# bindings (`1.0.0.15-preview.3`) and the LocalNet testing package (`0.8.4.1`) move together; the generated bindings regenerate cleanly. ⬆️
- **README audited against a real run** — the expected output, the sequence diagram, the section-to-SDK-call table, the project layout, the pinned-versions table and the exit-code description were re-checked against a real run on LocalNet 0.8.4-1, and a condensed excerpt of the update-stream observer walks the new check. Section 6 now names the Npgsql query that reads the `IHolding` views from PQS, and exit `1` is documented as also covering a ledger failure the demo does not classify. 📖
- **A README section on how the repo stays honest** — it now describes this repository's own workflows: build and unit tests on Linux and Windows, a public-tree gate on every pull request, and a **LocalNet demo lane** on GitHub-hosted runners that runs the demo end to end on every push to `main`. Its runs are on the [LocalNet workflow page](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/actions/workflows/localnet.yaml). ✅

### ▶️ Run it

```sh
./scripts/codegen.sh                                 # dpm build → dpm codegen-cs        [make codegen]
dotnet build MiniDemo.slnx                           #                                   [make build]
dotnet run --project src/MiniDemo                    # zero-config; defaults to a-validator-1   [make run]
dotnet run --project src/MiniDemo -- --require-pqs   # gate the exit code on PQS too     [REQUIRE_PQS=1 make run]
```

Needs a Canton **LocalNet 0.8.4-1 or later** up (JSON `:11975` / gRPC `:11901`) from [`canton-localnet`](https://github.com/peacefulstudio/canton-localnet) — `make up PQS=true` there if you want the PQS lane to project — plus `dpm` `>= 1.0.20` with Daml SDK `3.5.2` and a JDK 17+. On Windows, run `pwsh scripts/codegen.ps1` for the first step. Any endpoint, party, token or PQS override still rides a `CANTON_LOCALNET_*` env var — but the happy path needs none.

### 🧪 Under the hood

- `UpdateStreamObserver` in `src/MiniDemo` drains the stream under a linked deadline; only the demo's own deadline becomes a verification failure, a cancellation the caller asked for still propagates.
- Unit tests in `tests/MiniDemo.Tests` run against `Canton.Ledger.Testing` fakes, so the suite stays green with no LocalNet. The README's expected output is captured from a real run on 0.6.0-preview.3.
- Generated bindings stay committed under `src/MiniDemo.Contracts/Generated` — regenerate with `scripts/codegen.sh` (pinned to `dpm-codegen-cs:0.6.0-preview.3` by digest).

📖 Start at the [README](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/README.md) — it walks the whole Daml → codegen → two-step transfer → update-stream loop, section by section.

## [0.6.0-preview.2] — 2026-09-30

The mini-demo grows up: the single create → transfer round-trip becomes a **Splice Token Standard V2 two-step transfer**, split across **both** Ledger API transports, with a failure lane, a PQS read-model lane and documented exit codes around it.

**TL;DR** — the same zero-config `dotnet run` now issues a keyed `GOLD` instrument, mints 42 GOLD to **alice** by key, has **alice** propose a transfer to **bob** over **gRPC**, and has **bob** accept it over **JSON/REST** with the locked holding attached as a **disclosed contract**. It then checks `TotalSupply` by key (`52`), cross-reads bob's holdings on both transports, proves a duplicate command is rejected as a typed value, and reads the pending and final holdings back from PQS. Built on the **Canton .NET SDK 0.6.0-preview.2** (every `Daml.*` / `Canton.Ledger.*` package and the `dpm-codegen-cs` component) with Daml SDK **3.5.2** targeting Daml-LF **2.3**, against **LocalNet 0.8.3-2** or later. 🎉

### 🎯 What it shows

- **A Token Standard V2 two-step transfer** — `Asset`, `AssetTransferFactory` and `AssetTransferInstruction` implement the Splice `IHolding`, `ITransferFactory` and `ITransferInstruction` interfaces. Alice proposes through the generated **`TryTransferFactory_TransferAsync`**; her 42 GOLD is re-created **locked** 🔒 to the issuer until the deadline, next to a pending instruction. Bob accepts through **`TryTransferInstruction_AcceptAsync`** and receives an unlocked 42 GOLD. 🔁
- **One transfer, two wires** — the issuer and alice write over gRPC (`AddLedgerClient`), bob writes over REST (`AddRestLedgerClient`), and the runner never learns which wire it is on. A closing section has each transport read back the holdings the *other* one wrote — same contract ids, keys and `IHolding` views — and any divergence exits `65`. 🌐
- **Typed explicit disclosure** — the issuer queries its ACS with `includeDisclosure: true`, reads the locked holding's typed `Disclosure`, and bob attaches it through the generated helper's new `configure` callback: `submission => submission.WithDisclosedContracts(disclosure)`. No hand-built submission, no raw gRPC reader. 🔑
- **Contract keys at Daml-LF 2.3** — `Instrument` and `Asset` are keyed by `(issuer, name)`. Mints go through the generated `MintByKeyCommand`, and `TotalSupply` sums every holding under the (non-unique) key with `lookupAllByKey` — it must equal exactly `52`. 🗝️
- **Two result styles** — `GOLD` is created over gRPC by pattern-matching the `ExerciseOutcome`; `SILVER` over REST with `TryCreateAsync(...).OneOrThrowAsync()`. Pick whichever reads better in your code. 🎨
- **A failure lane** — one `create` submitted twice under one command id with `WithDeduplicationPeriod(...)`, on both transports. The second must come back as `DUPLICATE_COMMAND`: read from the `DamlError` outcome on gRPC, from `LedgerOperationException.ErrorId` on REST. An expected rejection is a value, not a crash. 🧯
- **A PQS lane** — the same generated bindings against the Postgres read model through `IPqsClient`: a bounded `FetchByIdAsync` poll, then `QueryAsync` with `Filter.Field` and `PqsPage` pushed into SQL, plus the `IHolding` interface view. Observational by default; pass **`--require-pqs`** to make anything short of full projection exit `69`. 🐘
- **Documented exit codes** — `0` clean, `1` LocalNet unreachable (gRPC and REST), `65` the demo's own verification failed, `69` PQS short of full projection under `--require-pqs`, `75` LocalNet reachable but unresponsive, `130` Ctrl+C. Anything unrecognised still propagates, never swallowed. 🚨

### ▶️ Run it

```sh
./scripts/codegen.sh                                 # dpm build → dpm codegen-cs        [make codegen]
dotnet build MiniDemo.slnx                           #                                   [make build]
dotnet run --project src/MiniDemo                    # zero-config; defaults to a-validator-1   [make run]
dotnet run --project src/MiniDemo -- --require-pqs   # gate the exit code on PQS too     [REQUIRE_PQS=1 make run]
```

Needs a Canton **LocalNet 0.8.3-2 or later** up (JSON `:11975` / gRPC `:11901`) from [`canton-localnet`](https://github.com/peacefulstudio/canton-localnet) — `make up PQS=true` there if you want the PQS lane to project — plus `dpm` `>= 1.0.20` with Daml SDK `3.5.2` and a JDK 17+. On Windows, run `pwsh scripts/codegen.ps1` for the first step. Any endpoint, party, token or PQS override still rides a `CANTON_LOCALNET_*` env var — but the happy path needs none.

### 🧪 Under the hood

- **Reruns stay clean.** Act-as rights are granted as a **lease**, revoked when the run completes or fails, so repeated runs no longer pile rights onto the validator's ledger user. The Daml package name carries a **content hash** (`scripts/compute-package-name.sh`) of the Daml source and the vendored DARs, so any change uploads under a fresh identity instead of colliding with a package already on the ledger. Parties, command ids and failure-lane asset names are fresh on every run.
- **Fail loud, not silent.** An ACS snapshot that ends without its terminal checkpoint throws instead of returning a truncated list, and a stream error is reported as a ledger fault rather than a content mismatch.
- The Splice `holding-v2`, `metadata-v1` and `transfer-instruction-v2` DARs are vendored under `daml/dars/`; Daml Script tests in `daml/test/` (`make daml-test`) cover accept, reject, withdraw, a late accept and a partial transfer.
- Generated bindings stay committed under `src/MiniDemo.Contracts/Generated` — regenerate with `scripts/codegen.sh` (pinned to `dpm-codegen-cs:0.6.0-preview.2` by digest).
- Unit tests in `tests/MiniDemo.Tests` run against `Canton.Ledger.Testing` fakes, so the suite stays green with no LocalNet. The README's expected output is captured from a real run on 0.6.0-preview.2.

📖 Start at the [README](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/README.md) — it walks the whole Daml → codegen → two-step transfer loop, section by section.

## [0.4.0-preview.1] — 2026-07-21

The first public cut of the mini-demo: a minimal, **runnable** end-to-end tour of the [Canton](https://canton.network) .NET SDK — Daml → C# codegen → a real ledger round-trip against a live LocalNet.

**TL;DR** — one `dotnet run` drives the whole arc: an issuer mints an `Asset` owned by **alice**, **alice** transfers it to **bob**, and the ACS is queried back as **bob** to prove the move landed on-ledger. Zero config — it defaults to a LocalNet `a-validator-1` slot. Built on **Daml codegen 0.4.0-preview.2** + **Canton ledger 0.4.0-preview.1** against **LocalNet 0.6.11**. 🎉

### 🎯 What it shows

- **The full round-trip** — `create → exercise Transfer → query ACS`, each step over the real gRPC Ledger API, printing contract IDs so you can watch alice's asset become bob's. 🔁
- **The 0.4.0 client in anger** — writes go through the capability-split **`ILedgerWriter`** (the generated `Asset` extension methods target it); ACS reads come back as **`AcsSnapshotEntry<T>`** snapshots, including the new **`StreamError`** case that surfaces a failed stream loudly instead of quietly ending. 📼
- **The `readAs` escape hatch** — 0.4.0's generated `Transfer` wrapper no longer carries `readAs`, so the transfer re-creates the `Asset` with stakeholders visible to **bob** but *not* the submitting party via **`TryCreateOneByExerciseAsync`** with a `SubmitterInfo(actAs: alice, readAs: {bob})` — the highest-level typed API that can still carry `readAs`. 🔑
- **Fail-loud preflight** — an unreachable LocalNet prints a clear diagnostic and exits `1`, never a silent no-op. 🚨

### ▶️ Run it

```sh
./scripts/codegen.sh                 # dpm build → dpm codegen-cs
dotnet build MiniDemo.slnx
dotnet run --project src/MiniDemo    # zero-config; defaults to a-validator-1
```

Needs a Canton **LocalNet 0.6.11** up (JSON `:11975` / gRPC `:11901`) and the `dpm` toolchain. LocalNet runs on Docker — budget **Docker ≥ 27 + Compose ≥ 2.27 and ~16 GB RAM**. Any endpoint, party, or token override rides a `CANTON_LOCALNET_*` env var — but the happy path needs none.

### 🧪 Under the hood

- Generated Daml bindings are committed under `src/MiniDemo.Contracts/Generated` — regenerate any time with `scripts/codegen.sh` (pinned to `dpm-codegen-cs:0.4.0-preview.2`).
- Unit tests live in `tests/MiniDemo.Tests` (with coverage) and fake the ledger, so the suite runs green with no LocalNet.
- The README quickstart mirrors a real run — party IDs in their true `<hint>-<hex>::<namespace>` shape and ACS amounts at full `Decimal` scale.

📖 Start at the [README](https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/blob/main/README.md) — it walks the whole Daml → codegen → run loop.

[Unreleased]: https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/compare/v0.6.0-preview.3...HEAD
[0.6.0-preview.3]: https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/compare/v0.6.0-preview.2...v0.6.0-preview.3
[0.6.0-preview.2]: https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/compare/v0.4.0-preview.1...v0.6.0-preview.2
[0.4.0-preview.1]: https://github.com/peacefulstudio/canton-dotnet-sdk-mini-demo/releases/tag/v0.4.0-preview.1
