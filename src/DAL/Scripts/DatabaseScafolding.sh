#!/usr/bin/env fish

echo Starting scaffold ....

dotnet ef dbcontext scaffold  $argv[1] --project DAL.csproj --startup-project DAL.csproj --configuration Debug --framework net8.0  Pomelo.EntityFrameworkCore.MySql --context NRDbContext --context-dir Context --output-dir Entities --schema netrisk --force --no-onconfiguring 
