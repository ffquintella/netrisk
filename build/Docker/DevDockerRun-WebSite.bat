@echo off
docker.exe run -p 6443:6443 --env-file=dev-env-website.list --rm --entrypoint /bin/bash -ti ffquintella/netrisk-website:0.50.1 