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

start_console_keepalive(){
  /bin/tail -f /dev/null
}

_main() {
	set_config
	start_console_keepalive
	exec "$@"
}

_main "$@"