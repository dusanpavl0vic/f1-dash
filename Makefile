.PHONY: help fixture test test-backend test-web build check clean

DOTNET := ./backend/dotnet.sh

help:
	@echo "make fixture       download the 2024 Italian GP race archive (~75 MB)"
	@echo "make test          run every suite"
	@echo "make check         build, lint and test everything"

## Downloads the reference session. Everything downstream is verified against
## it, and the full-race classification test skips without it.
fixture:
	$(DOTNET) run --project src/F1Dash.Server --verbosity quiet -- archive 2024 Italian Race

fixtures-more:
	$(DOTNET) run --project src/F1Dash.Server --verbosity quiet -- archive 2024 Italian Qualifying
	$(DOTNET) run --project src/F1Dash.Server --verbosity quiet -- archive 2024 "Las Vegas" Race

test-backend:
	$(DOTNET) test --verbosity quiet

test-web:
	cd web && npm run test

test: test-backend test-web

build:
	$(DOTNET) build --verbosity quiet
	cd web && npm run build

check: build test
	cd web && npm run lint && npm run typecheck

clean:
	$(DOTNET) clean --verbosity quiet
	rm -rf web/dist
