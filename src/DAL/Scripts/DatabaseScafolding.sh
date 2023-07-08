#!/usr/bin/fish

echo Starting scaffold ....

dotnet ef dbcontext scaffold 'server=127.0.0.1;uid='$argv[1]';pwd='$argv[2]';database=simplerisk;ConvertZeroDateTime=True' --project DAL.csproj --startup-project DAL.csproj --configuration Debug --framework net7.0  Pomelo.EntityFrameworkCore.MySql --context SRDbContext --context-dir Context --output-dir Entities --schema simplerisk --force 
