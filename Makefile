# NetRisk developer shortcuts.
#
# This is a thin, discoverable wrapper around the commands documented in
# CLAUDE.md (Nuke build, dotnet run/test, EF migration scripts). Every target
# ends up calling the same tooling those docs describe -- nothing new is
# implemented here.
#
# Run `make` (or `make help`) to list the available targets.

SHELL := /bin/bash

# Repo root, regardless of where make was invoked from.
ROOT := $(patsubst %/,%,$(dir $(abspath $(lastword $(MAKEFILE_LIST)))))

# Environment passed to the desktop client (`--environment=$(ENV)`).
ENV ?= dev

# Extra arguments forwarded to the underlying tool, e.g.
#   make gui ARGS="--verbose"
ARGS ?=

DOTNET ?= dotnet
NUKE   := $(ROOT)/build.sh
SLN    := $(ROOT)/src/netrisk.sln

.DEFAULT_GOAL := help

## help: list the available targets (default)
help:
	@echo "NetRisk -- make targets"
	@echo ""
	@grep -hE '^## ' $(MAKEFILE_LIST) \
	| sed -e 's/^## //' \
	| awk -F': *' '{ printf "  \033[1m%-22s\033[0m %s\n", $$1, $$2 }'
	@echo ""
	@echo "Variables: ENV=$(ENV) (desktop environment), ARGS (extra tool args)"
	@echo "Example:   make gui ENV=dev"

# Every runnable project resolves its configuration base path from the *current
# working directory*, not from the project directory: GUIClient calls
# SetBasePath(Directory.GetCurrentDirectory()) and the ASP.NET hosts default
# their content root to the same thing. `dotnet run --project <path>` keeps the
# caller's working directory, so running from the repo root makes the app look
# for appsettings*.json in the repo root and die with a FileNotFoundException.
# Hence the `cd` -- these targets must run from inside the project directory.
## gui: run the Avalonia desktop client (GUIClient)
gui:
	cd $(ROOT)/src/GUIClient && $(DOTNET) run -- --environment=$(ENV) $(ARGS)

## api: run the REST API
api:
	cd $(ROOT)/src/API && $(DOTNET) run $(ARGS)

## website: run the public website
website:
	cd $(ROOT)/src/WebSite && $(DOTNET) run $(ARGS)

## jobs: run the Hangfire background jobs host
jobs:
	cd $(ROOT)/src/BackgroundJobs && $(DOTNET) run $(ARGS)

## console: run the console client (pass args via ARGS)
console:
	cd $(ROOT)/src/ConsoleClient && $(DOTNET) run -- $(ARGS)

## build: plain compile of the whole solution
build:
	$(DOTNET) build $(SLN) $(ARGS)

## restore: restore NuGet packages for the solution
restore:
	$(DOTNET) restore $(SLN) $(ARGS)

## test: run all tests
test:
	$(DOTNET) test $(SLN) $(ARGS)

## coverage: run all tests and write Cobertura coverage to TestResults/
coverage:
	$(DOTNET) test $(SLN) --coverage --coverage-output-format cobertura $(ARGS)

## nuke: run a Nuke target, e.g. make nuke TARGET=PackageMacGUI
nuke:
	@test -n "$(TARGET)" || { echo "usage: make nuke TARGET=<NukeTarget>"; exit 2; }
	$(NUKE) $(TARGET) $(ARGS)

## nuke-targets: list the Nuke build targets
nuke-targets:
	$(NUKE) --help

## db-update: apply EF migrations to the configured database
db-update:
	$(ROOT)/databaseUpdate.sh

## migration-add: add an EF migration, e.g. make migration-add NAME=AddFoo
migration-add:
	@test -n "$(NAME)" || { echo "usage: make migration-add NAME=<MigrationName>"; exit 2; }
	$(ROOT)/migrationAdd.sh $(NAME)

## migrations-list: list the EF migrations
migrations-list:
	$(ROOT)/migrationsList.sh

## clean: Nuke clean (build outputs and artifacts)
clean:
	$(NUKE) Clean

.PHONY: help gui api website jobs console build restore test coverage \
        nuke nuke-targets db-update migration-add migrations-list clean
