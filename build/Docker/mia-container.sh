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
#   MIA_ENABLE        auto (default) | 1 | 0   auto starts the agent only when a CMIS endpoint is configured
#   MIA_REQUIRED      0 (default) | 1          1 aborts the container when the agent does not come up
#   MIA_WAIT_SECS     15 (default)             how long to wait for the helper socket
#   MIA_CONFIG_DIR    /etc/ferrogate           where the configuration lives (mount point)
#   MIA_STATE_DIR     /var/lib/ferrogate       the host identity: persist it (see the Dockerfile)

MIA_CONFIG_DIR=${MIA_CONFIG_DIR:-/etc/ferrogate}
MIA_STATE_DIR=${MIA_STATE_DIR:-/var/lib/ferrogate}
MIA_DEFAULTS_DIR=${MIA_DEFAULTS_DIR:-/usr/share/ferrogate-defaults}
MIA_SERVICE_USER=_ferrogate

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

mia_enabled() {
	case "${MIA_ENABLE:-auto}" in
		0|false|no|off) return 1 ;;
		1|true|yes|on)  return 0 ;;
		*)              mia_has_endpoint ;;
	esac
}

mia_fail_or_warn() {
	echo "mia: $1" >&2
	if [ "${MIA_REQUIRED:-0}" = "1" ]; then
		echo "mia: MIA_REQUIRED=1, refusing to start NetRisk without the agent" >&2
		exit 1
	fi
}

start_mia() {
	mia_seed_config

	if ! mia_enabled; then
		echo "mia: not started (MIA_ENABLE=${MIA_ENABLE:-auto}, no CMIS endpoint configured)"
		return 0
	fi

	if ! command -v mia > /dev/null 2>&1; then
		mia_fail_or_warn "the mia binary is not installed in this image"
		return 0
	fi

	# Both directories may be volumes, which arrive root-owned. The daemon starts as root and drops
	# to the service account itself, so it only needs these to exist and to be the account's.
	mkdir -p /run/ferrogate "$MIA_STATE_DIR"
	if getent passwd "$MIA_SERVICE_USER" > /dev/null 2>&1; then
		chown "$MIA_SERVICE_USER:$MIA_SERVICE_USER" "$MIA_STATE_DIR"
		chmod 0750 "$MIA_STATE_DIR"
	fi

	echo "mia: starting the machine identity agent"
	mia &
	local pid=$!

	local socket=${FERROGATE_HELPER_SOCKET:-/run/ferrogate/mia.sock}
	local waited=0 limit=${MIA_WAIT_SECS:-15}
	while [ "$waited" -lt "$limit" ]; do
		[ -S "$socket" ] && { echo "mia: helper socket ready at $socket"; return 0; }
		kill -0 "$pid" 2> /dev/null || break
		sleep 1
		waited=$((waited + 1))
	done

	if kill -0 "$pid" 2> /dev/null; then
		mia_fail_or_warn "the helper socket $socket did not appear within ${limit}s"
	else
		mia_fail_or_warn "the agent exited during start-up (see its log above)"
	fi
	return 0
}
