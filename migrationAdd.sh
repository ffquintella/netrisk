#!/bin/bash

# Check if the script has received N parameters
N=1

if [ $# -lt $N ]; then
  echo "Error: This script requires at least $N parameters."
  exit 1
fi

# Access the first parameter
migrationName=$1

#RUN
dotnet ef migrations add $migrationName --project src/DAL/DAL.csproj --startup-project src/ConsoleClient/ConsoleClient.csproj --context NRDbContext