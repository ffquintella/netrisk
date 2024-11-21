#!/bin/bash

# Check if the script has received N parameters
N=2

if [ $# -lt $N ]; then
  echo "Error: This script requires at least $N parameters."
  exit 1
fi

# Access the first parameter
migrationName=$1
destinationDir=$2

dotnet ef migrations script $migrationName --project src/DAL/DAL.csproj --startup-project src/ConsoleClient/ConsoleClient.csproj --context NRDbContext > $destinationDir/$migrationName.sql