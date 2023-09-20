@echo off
docker.exe run --env-file=dev-env-console.list --rm --entrypoint /bin/bash -ti ffquintella/netrisk-console:0.50.1 