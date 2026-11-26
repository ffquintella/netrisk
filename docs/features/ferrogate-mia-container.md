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
| `MIA_ENABLE` | `auto` | `auto` starts the agent only when a CMIS endpoint/SRV is configured; `1`/`0` force it |
| `MIA_REQUIRED` | `0` | `1` aborts the container if the agent does not come up |
| `MIA_WAIT_SECS` | `15` | wait for the helper socket |

## Build arguments

`MIA_VERSION` (default `0.30.40`), `MIA_RPM_URL` (mirror or non-x86_64 RPM), `MIA_RPM_SHA256` (verified when set).

## Operational notes

- **Persist `/var/lib/ferrogate`.** The host key is the identity: CMIS pins it, and a regenerated one is rejected.
- A container has no TPM; MIA uses its `host-key` backend. Callers must be on the signed allowlist
  (`allowlist.key`), and since a container is outside MIA's mount namespace, path-constrained allowlist entries never match.
- Not run end to end: image build and attestation against a real CMIS are unverified.
