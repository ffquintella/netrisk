---
description: >-
  If you are interested in helping on the NetRisk development please follow the
  bellow instructions
icon: gear-code
---

# Development

### CODE REPOSITORY

The code is avaliable at: [https://github.com/ffquintella/netrisk](https://github.com/ffquintella/netrisk)

### SETUP

To be able to develop using the described tools, you will need some requirements:

* To have .net 8 installed and visual studio or rider IDE (you can work with others, but we recommend one of those 2)
* Setup some .net secrets on some specific projects. Here basically you need to add a connection string on any project that needs to connect to the database (and NO this can´t be done on the DAL project). To do so follow the .net secrets procedure below

#### .NET Secrets procedure

To set up .net user secretes you can follow the steps bellow or search here for more info https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-7.0\&tabs=windows

* First init the secret on the project root dir with the following command: dotnet user-secrets init
* Then add a new secret in our case we need a datasource so: dotnet user-secrets set "Database:ConnectionString" "server=X.X.X.X;uid=YYYY;pwd=JDHFI;database=simplerisk;ConvertZeroDateTime=True"
* Finally, add the GuiClient secret dotnet user-secrets set "Server:Url" "https://127.0.0.1:5443"

### BUILD

We use nuke build to prepare the artifacts of the .net projects.

To use it you need to first install the nuke build tool on your machine with the following command: dotnet tool install Nuke.GlobalTool --global

Then you can run the build with the following command: nuke build
