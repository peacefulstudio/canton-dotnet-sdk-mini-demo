# Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.
# SPDX-License-Identifier: Apache-2.0

.PHONY: help codegen daml-test build run clean

help:
	@echo "Targets:"
	@echo "  make codegen   Build the Daml package and regenerate committed C# bindings"
	@echo "  make daml-test Run the Daml Script tests under daml/test (accept, reject, withdraw)"
	@echo "  make build     dotnet build MiniDemo.slnx"
	@echo "  make run       dotnet run --project src/MiniDemo (needs a running LocalNet; env vars optional)"
	@echo "                 REQUIRE_PQS=1 make run adds --require-pqs (PQS short of full projection exits 69)"
	@echo "  make clean     Remove build output"
	@echo ""
	@echo "LocalNet itself is NOT started here. Bring it up from peacefulstudio/canton-localnet"
	@echo "(e.g. 'make up' in that repo). Against a stock local LocalNet no config is needed;"
	@echo "override endpoints via CANTON_LOCALNET_* env vars only for a non-default/remote setup."

codegen:
	./scripts/codegen.sh

daml-test:
	cd daml && dpm build -o test/.daml/lib/canton-mini-demo.dar
	cd daml/test && dpm test

build:
	dotnet build MiniDemo.slnx

run:
	dotnet run --project src/MiniDemo $(if $(REQUIRE_PQS),-- --require-pqs)

clean:
	dotnet clean MiniDemo.slnx || true
	rm -rf daml/.daml daml/test/.daml
