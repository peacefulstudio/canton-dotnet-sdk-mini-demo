# Adopt this in your own app

Steps to lift the demo's pattern into your own project, and where the SDK source lives.

Back to the [README](../../README.md).

## Steps

This demo *is* the template. To do the same in your own project:

1. **Add the SDK packages** (all on nuget.org) — take the transport you want, or both:
   ```bash
   dotnet add package Daml.Runtime
   dotnet add package Canton.Ledger.Grpc.Client   # gRPC Ledger API
   dotnet add package Canton.Ledger.Rest.Client   # JSON Ledger API — same abstractions, other wire
   dotnet add package Canton.Ledger.Kernel
   ```
2. **Write your Daml** template(s) and a `daml.yaml`.
3. **Generate bindings** with `dpm codegen-cs --dar <your.dar> --out <dir>`
   (crib `codegen/daml.yaml` for the OCI component pin, and `scripts/codegen.sh` for the build →
   codegen → commit flow).
4. **Register the client** with `AddCantonLedger(configuration)` (or `AddCantonStaticAuth` +
   `AddLedgerClient` / `AddRestLedgerClient` as the demo does) and inject `ICantonLedgerClient` when
   you query interface views, or the narrower `ILedgerClient` / `ILedgerWriter` / `ILedgerStreamer`
   otherwise. Every one of them is an abstraction both transports register, so the transport stays a
   composition-root choice.
5. **Submit commands** through the generated `TryCreateAsync` / `Try…Async` extensions on that
   client, handling the `ExerciseOutcome<T>` result — `MiniDemoRunner.cs`'s command-submission
   code and `AssetAcsQuery.cs`'s ACS-query code copy over directly, whichever transport you chose.

> The demo's **bootstrap** (DAR upload, party allocation, token) uses
> `Peaceful.Canton.Localnet.Testing` — a LocalNet/dev-time helper, **not** a production dependency. A
> real service supplies those from its own deployment (participant admin API, IAM) and keeps only the
> `Daml.Runtime` + `Canton.Ledger.*` packages at runtime.

Everything else here — the codegen script, the drift check, the central package versions, the
coverage wiring — is meant to be lifted into a real service.

## The wider SDK

This demo consumes packages from the rest of the Canton .NET SDK. If you want the source:

| Package / component | Repository |
|---------------------|-----------|
| `Daml.Runtime`, `Daml.Ledger.Abstractions`, `Canton.Ledger.Grpc.Client`, `Canton.Ledger.Rest.Client`, `Canton.Ledger.Kernel`, `Canton.Ledger.Pqs.Client`, `dpm codegen-cs` | [`canton-dotnet-sdk`](https://github.com/peacefulstudio/canton-dotnet-sdk) |
| `Peaceful.Canton.Localnet.Testing`, LocalNet stack | [`canton-localnet`](https://github.com/peacefulstudio/canton-localnet) |
