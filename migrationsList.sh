#!/bin/bash

dotnet ef migrations list --project src/DAL/DAL.csproj --startup-project src/ConsoleClient/ConsoleClient.csproj --context NRDbContext