# The Daml contract and the C# generated from it

The Daml model in `daml/` and the C# object model `dpm codegen-cs` emits from it.

Back to the [README](../../README.md).

## The Daml contract

`daml/daml/MiniDemo/Asset.daml` holds four templates. The instrument and the holding are short enough
to read in full:

```daml
template Instrument
  with
    issuer : Party
    name : Text
  where
    signatory issuer

    key (issuer, name) : (Party, Text)
    maintainer key._1

    nonconsuming choice Mint : ContractId Asset
      with
        owner : Party
        amount : Decimal
      controller issuer
      do
        create Asset with issuer; owner; name; amount; lock = None

    nonconsuming choice TotalSupply : Decimal
      controller issuer
      do
        holdings <- lookupAllByKey @Asset (issuer, name)
        pure $ sum $ map (\(_, holding) -> holding.amount) holdings

template Asset
  with
    issuer : Party
    owner : Party
    name : Text
    amount : Decimal
    lock : Optional HoldingV2.Lock
  where
    signatory issuer
    observer owner
    ensure amount > 0.0

    key (issuer, name) : (Party, Text)
    maintainer key._1

    interface instance HoldingV2.Holding for Asset where
      view = HoldingV2.HoldingView with
        account = ownerAccount owner
        instrumentId = HoldingV2.InstrumentId with
          admin = issuer
          id = name
        amount
        lock
        meta = emptyMetadata
```

The other two implement the Splice token-standard transfer interfaces:

- **`AssetTransferFactory`** (`signatory issuer`, `observer users`) implements
  `TransferInstructionV2.TransferFactory`. Its `TransferFactory_Transfer` checks that the sender alone
  acts, that the instrument is the issuer's, and that `requestedAt <= now < executeBefore`. It then
  archives the sender's unlocked input holdings, re-creates the amount **locked** to the issuer until
  `executeBefore` (plus a change holding if the inputs cover more than the amount), and creates an
  `AssetTransferInstruction`. It returns `TransferInstructionResult_Pending`.
- **`AssetTransferInstruction`** (`signatory issuer`, `observer` sender and receiver) implements
  `TransferInstructionV2.TransferInstruction`. `TransferInstruction_Accept` (the receiver, before
  `executeBefore`) archives the locked holding and creates an unlocked one for the receiver.
  `TransferInstruction_Reject` (the receiver) and `TransferInstruction_Withdraw` (the sender) return the
  holding to the sender, unlocked.

A **single signatory** (`issuer`) on every template keeps authorization simple: the issuer's
authority comes from the factory and the instruction, which it signs, so alice and bob only ever act
as controllers.

The **key** `(issuer, name)` is maintained by the issuer and is shared by the `Instrument` and by
every `Asset` of it. Contract keys need Daml-LF 2.3, hence `--target=2.3`, and at LF 2.3 they are
**not unique**: the ledger accepts any number of active `Asset` contracts under `(issuer, "GOLD")`.
That is the point here. A holding is one of many under its instrument's key, and `lookupAllByKey`
returns all of them, which is how `TotalSupply` sums the supply without a registry of holdings. The
`Instrument` is created once per name in this demo, so exercising `Mint` or `TotalSupply` by key
always finds that one instrument.

The **factory's observers** are its `users`. Alice has to see the factory to exercise
`TransferFactory_Transfer` on it, and the demo lists bob as well. A registry would typically disclose
its factory to wallets instead.

The **interface instance** makes every `Asset` a Splice token-standard holding (`HoldingV2.Holding`,
from the `splice-api-token-holding-v2` and `splice-api-token-metadata-v1` DARs under `daml/dars/`,
declared as `data-dependencies` next to `splice-api-token-transfer-instruction-v2`). Its view says
the owner holds `amount` units of the instrument named `name`, administered by the issuer, with the
contract's `lock`, so a wallet that only speaks `IHolding` sees alice's pending 42 GOLD as locked
without knowing the `Asset` template.


## What codegen produces

From that one Daml module, `dpm codegen-cs` emits a small, idiomatic C# object model under
`src/MiniDemo.Contracts/Generated/MiniDemo/Asset/` (namespace `MiniDemo.Asset`, named after the Daml module):

- **`record Instrument(Party Issuer, string Name)`** and
  **`record Asset(Party Issuer, Party Owner, string Name, decimal Amount, Lock? Lock)`** — the templates
  as C# records, with `[DamlField]` attributes, `ToRecord()` / `FromRecord()` wire mapping, and static
  metadata (`TemplateId`, `PackageId`, `PackageName`, `PackageVersion`). The Daml `Optional Lock` is a
  nullable reference to the Splice-generated `Lock`.
- **`Instrument.Mint(Party Owner, decimal Amount)`** and **`Instrument.TotalSupply()`** — the choice
  arguments, and each choice's `ResultDecoder` (`Mint` returns the `ContractId<Asset>` it creates).
  Contract ids are the runtime's `ContractId<T>`, not a nested type.
- **`Instrument.Key`** / **`Asset.Key`** — a `KeyDescriptor<…, Tuple2<Party, string>>` carrying the
  key's encoder and decoder, and the by-key command builders `Instrument.MintByKeyCommand(key, arg)` /
  `Instrument.TotalSupplyByKeyCommand(key, arg)` / `ArchiveByKeyCommand(key)`, which return an
  `ExerciseByKeyCommand` to submit with `TrySubmitSingleAsync`.
- **`AssetTransferFactory(Party Issuer, IReadOnlyList<Party> Users)`** and
  **`AssetTransferInstruction(Party Issuer, Transfer Transfer, ContractId<Asset> LockedHoldingCid)`**,
  plus the interface instances: `IImplements<IHolding>` on `Asset`, `IImplements<ITransferFactory>`
  and `IImplements<ITransferInstruction>` on the other two. The Splice-generated interface types (from
  the `Splice.Api.Token.Holding.V2` and `Splice.Api.Token.Transfer.Instruction.V2` packages) carry the
  interface choices: `TryTransferFactory_TransferAsync`, `TryTransferInstruction_AcceptAsync` and the
  rest.
- **Ergonomic extension methods** so you rarely hand-build a ledger command — they are declared on
  `ILedgerWriter`, so the same call binds to either transport:
  - `TryCreateAsync(this ILedgerWriter, Instrument payload, …)` and the same for every template — the
    acting party is derived from the payload's signatory field, so there is no `SubmitterInfo` to pass.
  - `TryMintAsync` / `TryTotalSupplyAsync` on a `ContractId<Instrument>` — the same choices by contract
    id, returning a typed result (`ContractId<Asset>`, `decimal`). There is no by-key `Try…Async`, which is why
    the demo submits the `…ByKeyCommand` builders through `TrySubmitSingleAsync`.
  - `MintCommand(…)` / `TotalSupplyCommand(…)` / `ArchiveCommand(…)` — the same calls as bare
    `ExerciseCommand` builders, for batching several choices into one submission. The `Try…Async`
    helpers take a `configure` callback for what they don't expose, such as disclosed contracts.
- **Template ids for PQS** — no separate generated file: `TemplateExtensions.GetTemplateId<Asset>()`
  from `Daml.Runtime` returns the `{packageName}:Module:Entity` string PQS queries expect.

The Daml `Party`, `ContractId<T>`, `Decimal`, `Text`, and the record/choice machinery all come from
the **`Daml.Runtime`** package — the "codegen runtime library" the generated code depends on.
