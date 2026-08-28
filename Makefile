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

# Docker coordinates for `make docker-release`. The Nuke Docker targets tag
# every image as $(DOCKER_REGISTRY)/<name>:<version>, so the push loop has to
# resolve the same version build/Build.cs resolves for VersionClean: the newest
# of the `Releases/*` git tags and the version in src/Directory.Build.props,
# truncated to Major.Minor.Patch. Override DOCKER_VERSION to push a tag built
# earlier, or DOCKER_REGISTRY to publish somewhere other than Docker Hub.
DOCKER_REGISTRY ?= ffquintella
DOCKER_IMAGES   ?= netrisk-api netrisk-website netrisk-console netrisk-backgroundjobs
DOCKER_VERSION  ?= $(shell { git -C "$(ROOT)" tag -l 'Releases/*' 2>/dev/null | sed -e 's|^Releases/||'; \
                             sed -n -e 's|.*<AssemblyVersion>\([^<]*\)</AssemblyVersion>.*|\1|p' \
                                    -e 's|.*<Version>\([^<]*\)</Version>.*|\1|p' \
                                    "$(ROOT)/src/Directory.Build.props"; } \
                           | sed -n 's|^\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\).*|\1|p' \
                           | sort -t. -k1,1n -k2,2n -k3,3n | tail -1)

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

## docker-release: build all Docker images (Release) and push every one
docker-release:
	$(NUKE) CreateAllDockerImages --configuration Release $(ARGS)
	@test -n "$(DOCKER_VERSION)" || { \
	  echo "could not resolve the image version -- pass DOCKER_VERSION=<x.y.z>"; exit 2; }
	@set -e; \
	echo "==> verifying images for version $(DOCKER_VERSION)"; \
	for image in $(DOCKER_IMAGES); do \
	  tag="$(DOCKER_REGISTRY)/$$image:$(DOCKER_VERSION)"; \
	  docker image inspect "$$tag" >/dev/null 2>&1 || { \
	    echo "no such image: $$tag -- CreateAllDockerImages did not tag it (version mismatch?)"; \
	    exit 1; }; \
	done; \
	for image in $(DOCKER_IMAGES); do \
	  tag="$(DOCKER_REGISTRY)/$$image:$(DOCKER_VERSION)"; \
	  echo "==> docker push $$tag"; \
	  docker push "$$tag"; \
	done

## clean: Nuke clean (build outputs and artifacts)
clean:
	$(NUKE) Clean

.PHONY: help gui api website jobs console build restore test coverage \
        nuke nuke-targets db-update migration-add migrations-list \
        docker-release clean
