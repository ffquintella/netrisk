#!/bin/bash
# FerroGate MIA (Machine Identity Agent) inside the NetRisk containers.
#
# Sourced by entrypoint-api.sh and entrypoint-backgroundjobs.sh, which call `start_mia` before the
# application starts. The agent lets NetRisk mint DPoP-bound tokens for data protected by FerroGate
# (for example a BastionVault that requires machine identity) through the local helper socket.
#
# Configuration, lowest to highest precedence (the agent's own rule, docs/mia.md in FerroGate):
#
#   built-in defaults  <  /etc/ferrogate/mia.toml  <  FERROGATE_* environment variables
#
# so the container can be configured by mounting a file, by passing parameters, or by both:
#
#   docker run -v ./mia.toml:/etc/ferrogate/mia.toml:ro  ...        # mount the file
#   docker run -e FERROGATE_CMIS_ENDPOINT=https://cmis:8443 \
#              -e FERROGATE_CMIS_SPKI_PIN=<hex-sha384>   ...        # or pass parameters
#
# Container-only switches read here (not part of the agent):
#   MIA_MODE          auto (default) | embedded | host | off
#                       embedded  run an agent inside the container, with the container's own identity
#                       host      use the HOST's agent through its socket; the application then
#                                 identifies as the host (mount the host's /run/ferrogate on
#                                 MIA_HOST_SOCKET_DIR, no agent is started here)
#                       auto      host when the host socket is mounted, else embedded when a CMIS
#                                 endpoint is configured, else off
#   MIA_ENABLE        0 is an alias for MIA_MODE=off, 1 for MIA_MODE=embedded (older name)
#   MIA_REQUIRED      0 (default) | 1          1 aborts the container when the agent does not come up
#   MIA_WAIT_SECS     15 (default)             how long to wait for the helper socket
#   MIA_CONFIG_DIR    /etc/ferrogate           where the configuration lives (mount point)
#   MIA_STATE_DIR     /var/lib/ferrogate       the container's identity (embedded mode). MUST be a
#                                              bind mount or a named volume: see mia_check_identity_storage
#   MIA_ALLOW_EPHEMERAL_IDENTITY  0 (default)  1 lets the identity live in the container's writable
#                                              layer (throw-away containers only)
#   MIA_HOST_SOCKET_DIR  /run/ferrogate-host   where the host's /run/ferrogate is mounted (host mode)

MIA_CONFIG_DIR=${MIA_CONFIG_DIR:-/etc/ferrogate}
MIA_STATE_DIR=${MIA_STATE_DIR:-/var/lib/ferrogate}
MIA_DEFAULTS_DIR=${MIA_DEFAULTS_DIR:-/usr/share/ferrogate-defaults}
MIA_SERVICE_USER=_ferrogate
MIA_HOST_SOCKET_DIR=${MIA_HOST_SOCKET_DIR:-/run/ferrogate-host}
MIA_HOST_SOCKET=${MIA_HOST_SOCKET:-$MIA_HOST_SOCKET_DIR/mia.sock}

# Variables the application must see across `sudo -u netrisk`, which scrubs the environment. Used as
# `sudo --preserve-env=$MIA_PRESERVED_ENV`, so only these cross the privilege drop.
MIA_PRESERVED_ENV=FERROGATE_HELPER_SOCKET

# The packaged defaults are kept aside at build time because a bind mount over /etc/ferrogate hides
# them. A file the operator mounted or edited is never overwritten: only a missing one is seeded.
mia_seed_config() {
	[ -d "$MIA_DEFAULTS_DIR" ] || return 0
	mkdir -p "$MIA_CONFIG_DIR"

	local src name
	for src in "$MIA_DEFAULTS_DIR"/*; do
		[ -f "$src" ] || continue
		name=$(basename "$src")
		[ -e "$MIA_CONFIG_DIR/$name" ] || cp "$src" "$MIA_CONFIG_DIR/$name"
	done
}

# True when a CMIS endpoint is set by environment or by an uncommented `endpoint`/`srv` key.
mia_has_endpoint() {
	[ -n "${FERROGATE_CMIS_ENDPOINT:-}" ] || [ -n "${FERROGATE_CMIS_SRV:-}" ] && return 0

	local cfg
	for cfg in "${FERROGATE_CONFIG:-}" "$MIA_CONFIG_DIR"/mia.toml "$MIA_CONFIG_DIR"/mia-*.toml; do
		[ -n "$cfg" ] && [ -r "$cfg" ] || continue
		grep -Eq '^[[:space:]]*(endpoint|srv)[[:space:]]*=' "$cfg" && return 0
	done
	return 1
}

# The directory is mounted from the host, but the socket inside it is created by the host's agent and
# replaced whenever that agent restarts; mounting the directory (not the socket file) is what keeps
# the path valid across those restarts.
mia_host_socket_mounted() { [ -S "$MIA_HOST_SOCKET" ]; }

mia_mode() {
	local mode=${MIA_MODE:-}
	if [ -z "$mode" ]; then
		case "${MIA_ENABLE:-auto}" in
			0|false|no|off) mode=off ;;
			1|true|yes|on)  mode=embedded ;;
			*)              mode=auto ;;
		esac
	fi

	if [ "$mode" = auto ]; then
		if mia_host_socket_mounted; then mode=host
		elif mia_has_endpoint; then mode=embedded
		else mode=off
		fi
	fi
	echo "$mode"
}

mia_fail_or_warn() {
	echo "mia: $1" >&2
	if [ "${MIA_REQUIRED:-0}" = "1" ]; then
		echo "mia: MIA_REQUIRED=1, refusing to start NetRisk without the agent" >&2
		exit 1
	fi
}

# The identity (host-key.bin, svid-seed.bin) is what CMIS pins. A container that loses it mints a new
# one and is then refused (host_rejected) until a CMIS operator clears the pin, so a state directory
# that does not survive a restart is an outage waiting for the next `docker rm`. Declaring a VOLUME in
# the Dockerfile would not prevent it -- it creates a fresh anonymous volume per container -- so the
# entrypoint insists on an explicit mount instead.
mia_check_identity_storage() {
	local source
	source=$(awk -v d="$MIA_STATE_DIR" '$5 == d { print $4 }' /proc/self/mountinfo 2> /dev/null | tail -1)

	if [ -z "$source" ]; then
		if [ "${MIA_ALLOW_EPHEMERAL_IDENTITY:-0}" = "1" ]; then
			echo "mia: WARNING $MIA_STATE_DIR is not mounted; the identity is lost with this container" >&2
			return 0
		fi
		cat >&2 <<MSG
mia: $MIA_STATE_DIR is not a mount, so this container's machine identity would not survive a restart.
mia: mount it from the host or a named volume, for example
mia:     -v netrisk-mia-state:$MIA_STATE_DIR        (named volume)
mia:     -v /srv/netrisk/mia-state:$MIA_STATE_DIR   (host directory)
mia: or use the host's agent instead (MIA_MODE=host), or set MIA_ALLOW_EPHEMERAL_IDENTITY=1 for a throw-away container.
MSG
		return 1
	fi

	# An anonymous volume (a 64-hex name) is a mount, but it is gone with the container that made it.
	if [[ $source =~ /volumes/[0-9a-f]{64}/_data ]]; then
		echo "mia: WARNING $MIA_STATE_DIR is an anonymous volume; give it a name (-v name:$MIA_STATE_DIR) so a recreated container keeps its identity" >&2
	fi

	if [ -e "$MIA_STATE_DIR/host-key.bin" ]; then
		echo "mia: reusing the machine identity found in $MIA_STATE_DIR"
	else
		echo "mia: no machine identity in $MIA_STATE_DIR yet; the agent will create one (first start)"
	fi
	return 0
}

mia_wait_for_socket() {
	local socket=$1 pid=${2:-} waited=0 limit=${MIA_WAIT_SECS:-15}
	while [ "$waited" -lt "$limit" ]; do
		[ -S "$socket" ] && return 0
		if [ -n "$pid" ]; then kill -0 "$pid" 2> /dev/null || return 2; fi
		sleep 1
		waited=$((waited + 1))
	done
	return 1
}

# Host mode: the application talks to the host's agent, so it identifies as the host. Nothing is
# started here. The socket is group-owned on the host; the container's application account joins a
# group with that gid so the 0660 socket is reachable without widening its mode.
start_mia_host() {
	if ! mia_wait_for_socket "$MIA_HOST_SOCKET"; then
		mia_fail_or_warn "the host agent socket $MIA_HOST_SOCKET is not there; mount the host's /run/ferrogate on $MIA_HOST_SOCKET_DIR (-v /run/ferrogate:$MIA_HOST_SOCKET_DIR)"
		return 0
	fi

	local gid
	gid=$(stat -c %g "$MIA_HOST_SOCKET" 2> /dev/null || true)
	if [ -n "$gid" ] && [ "$gid" != 0 ] && ! id -G netrisk 2> /dev/null | tr ' ' '\n' | grep -qx "$gid"; then
		getent group "$gid" > /dev/null 2>&1 || groupadd --gid "$gid" ferrogate-host-clients
		usermod -aG "$gid" netrisk
	fi

	export FERROGATE_HELPER_SOCKET="$MIA_HOST_SOCKET"
	echo "mia: using the host agent at $MIA_HOST_SOCKET (the application identifies as the host)"
}

start_mia() {
	mia_seed_config

	local mode
	mode=$(mia_mode)

	case "$mode" in
		off)
			echo "mia: not started (MIA_MODE=${MIA_MODE:-auto}, no host socket and no CMIS endpoint configured)"
			return 0 ;;
		host)
			start_mia_host
			return 0 ;;
		embedded) ;;
		*)
			mia_fail_or_warn "unknown MIA_MODE '$mode' (expected auto, embedded, host or off)"
			return 0 ;;
	esac

	if ! command -v mia > /dev/null 2>&1; then
		mia_fail_or_warn "the mia binary is not installed in this image"
		return 0
	fi

	# Refusing is deliberate and not governed by MIA_REQUIRED: starting would mint an identity that
	# will not be there after the next restart.
	if ! mia_check_identity_storage; then
		exit 1
	fi

	# Both directories may be mounted, which arrives root-owned. The daemon starts as root and drops
	# to the service account itself, so it only needs these to exist and to be the account's.
	mkdir -p /run/ferrogate "$MIA_STATE_DIR"
	if getent passwd "$MIA_SERVICE_USER" > /dev/null 2>&1; then
		chown "$MIA_SERVICE_USER:$MIA_SERVICE_USER" "$MIA_STATE_DIR"
		chmod 0750 "$MIA_STATE_DIR"
	fi

	echo "mia: starting the machine identity agent"
	mia &
	local pid=$!

	local socket=${FERROGATE_HELPER_SOCKET:-/run/ferrogate/mia.sock} rc=0
	mia_wait_for_socket "$socket" "$pid" || rc=$?
	case $rc in
		0) echo "mia: helper socket ready at $socket" ;;
		1) mia_fail_or_warn "the helper socket $socket did not appear within ${MIA_WAIT_SECS:-15}s" ;;
		*) mia_fail_or_warn "the agent exited during start-up (see its log above)" ;;
	esac
	return 0
}
