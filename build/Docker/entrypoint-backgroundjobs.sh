#!/bin/bash

set -eo pipefail

set_config(){
	# If the config.php hasn't already been configured
	if [ ! -f /configurations/netrisk-config-configured ]; then
		CONFIG_PATH='/netrisk'
		CONFIG_DEFAULTS_PATH='/netrisk/defaults/*'

		cp -rf $CONFIG_DEFAULTS_PATH $CONFIG_PATH

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

# The loader is shipped into the sudo shell with `declare -f` and the file is read on the far side
# of the privilege drop, on purpose: sudo scrubs the environment by default, and exporting the
# credential out here would also hand it to every other child of the entrypoint.

start_netrisk(){
  export ASPNETCORE_ENVIRONMENT=production
  export DOTNET_USER_SECRETS_FALLBACK_DIR=/tmp
	cd /netrisk/
	sudo -u netrisk bash -c "$(declare -f load_netrisk_env); load_netrisk_env; cd /netrisk; exec /netrisk/BackgroundJobs"
}


_main() {
	set_config
	config_netrisk
	start_netrisk
}

_main 