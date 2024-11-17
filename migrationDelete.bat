echo off

shift
shift

dotnet ef migrations remove  --project src/DAL/DAL.csproj --startup-project src/ConsoleClient/ConsoleClient.csproj --context NRDbContext