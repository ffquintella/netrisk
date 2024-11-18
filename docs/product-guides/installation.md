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

It is also recomended to increase the max-allowed-packet in my.ini

To do so using docker mount a folder in /etc/mysql/conf.d and add a file finishing with .cnf.&#x20;

The file should contain:&#x20;

```
[mariadb] 
max_allowed_packet=500M
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
