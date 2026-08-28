# ⚙️ Installation

## Step 1 - Create a database

NetRisk is projected to work with MariaDB and the first step is to create a database and a user to connect. We do not include here instructions on how to install or configure MariaDB, but it should be pretty easy to find online.&#x20;



You will need a database ( you can choose any name here, we selected netriskdb).&#x20;

To do so, connect to maria db a use:

```sql
CREATE DATABASE netriskdb
  CHARACTER SET = 'utf8mb4'
  COLLATE = 'utf8mb4_unicode_ci';
```

Now you need to create a user with?

{% hint style="info" %}
Always choose a meaningful username and secure, random password!&#x20;

(The values here are only examples. NEVER USER THEM)
{% endhint %}

```sql
CREATE USER netriskdbuser@localhost IDENTIFIED BY 'supersecretpassword';
```

## Step 2 - Install Application - Using Puppet

Our preferred way to install NetRisk is using puppet and the dockerapp\_netrisk module that can be found on puppet forge [here](https://forge.puppet.com/modules/ffquintella/dockerapp\_netrisk/readme).&#x20;

The module will install the application using docker and download the images.&#x20;

The best way to configure it is using hiera with these parameters:

```yaml
---
classes:
  - dockerapp_netrisk

dockerapp_netrisk::service_name: netrisk
dockerapp_netrisk::version: '0.52.1'
dockerapp_netrisk::api_server: localhost
dockerapp_netrisk::api_protocol: https
dockerapp_netrisk::api_port: 5443
dockerapp_netrisk::website_port: 6443
dockerapp_netrisk::website_protocol: https
dockerapp_netrisk::website_server: localhost
dockerapp_netrisk::db_server: localhost
dockerapp_netrisk::db_port: 3306
dockerapp_netrisk::db_schema: netriskdb
dockerapp_netrisk::db_user: netriskdbuser
dockerapp_netrisk::db_password: supersecretpassword
dockerapp_netrisk::api_ssl_cert_file: /etc/pki/certs/netrisk.pfx
dockerapp_netrisk::api_ssl_cert_pwd: xxx
dockerapp_netrisk::website_ssl_cert_file: /etc/pki/certs/netrisk.pfx
dockerapp_netrisk::website_ssl_cert_pwd: xxx
dockerapp_netrisk::logging: Information
dockerapp_netrisk::email_from: netrisk@localhost
dockerapp_netrisk::email_server: smtp.netrisk.app
dockerapp_netrisk::email_port: 25
dockerapp_netrisk::enable_api: true
dockerapp_netrisk::enable_website: true
dockerapp_netrisk::enable_console: true 
```



## Step 3 - Initialize DB

Using the console client, initialize the database with the following command:

```
netrisk-console database init
```



## Step 4 - Create the first user

Now you need to create your first application user. To do so, use the following command on the console client:

```
 netrisk-console user add
```

{% hint style="info" %}
You can list your current users with the command: ConsoleClient user list
{% endhint %}



## Running console commands on a deployed host

Every `netrisk-console` command in this documentation runs inside the console container. That
container is a keepalive — its entrypoint ends in `tail -f /dev/null` — so commands reach it through
`docker exec`, and the image puts the launcher on `PATH` for exactly that:

```
sudo docker exec -ti netrisk-<env>_console netrisk-console database status
```

Use `netrisk-console` and not `/netrisk/ConsoleClient`. The two are not interchangeable:

* **`docker exec` does not inherit the entrypoint's environment.** It builds a fresh one from the
  image configuration. The database credential is not in the image — security finding NR-2026-025
  moved it out of `appsettings.json` into `/netrisk/netrisk.env`, mode 0600, which the entrypoint
  loads into PID 1 and nowhere else. The launcher re-reads that file per invocation.
* **The working directory matters.** `appsettings.json` is resolved against it and registered as
  non-optional, so the binary has to run from `/netrisk`. The launcher does the `cd`.

Console images from 2.17.4 on also read `/netrisk/netrisk.env` in the binary itself, so a direct
`/netrisk/ConsoleClient` invocation resolves the credential too — but it still needs the right
working directory, and older images do not have that fallback. `NETRISK_ENV_FILE` overrides the path
when running the binary outside a container.

{% hint style="warning" %}
If your host has its own `/usr/local/bin/netrisk-console` wrapper, check what it runs. A wrapper
predating 2.17.0 typically contains
`docker exec -ti <container> /bin/bash -c "cd /netrisk; /netrisk/ConsoleClient $1 $2 $3 $4"`, which
bypasses the in-image launcher and truncates your command at four arguments. The correct body is:

```bash
#!/usr/bin/env bash
exec docker exec -ti "netrisk-<env>_console" netrisk-console "$@"
```

Do not have the wrapper `source` `/netrisk/netrisk.env` to fix this. The connection string contains
`;`, which the shell reads as a command separator; that is the 2.17.0 regression the entrypoints
document at length.
{% endhint %}
