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

configure_netrisk(){
	/opt/puppetlabs/bin/puppet apply --modulepath=/etc/puppet/modules /etc/puppet/manifests/start.pp 
}

start_netrisk_api(){
  export ASPNETCORE_ENVIRONMENT=production
  export DOTNET_USER_SECRETS_FALLBACK_DIR=/tmp
	cd /netrisk/
	sudo -u netrisk bash -c 'cd /netrisk/; /netrisk/API' 
}


_main() {
	set_config
	configure_netrisk
	start_netrisk_api
}

_main 