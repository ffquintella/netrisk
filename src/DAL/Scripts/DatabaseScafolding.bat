@echo off
REM: print new line
echo.

echo ---------------------------------------------------------------
echo Scaffolding Database
echo ---------------------------------------------------------------

REM: print new line
echo.

echo CONNECTION STRING: %1

REM: print new line
echo.

@echo on 

dotnet ef dbcontext scaffold %1 --project DAL.csproj --startup-project DAL.csproj --configuration Debug --framework net7.0  Pomelo.EntityFrameworkCore.MySql --context SRDbContext --context-dir Context --output-dir Entities --schema simplerisk --force 
