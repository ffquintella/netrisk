@echo off
docker.exe run --network="bridge" --env-file=dev-env-backgroundjobs.list --rm --entrypoint /bin/bash -ti ffquintella/netrisk-backgroundjobs:0.50.1 