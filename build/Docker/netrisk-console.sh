#!/bin/bash
#
# `netrisk-console` -- the operator entry point inside the deployed console container.
#
# The console container is a keepalive: entrypoint-console.sh ends in `tail -f /dev/null` and the
# generated host launcher passes no command, so every operator command reaches it as
# `docker exec netrisk-<env>_console netrisk-console <command>`. That is the only path in, and it is
# the reason this wrapper has to load the environment file itself.
#
# What it fixes: security finding NR-2026-025 moved the database credential out of appsettings.json
# into /netrisk/netrisk.env, and the entrypoint loads it with load_netrisk_env into *its own*
# environment -- PID 1's. `docker exec` inherits none of that; it builds a fresh environment from the
# image config, which sets no Database__ConnectionString, and the Puppet-rendered
# /netrisk/appsettings.json deliberately carries no connection string either (only a comment saying
# where it went). So Configuration["Database:ConnectionString"] came back null, MySqlConnector fell
# back to its default localhost:3306, and every `netrisk-console database ...` command died with
# `Unable to connect to any of the specified MySQL hosts` -- naming a database server that had never
# been configured instead of the setting that was missing. The database is a separate container, so
# nothing answers on localhost and the refusal is immediate rather than a timeout.
#
# Three details that are not cosmetic:
#
#  * The loader below is a byte-identical copy of the one in the four entrypoints, and
#    Packaging.Tests/DeploymentEnvironmentFileLoaderTest asserts all five stay that way. It reads the
#    file line by line and exports the raw remainder of each one rather than sourcing it, because the
#    value is a connection string full of `;` -- a command separator -- and sourcing it is what
#    caused the 2.17.0 restart loop.
#  * The `cd` is required, not tidiness. Host.CreateDefaultBuilder resolves appsettings.json against
#    the current working directory and the console registers it with `optional: false`, so running
#    the binary from anywhere else fails at start-up. The numbered upgrade SQL and
#    DatabaseInformation.yaml are resolved from the assembly directory instead, so this one file is
#    the only thing that depends on the working directory.
#  * A missing credential warns rather than failing. Plenty of subcommands need no database, and this
#    wrapper is not the right place to decide which -- but staying silent is what made the original
#    failure look like a network problem, so it does not stay silent.

set -eo pipefail

load_netrisk_env() {
	[ -r /netrisk/netrisk.env ] || return 0

	local line key
	while IFS= read -r line || [ -n "$line" ]; do
		if [[ $line == \#* || $line != *=* ]]; then continue; fi

		key=${line%%=*}
		if [[ ! $key =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]]; then continue; fi

		export "$key=${line#*=}"
	done < /netrisk/netrisk.env
}

load_netrisk_env

if [ -z "${Database__ConnectionString:-}" ]; then
	echo "netrisk-console: warning: Database__ConnectionString is not set and /netrisk/netrisk.env" \
	     "supplied nothing. Any database command will try localhost and fail. Check that Puppet has" \
	     "written /netrisk/netrisk.env and that it is readable by this user." >&2
fi

cd /netrisk
exec ./ConsoleClient "$@"
