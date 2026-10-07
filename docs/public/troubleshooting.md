# Troubleshooting

Symptoms, causes and fixes for the demo, with the exit codes it uses.

Back to the [README](../../README.md).

| Symptom | Fix |
|---------|-----|
| `1. Bootstrap` fails with `Canton LocalNet is not reachable, or is not ready yet (Connection refused (localhost:11975))`, exit `1` | Start a LocalNet, or point the demo at a running one via the `CANTON_LOCALNET_*` env vars (see [Quickstart details](quickstart-details.md#prerequisites)). The startup banner prints the endpoints being targeted. The JSON Ledger API address serves **both** the bootstrap and the REST transport — by design, there is no separate REST endpoint variable — so one wrong value breaks both, and it fails at bootstrap before the REST section is reached. |
| Bootstrap succeeds, then `2. Issuance` fails with `Canton LocalNet is not reachable … (CreateInstrumentAsync failed (infra, status Grpc { StatusCode = Unavailable }): Error connecting to subchannel.)`, exit `1` | The gRPC leg is configured separately from the JSON one. Usually `CANTON_LOCALNET_LEDGER_GRPC` (default `http://localhost:11901`) still points at `a-validator-1` while `CANTON_LOCALNET_JSON_API_URL` targets another slot. Align the gRPC endpoint on the banner with your JSON API slot. |
| In your own app: the REST client fails while gRPC works | `options.HttpAddress` must be the **JSON Ledger API** base URL (`http://localhost:11975/` on a stock LocalNet), not `GrpcAddress` and not the gRPC port. In this demo the two share one resolved value, so a wrong one surfaces at bootstrap rather than in the REST section. |
| REST stream fails with `413 Content Too Large`, surfaced as a terminal stream error naming `StreamWindowLimit` | The participant's `http-list-max-elements-limit` is lower than the client's `StreamWindowLimit` (default `200`). Lower `options.StreamWindowLimit` to match the participant. ACS reads now page on their own, so this is usually an update or completion stream window — or a `StreamWindowLimit` set above the participant's own page-size ceiling (`10000`), which fails the ACS read's first page too. |
| `4. Cross-transport verification` fails with `Cross-transport check failed …`, exit `65` | The demo asserted something about the ledger and it did not hold; the report names the contract and the transports that disagreed. The usual cause is the two endpoints pointing at **different participants** — check that `CANTON_LOCALNET_LEDGER_GRPC` and `CANTON_LOCALNET_JSON_API_URL` on the startup banner name the same slot. If they do, the check has found a genuine disagreement between the two transports, which is what it exists to catch. |
| `3. Token Standard V2 two-step transfer` fails with `Pending-transfer check failed …` or `Total-supply check failed …`, exit `65` | The ledger did not hold what the transfer asserts: alice's holding is not locked exactly as proposed (the report names the holding and the expected lock), or `TotalSupply` by key is not `52`. A different total means `lookupAllByKey` found other holdings than the two the run left for bob. The issuer is a fresh party on every run, so its key never collides with an earlier run's; this points at the Daml model rather than leftover state. |
| `3. Token Standard V2 two-step transfer` fails with `TransferFactory_Transfer` rejected on `transfer.requestedAt` or `transfer.executeBefore` | The proposal's window is checked against the participant's ledger time. The demo sets `requestedAt` one minute before the local clock; a larger skew between your machine and the participant breaks it. Sync the clocks and re-run. |
| `5. Failure lane` fails with `Failure lane check failed …`, exit `65` | The ledger did not reject the resubmitted command id with `DUPLICATE_COMMAND` — it accepted it, or rejected it with another error id (named in the report). The lane treats the expected rejection as success, so this is a genuine change in the participant's command deduplication. |
| `dpm: command not found` / version too old | `curl -sSL https://get.digitalasset.com/install/install.sh \| sh -s -- 3.5.2`, add `~/.dpm/bin` to `PATH`, need `>= 1.0.20`. |
| Codegen fails with a JVM/Java error | Ensure a **JDK 17+** is on `PATH` (`java -version`) — the codegen component needs it to decode the DAR. |
| Committed `Generated/` differs from a fresh `make codegen` | Run `./scripts/codegen.sh` (Windows: `pwsh scripts/codegen.ps1`) locally and commit the updated `src/MiniDemo.Contracts/Generated/` files. |
| Want to point at a specific DAR | Set `MINI_DEMO_DAR=/path/to/your.dar` before `dotnet run`. |
| `6. PQS read model` prints `PQS is not available (…); skipping. Run \`make up PQS=true\` to enable it.` | Expected when the LocalNet was started without PQS — bring it up with `make up PQS=true` in `canton-localnet`, or ignore it: without `--require-pqs` this never fails the run. |
| `6. PQS read model` prints `PQS did not project the Asset contract …`, with `docker logs` / `psql` hints — or section 3 prints it with `The transfer proposal already committed` | The ledger side already passed, so the ledger is fine; the read model is lagging or `scribe` is down. Run the printed `docker logs --tail 50 pqs-a-validator-1 \| grep -i "unknown Daml package"` — if it matches, scribe is restarting to discover a newly uploaded package and will recover on its own; otherwise check the `scribe`/Postgres containers are up. |
| `6. PQS read model` reports `PQS projected N of M` with `N < M` | Scribe projects asynchronously; the ledger-side transfer already succeeded. Re-running the demo, or just waiting, resolves it — this is not a defect and does not fail the run. |
| `6. PQS read model` exits `69` (`--require-pqs was set, and PQS did not reach full projection within the bounded wait.`) | Only happens with `--require-pqs`. Sections 1-5 already passed, so the ledger is fine; PQS was unavailable, timed out with nothing projected, or stayed partial for the whole 120s budget. Same fixes as the two rows above — bring PQS up, or give `scribe` more time and re-run. |

## Exit codes

- `0` — a clean run.
- `1` — LocalNet is unreachable, or the ledger reports a failure the demo does not classify.
- `65` — the demo's own verification failed, a cross-transport divergence in contract ids, keys or `IHolding` views included. The report names what disagreed.
- `69` — `--require-pqs` was set and PQS did not reach full projection: unavailable, a timeout with nothing projected, or a partial projection still incomplete once the 120s budget runs out.
- `75` — LocalNet is reachable but unresponsive.
- `130` — Ctrl+C.

Anything else surfaces as an unhandled exception, which is also non-zero. Section 6 (PQS) is observational and does not contribute to the exit code unless `--require-pqs` is passed.
