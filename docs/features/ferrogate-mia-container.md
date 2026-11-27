# FerroGate MIA in the containers

The `netrisk-api` and `netrisk-backgroundjobs` images include the FerroGate Machine Identity Agent
(`mia`), installed from the FerroGate release RPM. The entrypoint starts it before the application,
so NetRisk can mint tokens for FerroGate-protected data through the helper socket
(`/run/ferrogate/mia.sock`, group `ferrogate-clients`, which the `netrisk` account belongs to).

## Configuring it

Precedence: built-in defaults < `/etc/ferrogate/mia.toml` < `FERROGATE_*` environment variables.

```bash
# mount the file
docker run -v ./mia.toml:/etc/ferrogate/mia.toml:ro -v mia-state:/var/lib/ferrogate ffquintella/netrisk-api:<v>

# or pass parameters
docker run -e FERROGATE_CMIS_ENDPOINT=https://cmis.example.com:8443 \
           -e FERROGATE_CMIS_SPKI_PIN=<hex-sha384> -v mia-state:/var/lib/ferrogate ffquintella/netrisk-api:<v>
```

Every `mia.toml` key has a `FERROGATE_*` equivalent (FerroGate `docs/mia.md`). The packaged defaults are
seeded into `/etc/ferrogate` only for files you did not provide.

| Container switch | Default | Meaning |
|---|---|---|
| `MIA_MODE` | `auto` | `embedded` runs an agent in the container; `host` uses the host's agent; `off` disables; `auto` picks `host` when the host socket is mounted, else `embedded` when a CMIS endpoint/SRV is configured, else `off` |
| `MIA_REQUIRED` | `0` | `1` aborts the container if the agent (or the host socket) is not available |
| `MIA_WAIT_SECS` | `15` | wait for the helper socket |
| `MIA_ALLOW_EPHEMERAL_IDENTITY` | `0` | `1` lets the identity live in the container's writable layer (throw-away containers only) |
| `MIA_HOST_SOCKET_DIR` | `/run/ferrogate-host` | where the host's `/run/ferrogate` is mounted in host mode |

## Identity that survives restarts (embedded mode)

The host key in `/var/lib/ferrogate` is what CMIS pins; a container that loses it is rejected
(`host_rejected`) until a CMIS operator clears the pin. `/var/lib/ferrogate` is therefore **not** a
Dockerfile `VOLUME` (that would create a new anonymous volume per container) and the entrypoint
**refuses to start** unless it is mounted explicitly:

```bash
-v netrisk-mia-state:/var/lib/ferrogate        # named volume
-v /srv/netrisk/mia-state:/var/lib/ferrogate   # or a host directory (chowned to the agent account on start)
```

The log says whether an existing identity was reused or a first one is being created, and warns
about an anonymous volume. Do not share one state directory between two running containers.

## Using the host's agent (`MIA_MODE=host`)

The application identifies as the **host**: no agent is started in the container.

```bash
docker run -e MIA_MODE=host -v /run/ferrogate:/run/ferrogate-host:ro ffquintella/netrisk-api:<v>
```

- Mount the **directory**, not the socket file: the host agent recreates the socket when it restarts.
- The container's `netrisk` account joins a group with the socket's gid, so the `0660` socket is reachable
  without widening its mode; `FERROGATE_HELPER_SOCKET` is exported and passed across the privilege drop.
- The host's allowlist must admit the application by **binary hash**. A container is outside the agent's
  mount namespace, so path-constrained entries never match.

## Build arguments

`MIA_VERSION` (default `0.30.40`), `MIA_RPM_URL` (mirror or non-x86_64 RPM), `MIA_RPM_SHA256` (verified when set).

## Operational notes

- A container has no TPM; the embedded agent uses its `host-key` backend. Callers must be on the signed
  allowlist (`allowlist.key`).
- Not run end to end: image build, a real attestation and the host-socket path are unverified.
