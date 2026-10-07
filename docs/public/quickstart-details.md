# Quickstart details

Prerequisites, LocalNet configuration, the run commands and the expected output of the demo.

Back to the [README](../../README.md).

## Prerequisites

| Tool | Version | Why |
|------|---------|-----|
| **.NET SDK** | `>= 10.0.100` | builds and runs the app (`dotnet --version`) |
| **dpm** (Daml Package Manager) | `>= 1.0.20` | `dpm codegen-cs` needs the `oci://` component syntax; `>= 1.0.20` verifies component digests on a cache hit |
| **JDK** | `17+` | the codegen component runs a JVM helper to decode the DAR |
| **Docker + Compose** | Docker `>= 27`, Compose `>= 2.27` | runs the Canton LocalNet stack (`make up`); budget ~16 GB RAM for it |
| **Canton LocalNet** | running | the ledger the demo talks to — this repo does **not** start one |
| **PQS** (optional) | `make up PQS=true` in `canton-localnet` | starts the scribe projector + Postgres read model for section 6; without it, section 6 detects the missing database instantly and moves on |

Install `dpm` and the pinned Daml SDK. Pin the installer to a specific release (`3.5.2` lands dpm
launcher `1.0.21`) rather than riding `latest` — a moving target, and dpm `< 1.0.20` can reuse a
stale same-name codegen component from cache without verifying its digest:

```bash
curl -sSL https://get.digitalasset.com/install/install.sh | sh -s -- 3.5.2
export PATH="$HOME/.dpm/bin:$PATH"
dpm --version               # expect 1.0.21
dpm install 3.5.2           # the SDK this demo builds against
```

Bring up a LocalNet from [`peacefulstudio/canton-localnet`](https://github.com/peacefulstudio/canton-localnet)
(`make up`). Against a **stock local LocalNet, no configuration is needed** — every endpoint defaults to
the `a-validator-1` slot (JSON Ledger API `http://localhost:11975`, gRPC `http://localhost:11901`, the
local Keycloak realm, and the demo client credentials). On startup the demo prints exactly which
endpoints it is targeting.

To target a different validator/slot, set `CANTON_LOCALNET_PROFILE`; every value below is an **optional
override** (all default to the local `a-validator-1` slot):

```bash
export CANTON_LOCALNET_PROFILE=c-validator-1    # a-validator-1 (default) … d-validator-1, sv-validator-1
export CANTON_LOCALNET_JSON_API_URL=...         # JSON Ledger API base URL (bootstrap + REST transport)
export CANTON_LOCALNET_TOKEN_URL=...            # OAuth2 token endpoint
export CANTON_LOCALNET_CLIENT_ID=...
export CANTON_LOCALNET_CLIENT_SECRET=...
export CANTON_LOCALNET_AUDIENCE=...             # optional
export CANTON_LOCALNET_SCOPE=...                # optional
export CANTON_LOCALNET_LEDGER_GRPC=...          # gRPC Ledger API (default http://localhost:11901, the a-validator-1 port)
export CANTON_LOCALNET_VALIDATOR_USER_ID=...    # set only when the validator's ledger user isn't the default
export CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING=...  # PQS Postgres connection string; the name follows the slot (C_VALIDATOR_1 for c-validator-1, …)
```

`make up` in `canton-localnet` is a plain `docker compose up -d` and prints none of these values. A
slot's validator ledger user id is the `AUTH_<SLOT>_VALIDATOR_USER_ID` entry (for example
`AUTH_C_VALIDATOR_1_VALIDATOR_USER_ID`) in that repository's
`compose/modules/keycloak/env/<slot>/on/oauth2.env`. The demo targets a **single-synchronizer**
validator (one Daml module, one program). For a non-default slot the gRPC address still defaults to the
`a-validator-1` port, so set `CANTON_LOCALNET_LEDGER_GRPC` (e.g. `http://localhost:13901` for
`c-validator-1`) and `CANTON_LOCALNET_VALIDATOR_USER_ID` to that validator's ledger user. Without a user
id the demo stops before it touches the ledger, names the variable and where to find the value, and exits
`78`; an unknown `CANTON_LOCALNET_PROFILE` exits `78` the same way (so does a missing built `.dar`, or a `MINI_DEMO_DAR` that points nowhere), as does `sv-validator-1` until you set its token URL, client id and client secret (it has no defaults for them).

PQS follows the slot too: the demo reads the Postgres database `pqs-<slot>` (for example `pqs-c-validator-1`) on the same host, port and credentials as the default, or the connection string in `CANTON_LOCALNET_<SLOT>_PQS_CONNECTION_STRING`. `make up PQS=true` starts PQS for `a-validator-1` only, yet Postgres creates the `pqs-a-validator-1`, `pqs-b-validator-1`, `pqs-c-validator-1` and `pqs-sv-validator-1` databases. On b, c and sv that database exists but stays empty (no tables, no `active` function), so the read fails with SQLSTATE `42883` or `42P01`; `d-validator-1` has no PQS database at all (SQLSTATE `3D000`). The demo treats all of these alike: section 6 reports PQS as not available and moves on (or exits `69` under `--require-pqs`).

> **No private feed, token, or credential is needed to build.** Every SDK package restores from
> **public nuget.org** — `NuGet.config` lists `nuget.org` only. `dotnet restore` / `dotnet build`
> work on a clean machine with zero secrets.

## Run it

Three commands (each also wrapped by a `make` target):

```bash
./scripts/codegen.sh                 # 1. dpm build → dpm codegen-cs → (re)generate C# bindings   [make codegen]
dotnet build MiniDemo.slnx           # 2. build the .NET solution                                  [make build]
dotnet run --project src/MiniDemo    # 3. run the integration test (needs a running LocalNet; env vars optional) [make run]
```

Pass `--require-pqs` to step 3 (`dotnet run --project src/MiniDemo -- --require-pqs`, or
`REQUIRE_PQS=1 make run`) to make section 6 gate on PQS reaching full projection — see
[Troubleshooting](troubleshooting.md) for what that changes about the exit code. The demo exits `69`
when the requirement is not met, but `make` reports `Error 69` and exits `2` itself, so check for `69` with `dotnet run` directly.

**Windows:** run step 1 as `pwsh scripts/codegen.ps1` — a faithful PowerShell twin of `codegen.sh`
(Windows PowerShell 5.1 or PowerShell 7+); steps 2 and 3 are byte-for-byte identical. The `make`
targets assume a Unix shell, so on Windows call the three commands directly.

On a fresh clone the generated **bindings** are already committed, so `dotnet build` compiles without
codegen. The compiled **DAR is not committed** (`.daml/` is gitignored), so `dotnet run` needs step 1
first (or at least `dpm build`, or `MINI_DEMO_DAR` pointing at an existing `.dar`). Re-run step 1
whenever you change `Asset.daml`.

<details>
<summary><b>Expected output</b> (party IDs, contract IDs and ledger offsets differ per run and are shortened with <code>…</code> here; the amounts do not)</summary>

```text
Targeting Canton LocalNet (AValidator1). Values default to a local LocalNet; override any via CANTON_LOCALNET_* env vars:
  CANTON_LOCALNET_JSON_API_URL   http://localhost:11975/
  CANTON_LOCALNET_LEDGER_GRPC    http://localhost:11901
  CANTON_LOCALNET_TOKEN_URL      http://localhost:8082/realms/AValidator1/protocol/openid-connect/token
  CANTON_LOCALNET_CLIENT_ID      a-validator-1-validator
== 1. Bootstrap ==
Uploading DAR: …/daml/.daml/dist/canton-mini-demo-hbde402d4f83a-0.1.0.dar
DAR upload outcome: Uploaded
issuer = issuer-89189719140d::1220…
alice  = alice-89189719140d::1220…
bob    = bob-89189719140d::1220…
Granted act-as (issuer/alice/bob) to ledger user … (leased — revoked when the run completes or fails)

== 2. Issuance — instruments, a transfer factory and a mint by key ==
  create   Instrument(issuer, GOLD) over gRPC -> 00…
             (TryCreateAsync, outcome pattern-matched)
  create   Instrument(issuer, SILVER) over REST -> 00…
             (TryCreateAsync(...).OneOrThrowAsync)
  create   AssetTransferFactory(issuer, users = alice, bob) over gRPC -> 00…
  mint     42 GOLD to alice, by key (issuer-89189719140d::1220…, GOLD) over gRPC -> 00…

== 3. Token Standard V2 two-step transfer — alice -> bob ==
  propose  alice -> bob, 42 GOLD via TransferFactory_Transfer over gRPC -> pending 00…
             (requestedAt 2026-10-01 10:03:20Z <= now < executeBefore 2026-10-01 11:04:20Z)
  acs      alice: 42 GOLD 🔒  locked by issuer-89189719140d::1220… until 2026-10-01 11:04:20Z (transfer to bob-89189719140d::1220…)
             (00…, read as an IHolding view over gRPC)
  pqs      alice: 42 GOLD 🔒  locked by issuer-89189719140d::1220… until 2026-10-01 11:04:20Z (transfer to bob-89189719140d::1220…)
             (the same pending holding, read from the Postgres read model)
  disclose issuer reads the locked holding's typed Disclosure from its gRPC ACS query (1051 bytes) and hands it to bob off-ledger
  accept   bob accepts over REST, the locked holding attached as a disclosed contract -> completed, bob holds 00…
  observe  gRPC update stream (2777, 2783] delivered Created 00… to bob at offset 2781 (1 event(s) read)
  observe  REST update stream (2777, 2783] delivered Created 00… to bob at offset 2781 (1 event(s) read)
  mint     10 GOLD to bob, by key (issuer-89189719140d::1220…, GOLD) over gRPC -> 00…
  supply   TotalSupply by key (issuer-89189719140d::1220…, GOLD) over gRPC = 52 (42 accepted by bob + 10 minted to bob)

== 4. Cross-transport verification ==
  gRPC reads back the Asset written over REST (00…): same key (issuer-89189719140d::1220…, GOLD), same IHolding view 42 GOLD
  gRPC reads back the Asset written over gRPC (00…): same key (issuer-89189719140d::1220…, GOLD), same IHolding view 10 GOLD
  REST reads back the Asset written over REST (00…): same key (issuer-89189719140d::1220…, GOLD), same IHolding view 42 GOLD
  REST reads back the Asset written over gRPC (00…): same key (issuer-89189719140d::1220…, GOLD), same IHolding view 10 GOLD

== 5. Failure lane — expected rejections as typed values ==
  gRPC submit create Asset(issuer, issuer, DEDUP-gRPC-…) as command mini-demo-dedup-gRPC-… (deduplicated for 5 min)
  gRPC committed
  gRPC submit the same command id again (rejection read from the DamlError outcome)
  gRPC rejected as expected: DUPLICATE_COMMAND (category InvalidGivenCurrentSystemStateResourceExists)
  REST submit create Asset(issuer, issuer, DEDUP-REST-…) as command mini-demo-dedup-REST-… (deduplicated for 5 min)
  REST committed
  REST submit the same command id again (rejection read from the LedgerOperationException)
  REST rejected as expected: DUPLICATE_COMMAND (category InvalidGivenCurrentSystemStateResourceExists)

== 6. PQS read model ==
  PQS projected 2 of 2 Asset contract(s) owned by bob (WHERE owner = ? and LIMIT pushed into Postgres — the participant is never queried):
             00… name=GOLD amount=42.0000000000
             00… name=GOLD amount=10.0000000000
  PQS projected 2 of 2 of them as IHolding views (the interface view, decoded without naming the Asset template):
             00… owner=bob-89189719140d::1220…, admin=issuer-89189719140d::1220…, instrument=GOLD, amount=42.0000000000
             00… owner=bob-89189719140d::1220…, admin=issuer-89189719140d::1220…, instrument=GOLD, amount=10.0000000000

Done — one set of generated bindings drove 2 transports (gRPC and REST) through a Token Standard V2 two-step transfer against one ledger.
```

On startup the program prints the LocalNet endpoints it will target; if LocalNet isn't reachable it
prints `Canton LocalNet is not reachable, or is not ready yet (…)` with a hint and exits `1` — it never silently no-ops.

Only the identifiers and timestamps move between runs. Every run allocates fresh parties — note the
per-run suffix on `issuer-…` / `alice-…` / `bob-…` — so the contract ids and those suffixes differ
each time, but the amounts are fixed: alice's pending holding is always **42 GOLD 🔒**, the total
supply is always **52**, section 3 always prints **2** `observe` lines (one per transport, with the same offset), and section 4 always prints **4** lines — two readers × two of bob's
holdings, each with a matching key and view. A different amount or count is a real divergence, not
run-to-run noise. The `requestedAt` line sits one minute before the proposal and `executeBefore` one
hour after it; the minute absorbs clock skew between your machine and the participant.

Section 6 and the `pqs` line of section 3 are the exception to that rule by default: they are
**observational, not asserted**. If PQS is unreachable they print a one-line "not available" hint
and move on; if it's reachable but still catching up, they poll for up to 120s and report a partial
count or a timeout hint instead of failing. Neither a hint, a partial count, nor the projected count
itself changes the exit code — only sections 1-5 do that, unless `--require-pqs` is passed. With that
flag, an unavailable or a timed-out-with-nothing outcome exits `69` right away — from section 3 if the pending-holding read fails first, leaving alice's proposal pending, otherwise from section 6; a partial outcome keeps
polling until it either reaches `N of N` on both queries or exhausts the same 120s budget, and in the
latter case also exits `69` instead of `0`.

Reproducing this output needs LocalNet [`v0.8.4-1`](https://github.com/peacefulstudio/canton-localnet)
or later — earlier releases grant PQS read access to the validator party only, so PQS times out against
this demo's fresh parties instead of projecting anything.
</details>
