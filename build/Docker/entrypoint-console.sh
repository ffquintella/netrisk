#!/bin/bash

set -eo pipefail

set_config(){
	# If the config.php hasn't already been configured
	if [ ! -f /configurations/netrisk-config-configured ]; then
		CONFIG_PATH='/netrisk'
		CONFIG_DEFAULTS_PATH='/netrisk/defaults/*'

		cp -rf $CONFIG_DEFAULTS_PATH $CONFIG_PATH

    config_netrisk

		# Create a file so this doesn't run again
		touch /configurations/netrisk-config-configured
	fi
}

unset_variables() {
	unset NETRISK_DB_HOSTNAME
}

config_netrisk(){
	/opt/puppetlabs/bin/puppet apply --modulepath=/etc/puppet/modules /etc/puppet/manifests/start.pp 
}

# Holds the container up so operators can reach the console client with
# `docker exec netrisk-<env>_console netrisk-console <command>` (docs/product-guides/installation.md).
start_console_keepalive(){
  /bin/tail -f /dev/null
}

# Security finding NR-2026-025 moved the database credential out of appsettings.json and into
# /netrisk/netrisk.env, mode 0600, owned by the service account. The 2.17.0 regression that followed
# was in how this script read it back.
#
# That file is a literal KEY=VALUE environment file -- Docker's `--env-file` format -- and not a
# shell script. Reading it with `.` made the shell parse the value, and the connection string
# contains `;`, which is a command separator:
#
#   Database__ConnectionString=server=10.0.0.1;port=4306;uid=netrisk;pwd=...;database=netrisk
#
# set Database__ConnectionString to `server=10.0.0.1` and then ran `port=4306`, `uid=...` and the
# rest as five unrelated assignments. With no port left in the connection string, MySqlConnector
# used its default 3306, the database self test timed out after 15s, and the host exited on every
# start -- so systemd restarted it forever.
#
# Exporting the raw remainder of each line keeps every character of the value -- `;`, `$`, quotes,
# backticks, spaces -- exactly as Puppet wrote it. Tested by
# Packaging.Tests/DeploymentSecretPlacementTest, which runs this very function over a rendered
# template.
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

# The deployed console container is a keepalive, not a one-shot command runner. Dockerfile-ConsoleClient
# declares `ENTRYPOINT [ "/entrypoint.sh" ]` and no `CMD`, and the generated host launcher
# (/usr/local/bin/docker-run-netrisk-dsv_console-start.sh on apldc1vds0044) ends its `docker create`
# at the image name -- no command follows it. So in production "$@" is empty, and the container
# exists only to hold the console client for `docker exec`.
#
# It used to call start_console_keepalive unconditionally, which blocks forever in `tail -f`, leaving
# the `exec "$@"` below it unreachable. Harmless on the no-argument production path and a trap on any
# other: `docker run <image> netrisk-console database init` printed nothing and hung instead of
# running the command. Execing first when arguments are present leaves the deployed path
# byte-for-byte identical and makes the argument form do what it reads as.
_main() {
	set_config
	load_netrisk_env

	if [ "$#" -gt 0 ]; then
		exec "$@"
	fi

	start_console_keepalive
}

_main "$@"