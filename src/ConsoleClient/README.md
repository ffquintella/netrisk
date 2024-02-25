# Console Client

This app is meant to be used on the same server as the API and is basically to do some advanced administrative functions.

## SETUP
Please first setup the dotnet user-secrets (if in development) or add the 
connection string on appsetting.json

Command: dotnet user-secrets set "Database:ConnectionString" "server=X.X.X.X;Port=1234;uid=YYYY;pwd=JDHFI;database=netrisk;ConvertZeroDateTime=True"