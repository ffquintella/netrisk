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

# Security finding NR-2026-025. The database credential is no longer rendered into
# appsettings.json; Puppet writes it to /netrisk/netrisk.env with mode 0600 owned by the service
# account, and it reaches the process as Database__ConnectionString. Sourced inside the `sudo -u`
# shell rather than exported out here on purpose: sudo scrubs the environment by default, and
# exporting it in this shell would also hand it to every other child of the entrypoint.

start_netrisk(){
  export ASPNETCORE_ENVIRONMENT=production
  export DOTNET_USER_SECRETS_FALLBACK_DIR=/tmp
	cd /netrisk/
	sudo -u netrisk bash -c 'set -a; [ -r /netrisk/netrisk.env ] && . /netrisk/netrisk.env; set +a; cd /netrisk; /netrisk/WebSite' 
}


_main() {
	set_config
	configure_netrisk
	start_netrisk
}

_main 