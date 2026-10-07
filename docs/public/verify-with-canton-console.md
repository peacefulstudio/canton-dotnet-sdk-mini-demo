# Verify on-ledger with the Canton console

Confirm with the participant's own transaction stream that the mint, the proposal and the acceptance landed on-ledger.

Back to the [README](../../README.md).

The demo prints the contract IDs it creates, but you don't have to take its word for it — you can read
the **participant's own transaction stream** and confirm the mint, the proposal and the acceptance
actually landed on-ledger. The Daml SDK ships a Canton console for exactly this: `dpm canton-console`.

> Needs a running LocalNet (the same single-sync **a-validator-1** the demo targets by default) and
> `dpm >= 1.0.20`. None of this is required to run the demo — it's a verification aid.

**1. Write a console config** pointing at a-validator-1's Ledger + Admin APIs (this is LocalNet's own
console config with the container address swapped for `localhost`):

```bash
cat > canton-console.conf <<'CONF'
canton.features.enable-testing-commands = yes
canton.remote-participants.a-validator-1 {
  ledger-api { address = "localhost", port = 11901 }
  admin-api  { address = "localhost", port = 11902 }
  token = ${A_VALIDATOR_1_VALIDATOR_USER_TOKEN}
}
CONF
```

**2. Mint a bearer token** into that env var (a-validator-1's Keycloak realm — these are LocalNet's
fixed local-dev credentials, the same ones the demo defaults to, not secrets). It is piped straight
into the variable so it never reaches your terminal or shell history:

```bash
export A_VALIDATOR_1_VALIDATOR_USER_TOKEN=$(
  curl -fsS http://localhost:8082/realms/AValidator1/protocol/openid-connect/token \
    -d grant_type=client_credentials -d scope=openid \
    -d client_id=a-validator-1-validator \
    -d client_secret=AL8648b9SfdTFImq7FV56Vd0KHifHBuC \
  | sed -n 's/.*"access_token":"\([^"]*\)".*/\1/p')
```

**3. Open the console:**

```bash
dpm canton-console -c canton-console.conf
```

**4. Inside the console**, resolve the participant and the ids from the run you care about. Take them
from the demo's own output: the full `issuer = …` and `bob = …` lines of section 1, and the ledger user
from the `Granted act-as (issuer/alice/bob) to ledger user <id>` line right below them. Every run
allocates fresh parties, so ids from an older run — or a lookup by the `issuer-` prefix, which finds
whichever demo run came first — read the wrong contracts.

The demo leases act-as rights to the ledger user for the run and revokes them when it finishes, so
after a completed run the console's token no longer has any right on those parties and every query
fails with `PERMISSION_DENIED`. Grant read rights for the two parties, run the queries, then revoke
them again:

```scala
val p = participants.remote.find(_.name == "a-validator-1").get

val issuer = PartyId.tryFromProtoPrimitive("issuer-<hex>::1220…")
val bob    = PartyId.tryFromProtoPrimitive("bob-<hex>::1220…")
val uid    = "<ledger user id from the Granted act-as line>"

p.ledger_api.users.rights.grant(id = uid, actAs = Set.empty, readAs = Set(issuer, bob))

// (a) What bob owns now — the accepted holding and the one minted to him:
p.ledger_api.state.acs.of_party(bob).foreach { c =>
  println(s"${c.templateId.entityName}  ${c.contractId.take(16)}…")
}

// (b) The transactions that produced them (issuer is a signatory on every contract):
val end = p.ledger_api.state.end()
p.ledger_api.updates.transactions(Set(issuer), 100, endOffsetInclusive = Some(end)).foreach { w =>
  val tx = w.transaction
  println(s"workflow=${tx.workflowId}  (${tx.events.size} event(s))")
  tx.events.foreach { e =>
    e.event.created.foreach (c => println(s"   + created  ${c.contractId.take(16)}…"))
    e.event.archived.foreach(a => println(s"   - archived ${a.contractId.take(16)}…"))
  }
}

p.ledger_api.users.rights.revoke(id = uid, actAs = Set.empty, readAs = Set(issuer, bob))
```

The contract IDs vary per run, but match the ones the demo just printed. In (a), bob's active
contract set lists three contracts: the `AssetTransferFactory` (bob is one of its observers) and two
`Asset` contracts, the 42 GOLD he accepted and the 10 GOLD minted to him. In
(b), the query returns nine updates; read the issuer's history top to bottom:

- three single-event transactions create the `GOLD` and `SILVER` instruments and the
  `AssetTransferFactory`;
- the mint to alice creates her unlocked `Asset`;
- the proposal **archives** that `Asset` and **creates** two contracts: the locked `Asset` and the
  `AssetTransferInstruction`;
- the acceptance **archives** the instruction and the locked `Asset`, and **creates** bob's unlocked
  `Asset`;
- the mint to bob creates his second `Asset`;
- the failure lane's two committed creates follow.

`TotalSupply` is a nonconsuming choice that creates nothing, so it leaves no event in this view. The
proposal went over gRPC and the acceptance over REST, but nothing here says so: the ledger records the
same commands whichever transport carried them, which is why the demo's own cross-check works on the
read side. Type `exit` to leave.

> The bearer expires after a few minutes; if a command returns `UNAUTHENTICATED`, re-run step 2 and
> reconnect. This targets **a-validator-1** — for another slot use its port (`b`=`12901`, `c`=`13901`,
> `d`=`14901`), Keycloak realm (`BValidator1`…), and client id/secret.
