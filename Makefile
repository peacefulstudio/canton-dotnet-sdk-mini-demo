# Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.
# SPDX-License-Identifier: Apache-2.0

.PHONY: help codegen build run clean

help:
	@echo "Targets:"
	@echo "  make codegen   Build the Daml package and regenerate committed C# bindings"
	@echo "  make build     dotnet build MiniDemo.slnx"
	@echo "  make run       dotnet run --project src/MiniDemo (needs a running LocalNet + env vars)"
	@echo "  make clean     Remove build output"
	@echo ""
	@echo "LocalNet itself is NOT started here. Bring it up from peacefulstudio/canton-localnet"
	@echo "(e.g. 'make up' in that repo) and export the CANTON_LOCALNET_* discovery env vars."

codegen:
	./scripts/codegen.sh

build:
	dotnet build MiniDemo.slnx

run:
	dotnet run --project src/MiniDemo

clean:
	dotnet clean MiniDemo.slnx || true
	rm -rf daml/.daml
