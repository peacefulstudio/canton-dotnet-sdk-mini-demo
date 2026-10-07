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
   dotnet add package Microsoft.Extensions.DependencyInjection
   dotnet add package Microsoft.Extensions.Configuration.Json   # or reference Microsoft.Extensions.Hosting, which brings both
   ```
2. **Write your Daml** template(s) and a `daml.yaml`.
3. **Generate bindings** with `dpm codegen-cs --dar <your.dar> --out <dir>`
   (crib `codegen/daml.yaml` for the OCI component pin, and `scripts/codegen.sh` for the build →
   codegen → commit flow).
4. **Register the client.** On gRPC, `AddCantonLedger(configuration)` (from `Canton.Ledger.Grpc.Client`)
   wires client and auth together from the root configuration (`Canton:Ledger` and `Canton:Auth`). On
   JSON/REST there is no root-configuration convention call like it: pass the two sections to
   `services.AddRestLedgerClient(configuration.GetSection("Canton:Rest"), configuration.GetSection("Canton:Auth"))`, which also registers the token provider. The demo uses
   `AddCantonStaticAuth` + `AddLedgerClient` / `AddRestLedgerClient` because its token comes from the
   LocalNet fixture. Then inject `ICantonLedgerClient` when
   you query interface views, or the narrower `ILedgerClient` / `ILedgerWriter` / `ILedgerStreamer`
   otherwise. Every one of them is an abstraction both transports register, so the transport stays a
   composition-root choice.

   A minimal `appsettings.json` that works against a stock local LocalNet (the token endpoint,
   client id and client secret are the LocalNet defaults the demo itself uses; the user id is the
   `a-validator-1` validator's ledger user, which the demo prints in its
   `Granted act-as … to ledger user <id>` line):

   ```json
   {
     "Canton": {
       "Ledger": { "GrpcAddress": "http://localhost:11901", "UserId": "<validator ledger user id>" },
       "Rest": { "HttpAddress": "http://localhost:11975/", "UserId": "<validator ledger user id>" },
       "Auth": {
         "TokenEndpoint": "http://localhost:8082/realms/AValidator1/protocol/openid-connect/token",
         "ClientId": "a-validator-1-validator",
         "ClientSecret": "AL8648b9SfdTFImq7FV56Vd0KHifHBuC",
         "AllowInsecureTokenEndpoint": true
       }
     }
   }
   ```

   `AllowInsecureTokenEndpoint` is needed only because LocalNet's Keycloak speaks plain `http`; without
   it startup fails with an `OptionsValidationException` ("The token endpoint uses plaintext http…").
   Leave it out against a real `https` identity provider. `Canton:Ledger` feeds
   `AddCantonLedger(configuration)` on gRPC, and `Canton:Rest` feeds `AddRestLedgerClient`; load the file with
   `new ConfigurationBuilder().AddJsonFile("appsettings.json").Build()`.

   `AddJsonFile` resolves against the application's output directory, so copy the file there from
   your `.csproj`; without it `dotnet run` fails with "The configuration file 'appsettings.json' was
   not found":

   ```xml
   <ItemGroup>
     <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
   </ItemGroup>
   ```
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
