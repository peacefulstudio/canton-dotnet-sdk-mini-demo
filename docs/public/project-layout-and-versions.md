# Project layout and pinned versions

Where everything lives in the repository and which versions the demo is pinned to.

Back to the [README](../../README.md).

## Project layout

```
daml/
  daml.yaml                       # sdk-version 3.7.1, Daml-LF 2.3, package canton-mini-demo-h3c507af1f40c 0.1.0
  daml/MiniDemo/Asset.daml        # Instrument, Asset (IHolding), AssetTransferFactory, AssetTransferInstruction
  dars/                           # Splice token-standard DARs: holding-v2, metadata-v1, transfer-instruction-v2
  test/                           # Daml Script tests: accept, reject, withdraw, late accept, partial transfer (plus the shared setup script, which `dpm test` also reports)
scripts/
  compute-package-name.sh         # derives the hash-suffixed Daml package name that daml/daml.yaml pins
src/
  MiniDemo.Contracts/             # csproj that builds daml/ into a DAR and generates the C# bindings at build time (obj/<Configuration>/net11.0/DamlGenerated/MiniDemo/Asset/)
  MiniDemo/                       # the console CLI: read Program.cs and MiniDemoRunner.cs first, the folders hold the helpers
    Program.cs                    #   entry point: read env → check configuration → print banner → MiniDemoRunner → `Done in <N>s`
    MiniDemoRunner.cs             #   bootstrap → issuance → two-step transfer → cross-transport check
    Config/                       #   where the run connects and what it loads
      LedgerEndpoint.cs           #     resolve the gRPC (env, default :11901) and JSON Ledger API addresses
      LedgerTokenProvider.cs      #     the access token each ledger client sends
      LocalnetSlot.cs             #     a LocalnetProfile as its canton-localnet slot name (c-validator-1, C_VALIDATOR_1)
      DarLocator.cs               #     locate the built .dar (MINI_DEMO_DAR or the first .dar beside the assembly); a missing one exits 78
    Preflight/                    #   checks before the first ledger call
      LocalnetConfiguration.cs    #     up-front check: known profile, complete endpoint defaults (exit 78)
      LocalnetPreflight.cs        #     startup endpoint banner + "unreachable" socket-error classifier
    Ledger/                       #   the SDK calls the runner builds on
      LedgerTransport.cs          #     a transport: name + endpoint + the resolved ICantonLedgerClient + its result style
      ResultStyle.cs              #     pattern-matched outcome (gRPC lane) vs OneOrThrowAsync (REST lane)
      AssetKey.cs                 #     the (issuer, name) contract key, to and from its Daml tuple
      AssetAcsQuery.cs            #     typed, keyed active-contract-set query → AssetSnapshot list
      HoldingQuery.cs             #     IHolding interface-view query → HoldingSnapshot list, with each lock; typed Disclosure read
      UpdateStreamObserver.cs     #     offset-bounded, deadline-guarded SubscribeAsync<Asset> read of the transfer's Created event
      FailureLane.cs              #     section 5: expected rejection, a duplicate command id inside its deduplication window
    Pqs/                          #   section 6: the PQS read model
      PqsLane.cs                  #     bounded wait for PQS to project, then asset and IHolding reports
      NpgsqlHoldingsQuery.cs      #     IHolding views from PQS's active() SQL function, filtered to given contract ids
      PqsAvailability.cs          #     classifies "no PQS database" (SqlState 3D000) as unavailable
      PqsConnectionString.cs      #     PQS connection string (per-slot env var, default the slot's local pqs-<slot> database)
      PqsRequirementNotMetException.cs  # --require-pqs not satisfied (exit 69)
    Errors/                       #   how a failure becomes an exit code and a message
      DemoExitCode.cs             #     maps run failures to exit codes 0 / 1 / 65 / 69 / 75 / 78 / 130
      LedgerFailureReport.cs      #     prints a ledger rejection as code, message, command id and hint
      DamlFailureMessage.cs       #     shortens Canton's long interpretation-error text to its user message
      ErrorHint.cs                #     one-line hint per known Canton error code
      DemoVerificationException.cs  #   the demo's own failed assertion (exit 65)
      ExceptionChain.cs           #     shared exception-chain walk
    Reporting/                    #   what the console shows while the run progresses
      DemoProgress.cs             #     the step and transport the run is in, read when a failure is reported
      DemoStep.cs                 #     one step name with the transport it runs over
      RunTimer.cs                 #     times the run and prints `Done in <N>s`
      AmountFormat.cs             #     amount display for console output
      Tracing.cs                  #     opt-in OTLP trace export (OTEL_EXPORTER_OTLP_ENDPOINT) and the `mini-demo run` parent activity
tests/
  Directory.Build.props           # test-project build settings
  Directory.Packages.props        # test-project package versions
  MiniDemo.Tests/                 # xUnit v3 unit tests: two-step transfer, AssetAcsQuery, cross-transport check, PQS lane, …; one folder per src/MiniDemo folder, shared fakes in Support/
MiniDemo.slnx
Directory.Build.props             # shared build settings and the demo <Version>
Directory.Packages.props          # central SDK package versions
NuGet.config                      # nuget.org only — no private feed
global.json                       # pins the .NET SDK (11.0.100 minimum, rolls forward to the latest installed 11.x)
coverage.settings.xml             # code-coverage settings
Makefile                          # package-name / daml-test / build / build-dpm / run / clean
CHANGELOG.md                      # release notes
CONTRIBUTING.md                   # how to contribute
CODE_OF_CONDUCT.md                # community conduct
SECURITY.md                       # how to report a vulnerability
LICENSE                           # Apache-2.0
NOTICE                            # attribution notice
```

`daml/daml.yaml` drives `dpm build`. The `Daml.Codegen.CSharp.MSBuild` package runs that build and generates the
bindings from the DAR; in `Dpm` mode it pins the `dpm-codegen-cs` OCI component itself, by version and digest.


## Pinned versions

| Component | Version |
|-----------|---------|
| Daml SDK (`dpm install`) | `3.7.1` |
| Daml-LF target (`--target`) | `2.3` |
| `dpm` launcher | `>= 1.0.20` (pin the installer to `3.7.1`, which lands `1.1.1-snapshot-20261102.208.0.v13b5087`) |
| `Daml.Codegen.CSharp.MSBuild` | `0.8.1` (its `Dpm` mode pins the `dpm-codegen-cs` component `0.8.1` by digest) |
| `Daml.Runtime` | `0.8.1` |
| `Daml.Ledger.Abstractions` | `0.8.1` |
| `Canton.Ledger.Grpc.Client` | `0.8.1` |
| `Canton.Ledger.Rest.Client` | `0.8.1` |
| `Canton.Ledger.Kernel` | `0.8.1` |
| `Canton.Ledger.Pqs.Client` | `0.8.1` |
| `Canton.Ledger.OpenTelemetry` | `0.8.1` |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | `1.21.0` |
| `Npgsql` | `10.0.3` |
| `Splice.Api.Token.Holding.V2` | `1.0.0.22` |
| `Splice.Api.Token.Transfer.Instruction.V2` | `1.0.0.22` |
| `Canton.Ledger.Testing` | `0.8.1` (test projects only) |
| `Peaceful.Canton.Localnet.Testing` | `0.10.2.1` |
| `Microsoft.Extensions.DependencyInjection` | `11.0.0` |
| `Microsoft.Extensions.Logging.Console` | `11.0.0` |
| .NET SDK | `11.0` |

All versions are centrally managed (Central Package Management) in `Directory.Packages.props`, and
in `tests/Directory.Packages.props` for packages only the test projects reference.
