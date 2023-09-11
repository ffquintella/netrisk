@echo off
docker.exe run --env-file=dev-env.list --rm --entrypoint /bin/bash -ti ffquintella/netrisk-api:0.50.1 