#!/bin/bash

# Check if the script has received N parameters
N=0

if [ $# -lt $N ]; then
  echo "Error: This script requires at least $N parameters."
  exit 1
fi

#RUN
dotnet ef database update --project src/DAL/DAL.csproj --startup-project src/ConsoleClient/ConsoleClient.csproj --context NRDbContext