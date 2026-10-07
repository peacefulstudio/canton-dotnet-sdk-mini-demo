# Project layout and pinned versions

Where everything lives in the repository and which versions the demo is pinned to.

Back to the [README](../../README.md).

## Project layout

```
daml/
  daml.yaml                       # sdk-version 3.5.2, Daml-LF 2.3, package canton-mini-demo-hbde402d4f83a 0.1.0
  daml/MiniDemo/Asset.daml        # Instrument, Asset (IHolding), AssetTransferFactory, AssetTransferInstruction
  dars/                           # Splice token-standard DARs: holding-v2, metadata-v1, transfer-instruction-v2
  test/                           # Daml Script tests: accept, reject, withdraw, late accept, partial transfer
codegen/
  daml.yaml                       # codegen-only project — pins the dpm-codegen-cs OCI component
scripts/
  codegen.sh                      # dpm build → dpm codegen-cs (OCI) → atomic swap into Generated/
  codegen.ps1                     # Windows (PowerShell) twin of codegen.sh
src/
  MiniDemo.Contracts/             # generated C# bindings (committed) + csproj
    Generated/MiniDemo/Asset/     #   Instrument*.cs, Asset.cs, AssetTransferFactory.cs, AssetTransferInstruction.cs
  MiniDemo/                       # the console CLI
    Program.cs                    #   entry point: read env → print banner → MiniDemoRunner
    MiniDemoRunner.cs             #   bootstrap → issuance → two-step transfer → cross-transport check
    UpdateStreamObserver.cs       #   offset-bounded, deadline-guarded SubscribeAsync<Asset> read of the transfer's Created event
    LedgerTransport.cs            #   a transport: name + endpoint + the resolved ICantonLedgerClient + its result style
    ResultStyle.cs                #   pattern-matched outcome (gRPC lane) vs OneOrThrowAsync (REST lane)
    FailureLane.cs                #   expected rejection: a duplicate command id inside its deduplication window
    AssetAcsQuery.cs              #   typed, keyed active-contract-set query → AssetSnapshot list
    AssetKey.cs                   #   the (issuer, name) contract key, to and from its Daml tuple
    HoldingQuery.cs               #   IHolding interface-view query → HoldingSnapshot list, with each lock; typed Disclosure read
    ExerciseOutcomeExtensions.cs  #   Unwrap: ExerciseOutcome<T> → the one result or a clear throw
    LocalnetPreflight.cs          #   startup endpoint banner + "unreachable" socket-error classifier
    LedgerEndpoint.cs             #   resolve the gRPC (env, default :11901) and JSON Ledger API addresses
    DarLocator.cs                 #   locate the built .dar (MINI_DEMO_DAR or newest build output)
    DemoExitCode.cs               #   maps run failures to exit codes 0 / 1 / 65 / 69 / 75 / 130
    DemoVerificationException.cs  #   the demo's own failed assertion (exit 65)
    PqsLane.cs                    #   section 6: bounded wait for PQS to project, then asset and IHolding reports
    NpgsqlHoldingsQuery.cs        #   IHolding views from PQS's active() SQL function, filtered to given contract ids
    PqsAvailability.cs            #   classifies "no PQS database" (SqlState 3D000) as unavailable
    PqsConnectionString.cs        #   PQS connection string (env var, default local a-validator-1 database)
    PqsRequirementNotMetException.cs  # --require-pqs not satisfied (exit 69)
    ExceptionChain.cs             #   shared exception-chain walk
    AmountFormat.cs               #   amount display for console output
tests/
  MiniDemo.Tests/                 # xUnit v3 unit tests: two-step transfer, AssetAcsQuery, cross-transport check, PQS lane, …
MiniDemo.slnx
Directory.Packages.props          # central SDK package versions
NuGet.config                      # nuget.org only — no private feed
Makefile                          # codegen / daml-test / build / run / clean
```

Two `daml.yaml` files exist because `dpm` rejects a single file that sets both `sdk-version` and
`components`: `daml/daml.yaml` drives `dpm build`, and `codegen/daml.yaml` pins the codegen
component for `dpm codegen-cs`.


## Pinned versions

| Component | Version |
|-----------|---------|
| Daml SDK (`dpm install`) | `3.5.2` |
| Daml-LF target (`--target`) | `2.3` |
| `dpm` launcher | `>= 1.0.20` (pin the installer to `3.5.2`, which lands `1.0.21`) |
| `dpm-codegen-cs` OCI component | `0.6.0-preview.4` (pinned by digest) |
| `Daml.Runtime` | `0.6.0-preview.4` |
| `Daml.Ledger.Abstractions` | `0.6.0-preview.4` |
| `Canton.Ledger.Grpc.Client` | `0.6.0-preview.4` |
| `Canton.Ledger.Rest.Client` | `0.6.0-preview.4` |
| `Canton.Ledger.Kernel` | `0.6.0-preview.4` |
| `Canton.Ledger.Pqs.Client` | `0.6.0-preview.4` |
| `Npgsql` | `10.0.3` |
| `Splice.Api.Token.Holding.V2` | `1.0.0.16-preview.4` |
| `Splice.Api.Token.Transfer.Instruction.V2` | `1.0.0.16-preview.4` |
| `Canton.Ledger.Testing` | `0.6.0-preview.4` (test projects only) |
| `Peaceful.Canton.Localnet.Testing` | `0.8.4.1` |
| `Microsoft.Extensions.DependencyInjection` | `10.0.12` |
| `Microsoft.Extensions.Logging.Console` | `10.0.12` |
| .NET SDK | `10.0` |

All versions are centrally managed (Central Package Management) in `Directory.Packages.props`, and
in `tests/Directory.Packages.props` for packages only the test projects reference.
