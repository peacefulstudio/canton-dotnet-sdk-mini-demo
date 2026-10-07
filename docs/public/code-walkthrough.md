# The code, section by section

A tour of the C# in `src/MiniDemo/`: how the transport is chosen, how commands and queries are written, and how the tests pin the behavior.

Back to the [README](../../README.md).

`Program.cs` is a thin entry point that reads the environment, prints the target-endpoint banner,
builds one service provider per transport, and hands off to `MiniDemoRunner`, which walks the
issuance and the two-step transfer step by step; `AssetAcsQuery` owns the keyed ACS query,
`HoldingQuery` the `IHolding` interface query (with each holding's lock) and the typed `Disclosure`
read that feeds the acceptance, and `ExerciseOutcomeExtensions` the `Unwrap` helper. A transport is nothing more than
`LedgerTransport(string Name, string Endpoint, ICantonLedgerClient Client, ResultStyle ResultStyle)` —
the runner never learns which wire it holds. Three small modules keep the wiring honest: `LocalnetPreflight` builds that startup
banner and classifies "LocalNet unreachable" socket errors into a friendly hint (so an unreachable
ledger exits `1` with guidance, never a silent no-op); `LedgerEndpoint` resolves both addresses — the
gRPC one from `CANTON_LOCALNET_LEDGER_GRPC` (default `http://localhost:11901`), the JSON Ledger API
one from the same `EndpointDiscovery` the bootstrap already uses, so the REST transport needs no new
environment variable; and `DarLocator` finds the built `.dar` (`MINI_DEMO_DAR` if set, else the most
recent archive under `daml/.daml/dist/`). Each console section maps to an SDK concept:

| Console section | What it exercises | Key SDK surface |
|-----------------|-------------------|-----------------|
| `1. Bootstrap` | Upload the DAR and allocate `issuer` / `alice` / `bob`, grant act-as rights (permission to submit commands as those parties) as a lease revoked when the run completes or fails | `LocalnetFixture.UploadDarAsync`, `AllocatePartyAsync`, `GrantUserRightsLeaseAsync` |
| `2. Issuance` | Create the `GOLD` instrument over gRPC and the `SILVER` one over REST (the two ways to read a create), the `AssetTransferFactory` with alice and bob as observers, and mint 42 GOLD to alice **by key** | generated `TryCreateAsync`; `Instrument.MintByKeyCommand` submitted with `TrySubmitSingleAsync`; `ExerciseOutcome<T>` pattern match vs `OneOrThrowAsync` |
| `3. Token Standard V2 two-step transfer` | Alice proposes over gRPC through the `ITransferFactory` interface; the runner reads her locked holding from the ACS and from PQS; the issuer reads its typed `Disclosure`; bob accepts over REST with that holding **disclosed**; both transports observe bob's `Created` event on the offset-bounded update stream; the issuer mints 10 GOLD to bob and checks `TotalSupply` by key is `52` | generated `TryTransferFactory_TransferAsync`; `QueryActiveAsync<IHolding, HoldingView>`; `TryTransferInstruction_AcceptAsync(configure: …WithDisclosedContracts)`; `QueryActiveAsync(includeDisclosure: true)`; `GetLedgerEndAsync`; `SubscribeAsync<Asset>(submitter, fromOffset, toOffset)`; `UpdateStreamObserver.ObserveCreatedAsync`; `Instrument.TotalSupplyByKeyCommand` + `ExerciseResult<decimal>` |
| `4. Cross-transport verification` | Each transport re-reads bob's keyed ACS and `IHolding` views and must see both of his holdings, under the same key and with the same view | `MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync`, which throws naming both transports on any divergence (exit `65`) |
| `5. Failure lane` | Submit one `create` twice under one command id inside a 5-minute deduplication window; the second submission must come back as `DUPLICATE_COMMAND`, read as a typed `DamlError` on gRPC and as a `LedgerOperationException` on REST | `CommandsSubmission.WithDeduplicationPeriod`, `DeduplicationPeriod.Duration`, `TrySubmitAndWaitForTransactionAsync`; `FailureLane.RunAsync` (exit `65` if the ledger accepts the duplicate) |
| `6. PQS read model` | Poll the PQS read model for bob's accepted holding, then run a filtered/paged SQL query and an `IHolding` interface query against it | `AddPqsClient(options)`, resolved as `IPqsClient`; `PqsLane.RunAsync`; `NpgsqlHoldingsQuery` |

## Choosing the transport

The client comes from dependency injection, and that is where the transport is decided. `Program.cs`
builds one container per transport. Register the token provider **before** the client — both
registrations fall back to `ITokenProvider.None` for any provider not already registered, so the order
matters:

```csharp
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Pqs.Client;
using Canton.Ledger.Rest.Client;
using Microsoft.Extensions.DependencyInjection;

await using var grpcServices = new ServiceCollection()
    .AddCantonStaticAuth(accessToken)
    .AddLedgerClient(options => options.GrpcAddress = grpcAddress)
    .BuildServiceProvider();

await using var restServices = new ServiceCollection()
    .AddCantonStaticAuth(accessToken)
    .AddRestLedgerClient(options =>
    {
        options.HttpAddress = jsonApiAddress;
        options.UserId = fixture.ValidatorUserId;
    })
    .BuildServiceProvider();

await using var pqsServices = new ServiceCollection()
    .AddPqsClient(options => options.ConnectionString = pqsConnectionString)
    .BuildServiceProvider();

LedgerTransport[] transports =
[
    new("gRPC", grpcAddress, grpcServices.GetRequiredService<ICantonLedgerClient>()),
    new("REST", jsonApiAddress, restServices.GetRequiredService<ICantonLedgerClient>(), ResultStyle.OrThrow),
];

var pqsClient = pqsServices.GetRequiredService<IPqsClient>();
```

`AddPqsClient` needs no bearer token — PQS is a direct Postgres connection, not an authenticated
Ledger API call, so it is registered in its own container rather than alongside
`AddCantonStaticAuth`.

Everything downstream of that array is shared. The whole difference between reaching Canton over gRPC
and reaching it over HTTP is the registration call:

```diff
     .AddCantonStaticAuth(accessToken)
-    .AddLedgerClient(options => options.GrpcAddress = grpcAddress)
+    .AddRestLedgerClient(options =>
+    {
+        options.HttpAddress = jsonApiAddress;
+        options.UserId = fixture.ValidatorUserId;
+    })
```

`HttpAddress` is the **JSON Ledger API** base URL — not `GrpcAddress`, and not the gRPC port.
`UserId` is optional: left unset, the participant derives it from the token; the demo passes
`fixture.ValidatorUserId`, the same ledger user it granted act-as rights to during bootstrap. Both
transport packages ship on the same `0.6.0-preview.4` line — preview software, pinned centrally (see
[Pinned versions](project-layout-and-versions.md#pinned-versions)).

Either call registers one adapter resolvable as the same five service types — `ICantonLedgerClient`,
`ILedgerClient`, `ILedgerReader`, `ILedgerWriter` and `ILedgerStreamer`. The gRPC package registers
them as singletons, so the channel is shared and the container owns it — dispose the service provider, not the client (the demo does so with `await using`); the REST package registers
them as transients over a named `HttpClient`. The demo resolves `ICantonLedgerClient` — still an
abstraction, not a transport type, and the narrowest one that carries the interface-view query
`QueryActiveAsync<TInterface, TView>` alongside the `ILedgerClient` writes and reads. The runner
methods that need only part of it still take `ILedgerWriter` or `ILedgerClient`, so the transport
stays a detail of the composition root.

The demo uses `AddCantonStaticAuth` because its token comes from the LocalNet fixture; a real service
uses `AddCantonAuth(configuration)` for OAuth2 client credentials, or `AddCantonLedger(configuration)`
to wire client and auth together from the canonical `Canton:Ledger` and `Canton:Auth` configuration
sections.

## Submitting commands

The command-submission calls read almost exactly like the domain language — and the parameter type is
the point, because it is what makes them portable. These are the runner's issuance and transfer
methods, with their console output elided and `DemoAsset` aliasing the generated `MiniDemo.Asset.Asset`:

```csharp
internal static async Task<ContractId<Instrument>> CreateInstrumentAsync(
    ILedgerWriter ledgerClient, Instrument instrument, string transportName, ResultStyle style, CancellationToken ct)
{
    var createdCid = style switch
    {
        ResultStyle.OutcomePatternMatch => MatchCreated(await ledgerClient.TryCreateAsync(instrument, cancellationToken: ct)),
        ResultStyle.OrThrow => await ledgerClient
            .TryCreateAsync(instrument, cancellationToken: ct)
            .OneOrThrowAsync(nameof(CreateInstrumentAsync)),
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown result style."),
    };
    return createdCid;
}

private static ContractId<Instrument> MatchCreated(ExerciseOutcome<ContractId<Instrument>> outcome) => outcome switch
{
    ExerciseOutcome<ContractId<Instrument>>.One created => created.Result,
    _ => outcome.Unwrap(nameof(CreateInstrumentAsync)),
};

internal static async Task<ContractId<DemoAsset>> MintByKeyAsync(
    ILedgerWriter ledgerClient,
    AssetKey instrumentKey,
    Party issuer,
    Party owner,
    string ownerLabel,
    decimal amount,
    string transportName,
    CancellationToken ct)
{
    var outcome = await ledgerClient.TrySubmitSingleAsync(
        Instrument.MintByKeyCommand(instrumentKey.ToDaml(), new Instrument.Mint(owner, amount)),
        new SubmitterInfo(issuer),
        workflowId: WorkflowId,
        cancellationToken: ct);
    var transaction = outcome.Unwrap(nameof(MintByKeyAsync));
    var minted = transaction.Single<DemoAsset>();
    return minted;
}

internal static async Task<ContractId<ITransferInstruction>> ProposeAsync(
    ILedgerWriter ledgerClient,
    ContractId<ITransferFactory> factoryCid,
    TransferProposal proposal,
    string transportName,
    CancellationToken ct)
{
    var outcome = await factoryCid.TryTransferFactory_TransferAsync(
        ledgerClient,
        new TransferFactory_Transfer(proposal.Transfer, [proposal.Parties.Alice], NoExtraArgs),
        new SubmitterInfo(proposal.Parties.Alice),
        workflowId: WorkflowId,
        cancellationToken: ct);
    var result = outcome.Unwrap(nameof(ProposeAsync));
    if (result.Output is not TransferInstructionResult_Output.TransferInstructionResult_Pending pending)
        throw new DemoVerificationException(
            $"TransferFactory_Transfer returned {result.Output.GetType().Name}; a two-step transfer must " +
            "leave a pending transfer instruction for the receiver to accept.");

    return pending.Value.TransferInstructionCid;
}

internal static async Task<ContractId<DemoAsset>> AcceptWithDisclosureAsync(
    ILedgerWriter ledgerClient,
    ContractId<ITransferInstruction> instructionCid,
    DisclosedContract lockedHolding,
    Party bob,
    string transportName,
    CancellationToken ct)
{
    var result = await instructionCid
        .TryTransferInstruction_AcceptAsync(
            ledgerClient,
            new TransferInstruction_Accept([bob], NoExtraArgs),
            new SubmitterInfo(bob),
            workflowId: WorkflowId,
            configure: submission => submission.WithDisclosedContracts(lockedHolding),
            cancellationToken: ct)
        .OneOrThrowAsync(nameof(AcceptWithDisclosureAsync));
    if (result.Output is not TransferInstructionResult_Output.TransferInstructionResult_Completed completed
        || completed.Value.ReceiverHoldingCids.Count != 1)
        throw new DemoVerificationException(
            $"TransferInstruction_Accept returned {result.Output.GetType().Name}; " +
            "accepting must complete the transfer into exactly one holding for bob.");

    return new ContractId<DemoAsset>(completed.Value.ReceiverHoldingCids[0].Value);
}

internal static async Task VerifyTotalSupplyAsync(
    ILedgerWriter ledgerClient, AssetKey instrumentKey, Party issuer, string transportName, CancellationToken ct)
{
    var outcome = await ledgerClient.TrySubmitSingleAsync(
        Instrument.TotalSupplyByKeyCommand(instrumentKey.ToDaml(), new Instrument.TotalSupply()),
        new SubmitterInfo(issuer),
        workflowId: WorkflowId,
        cancellationToken: ct);
    var transaction = outcome.Unwrap(nameof(VerifyTotalSupplyAsync));
    var totalSupply = transaction.ExerciseResult<decimal>(Instrument.ChoiceTotalSupply.Name);
    if (totalSupply != ExpectedTotalSupply)
        throw new DemoVerificationException(
            $"Total-supply check failed: TotalSupply by key {instrumentKey} returned " +
            $"{AmountFormat.Display(totalSupply)}; expected {AmountFormat.Display(ExpectedTotalSupply)}.");
}
```

The issuance lane creates `GOLD` through `TryCreateAsync` over gRPC and pattern-matches the returned
outcome; `SILVER` goes over REST through `TryCreateAsync(...).OneOrThrowAsync(...)` from
`Canton.Ledger.Abstractions`, which returns the value or throws a `LedgerOperationException`. The
console line under each `create` names the style, so the two are visible side by side. Every
`ContractId<T>` is one sealed type with value equality, so the cross-transport check compares the id a
transport wrote with the id every reader reports using `==`, not string comparison.

The generated extensions are declared on the abstraction, not on a transport —
`TryCreateAsync(this ILedgerWriter client, …)` and
`TryTransferFactory_TransferAsync(this ContractId<ITransferFactory> cid, ILedgerWriter client, …)` —
so they bind to whichever adapter the container produced. No overload, no conditional, no second
code path. `TryCreateAsync` takes no `SubmitterInfo`: each template's only signatory is its `issuer`
field, so the emitter derives the acting party from the payload. The choice helpers take a
`SubmitterInfo` because a bare contract id carries no payload to derive the controller from.

`TryTransferFactory_TransferAsync` comes from the Splice token-standard bindings
(`Splice.Api.Token.Transfer.Instruction.V2`), not from this repo's codegen: `AssetTransferFactory`
implements the `TransferFactory` interface, so any wallet that speaks the token standard can drive
it through `ContractId<ITransferFactory>` without knowing the template. Its result is the typed
`TransferInstructionResult`, whose `Output` union the runner matches on `Pending`.

The acceptance goes through the generated `TryTransferInstruction_AcceptAsync` like the proposal. Its
`configure` callback receives the `CommandsSubmission` built for the call, and the runner attaches the
locked holding with `WithDisclosedContracts`. The typed `TransferInstructionResult` comes straight back,
unwrapped over REST with `OneOrThrowAsync`.

## Observing the update stream

Observing the transfer takes the two ledger-end offsets the runner recorded around the proposal and
the accept, and drains the typed update stream on each transport (the logic is the same on both; only
the registered client differs). The excerpt omits the console output and the `EnsureInsideWindow` helper:

```csharp
internal static async Task ObserveCreatedAsync(
    Func<CancellationToken, IAsyncEnumerable<ContractStreamEvent<DemoAsset>>> openStream,
    string transportName,
    ContractId<DemoAsset> expected,
    LedgerOffset after,
    LedgerOffset through,
    TimeSpan timeout,
    TextWriter output,
    CancellationToken ct)
{
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
    deadline.CancelAfter(timeout);

    var eventsRead = 0;
    try
    {
        await foreach (var streamEvent in openStream(deadline.Token).WithCancellation(deadline.Token))
        {
            eventsRead++;
            switch (streamEvent)
            {
                case ContractStreamEvent<DemoAsset>.StreamError error:
                    throw new DemoVerificationException(
                        $"Update-stream check failed: the {transportName} stream ended with a fault ({error.Message}).");
                case ContractStreamEvent<DemoAsset>.Created created when created.ContractId == expected:
                    EnsureInsideWindow(created.Offset, after, through, transportName);
                    return;
            }
        }
    }
    catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
    {
        throw new DemoVerificationException(
            $"Update-stream check failed: the {transportName} stream delivered no Created {expected.Value} " +
            $"within {timeout.TotalSeconds:0} s over ({after.Value}, {through.Value}].");
    }

    throw new DemoVerificationException(
        $"Update-stream check failed: the {transportName} stream completed over ({after.Value}, {through.Value}] " +
        $"after {eventsRead} event(s) without a Created {expected.Value}.");
}
```

For each transport the runner calls the `ObserveCreatedAsync` overload that takes the client, which opens the stream with
`ledgerClient.SubscribeAsync<DemoAsset>(new SubmitterInfo(observer), after, through, streamCt)`:
`observer` is bob, and `after` and `through` are the `windowStart` and `windowEnd` offsets from `GetLedgerEndAsync`. A cancellation the caller asked for
still propagates as an `OperationCanceledException` (exit `130`); only the demo's own deadline becomes
a verification failure.

The mint and the total supply go **by key**: `Instrument.MintByKeyCommand((issuer, name), …)` and
`Instrument.TotalSupplyByKeyCommand((issuer, name), …)` build an `ExerciseByKeyCommand` that
`TrySubmitSingleAsync` submits. That path is untyped: it returns an
`ExerciseOutcome<TransactionResult>`, so the mint picks the one `Asset` it created out of
`CreatedContracts`, and the total supply reads the choice's `Decimal` with `ExerciseResult<decimal>`.

Every `Try…Async` helper returns an **`ExerciseOutcome<T>`** — a structured result that
distinguishes success (`One`), a Daml validation error (`DamlError` with error-id / category /
message), an infrastructure/transport error (`InfraError`), and the empty / multiple cardinality
cases. The demo's `Unwrap` helper collapses that to "give me the one result or throw a clear
exception".

Infrastructure faults carry a `TransportStatus` you can match on, so "the network is down" is
distinguishable from "the ledger said no". This is a documentation excerpt, not something the demo
runs live:

```csharp
var diagnosis = outcome switch
{
    ExerciseOutcome<ContractId<Instrument>>.InfraError { Status: TransportStatus.Grpc { StatusCode: GrpcStatusCode.Unavailable } }
        => "the ledger is unreachable over gRPC",
    ExerciseOutcome<ContractId<Instrument>>.InfraError { Status: TransportStatus.NoResponse }
        => "no complete response: the connection failed or the deadline expired",
    ExerciseOutcome<ContractId<Instrument>>.DamlError { ErrorId: "DUPLICATE_COMMAND" }
        => "the ledger already saw this command id",
    _ => "something else",
};
```

## Querying the active contract set

The keyed ACS query (section 4) uses the typed
`ledgerClient.SubscribeActiveAsync<Asset>(party, cancellationToken: ct)` stream —
a snapshot of the active contract set at the current ledger end, filtered to the `Asset` template by
its generated `TemplateId`. `AssetAcsQuery` switches on the resulting `AcsSnapshotEntry<Asset>`,
handling every case explicitly. `Created` events are mapped to an `AssetSnapshot`, with the
contract key decoded through the generated `Asset.Key.KeyDecoder` into an `(issuer, name)` pair; an
`Asset` that arrives without a key, or with one that doesn't decode, throws. The terminal
`Checkpoint` — which the SDK emits once at the end of every snapshot, including an empty one —
returns the accumulated list and ends the stream. A `StreamError` (transport failure), an
`Unclassified` event (one the SDK couldn't map to `Asset`), and any unrecognised entry type all
throw rather than being silently dropped, so a codegen/package-id mismatch surfaces immediately
instead of showing up as a missing contract. A stream that ends with neither terminal marker throws
as well, since that snapshot is truncated and returning it would be indistinguishable from a
complete one.

The `acs` line of section 3 reads holdings a second way, through the Splice token-standard
`IHolding` interface that `Asset` implements: `HoldingQuery` calls
`ICantonLedgerClient.QueryActiveAsync<IHolding, HoldingView>(alice)`, which drains an interface-filtered
ACS snapshot into a list of `InterfaceContract<IHolding, HoldingView>`. The runner requires exactly
one locked holding owned by alice: `42` units of the instrument `(issuer, "GOLD")`, locked by the
issuer until the transfer's `executeBefore`, with the context `transfer to <bob>`. That is exactly
what the Daml `interface instance` computes from the `lock` field. The `pqs` line checks the same
view read from PQS. Section 4 reuses the query for bob's holdings. A stream fault or an unclassified
row surfaces there as a `LedgerOperationException`, the SDK's own contract for this method.

`AssetAcsQuery` needs no transport branch for the same reason the submission code doesn't:
`ILedgerStreamer` is one contract and both packages implement it, yielding the same
`AcsSnapshotEntry<Asset>` union. Over REST the update stream is paginated underneath — windows of
`StreamWindowLimit` entries (default `200`, matching Canton's `http-list-max-elements-limit`), each
bounded by `StreamWindowIdleTimeout` (default `250ms`) — and faults arrive in band as terminal stream
errors, the way gRPC has always reported them. The ACS snapshot pages the same way: it reads
`StreamWindowLimit`-sized pages at the same offset until the participant returns none left, so the
pages together are one consistent snapshot rather than a single unbounded read, and a page that fails
ends the stream with an in-band `StreamError` after the entries already read (see
[Troubleshooting](troubleshooting.md)).

Section 4 is `VerifyEveryTransportSeesEveryAssetAsync`. For each transport it reads bob's keyed
active contract set and his `IHolding` views, and checks that both of his holdings — the one accepted
over REST and the one minted over gRPC — are in both, under the key it was written with, with the same payload and the same view the
first transport reported. A missing contract or view, a different key, or a different payload or view
throws a `DemoVerificationException`, naming both transports and the contract, and the demo exits
`65`. A disagreement between the wires is therefore a hard failure, not a line of output nobody
reads.

## The PQS lane

Section 6, `PqsLane`, queries the same generated `Asset` bindings against a different kind of surface:
`IPqsClient` is **not** an `ILedgerClient` — it has no `Create`/`Exercise`, no streaming subscription,
and only ever sees the current active-contract snapshot the scribe projector has caught up to, not
history or as-of queries. `PqsLane.RunAsync` polls `IPqsClient.FetchByIdAsync<Asset>` for the specific
contract id of bob's accepted holding — not "any row", which would leave a cold database and an
empty one indistinguishable — bounded to 120s at a 500ms interval, with a progress line after 2s of
silence.

Two outcomes short-circuit that wait and print a hint instead of a result: a `PostgresException`
with `SqlState 3D000` (no PQS database — `PqsAvailability.IsPqsUnavailable`, walking the exception chain
via the same `ExceptionChain.Flatten` helper `LocalnetPreflight` uses) and a connection-refused
`SocketException`, both meaning "PQS isn't running" rather than "PQS is behind". Once the probe
contract is found, it runs one `IPqsClient.QueryAsync<Asset>(Filter.Field(Owner, bob), PqsPage(...))`
and reports how many of bob's holdings PQS has projected, then one
`NpgsqlHoldingsQuery` (`SELECT … FROM active(<IHolding type id>) WHERE contract_id = ANY(…)` over Npgsql,
restricted to bob's holding ids) and reports how many of them it projected as `IHolding` views.

None of this can fail the run by default: `MiniDemoRunner.RunPqsLaneAsync` wraps
the whole lane in a catch-all (everything except `OperationCanceledException`, so Ctrl+C still exits
130) that reports the failure and moves on — so a PQS error `PqsAvailability` doesn't recognize can't
reach `DemoExitCode` unless `--require-pqs` is set. With that flag, an unavailable/timeout/short
outcome throws `PqsRequirementNotMetException` — a partial projection first keeps polling
`QueryAsync<Asset>` / `NpgsqlHoldingsQuery` until it is complete or the same 120s budget
runs out — and `DemoExitCode.ForRunAsync` maps that exception to exit `69`. See
[Troubleshooting](troubleshooting.md) for what each hint means.

## Tests

The LocalNet-free logic is covered by **xUnit v3 unit tests** in `tests/MiniDemo.Tests/`:

- `UnwrapTests` pins every `ExerciseOutcome<T>` branch of `Unwrap`.
- `FailureLaneTests` pins the duplicate-command section: the rejection is observed in both result styles, an accepted or differently rejected resubmission exits `65`, and infrastructure faults keep their usual exit codes. `CreateInstrumentStyleTests` pins the two ways to read a create.
- `AssetAcsQueryTests` drives `AssetAcsQuery` through a fake ledger client: created events map to snapshots with their decoded key and accumulate in order, a terminal checkpoint ends the snapshot, and stream-error, unclassified, marker-less, keyless and undecodable-key streams throw.
- `TwoStepTransferTests` drives the issuance and the transfer through a recording ledger writer:
  - the instrument and factory creates act as the issuer alone and list alice and bob as factory users;
  - the mint and `TotalSupply` go by the instrument key as the issuer;
  - the proposal acts as alice and must return a pending instruction;
  - the acceptance acts as bob, carries the locked holding as a disclosed contract and must complete;
  - a supply other than `52` exits `65`;
  - the pending-holding check rejects an unlocked holding or a lock with another holder, deadline or context, and its PQS read follows the same lenient/strict rules as section 6.
- `MiniDemoRunnerTests` drives the cross-transport check through fake transports: every transport sees every asset, key and view; a missing contract or view, or a diverging key, payload or view, throws and exits `65` through `DemoExitCode`; a lone transport reads back its own asset. It also pins `RunPqsLaneAsync`'s safety net: a `null` client skips cleanly, a query failure after the probe is found is caught and reported unless `--require-pqs` is set (then it is wrapped into `PqsRequirementNotMetException`), and cancellation still propagates.
- `DemoExitCodeTests` pins every `DemoExitCode.ForRunAsync` branch to its exit code, including a verification failure that names a connection issue and `PqsRequirementNotMetException` mapping to `69`.
- `LedgerEndpointTests` covers both endpoint resolutions: the gRPC-address env var and its default, and the JSON Ledger API URL from `EndpointDiscovery`.
- `LocalnetPreflightTests` covers the startup banner and the socket-error "unreachable" classifier; `ExceptionChainTests` pins the shared exception-chain walk (linear chains, `AggregateException` branches, de-duplication of a shared reference).
- `PqsConnectionStringTests` covers the PQS connection-string env var and its default.
- `PqsAvailabilityTests` and `PqsLaneTests` drive the PQS lane through `FakePqsClient` and a hand-rolled `IPqsClient` stub, all on millisecond-scale timeouts: the found, timeout and unavailable outcomes of the bounded wait, the projected-count report and the "still catching up" line. With `--require-pqs` they cover throwing on an unavailable, timed-out or still-incomplete-at-budget outcome, polling through a partial projection until it completes within the budget, and leaving a full first-pass projection unaffected.

`FakePqsClient.WithQueryResults` returns its staged list unconditionally rather than applying the `PqsFilter`, so those tests stage the set a real filtered query would return rather than exercising the filtering itself.

The Daml model has its own **Daml Script tests** in `daml/test/` (`make daml-test`): an accept moves
the locked holding to bob, a reject and a withdraw each return it to alice unlocked, an accept after
`executeBefore` fails, and a partial transfer leaves alice her change. They also pin who may act:
bob's accept fails without the disclosed holding, only bob may reject and only alice may withdraw.
The accept and reject tests check the total supply by key along the way.
