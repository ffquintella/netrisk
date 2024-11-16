echo off

set arg1=%1

shift
shift

dotnet ef migrations add %arg1% --project src/DAL/DAL.csproj --startup-project src/ConsoleClient/ConsoleClient.csproj --context NRDbContext