echo off

set arg1=%1
set arg2=%2

shift
shift

dotnet ef migrations script %arg1% --project src/DAL/DAL.csproj --startup-project src/ConsoleClient/ConsoleClient.csproj --context NRDbContext > %arg2%/%arg1%.sql