# == Class: srnet::params
#
# Defines default values for srnet module
#
class netrisk::website (
  $netrisk_url = $netrisk::params::netrisk_url,
  
  # Database Settings
  $dbserver   = $netrisk::params::dbserver,
  $dbuser     = $netrisk::params::dbuser,
  $dbport     = $netrisk::params::dbport,
  $dbpassword = $netrisk::params::dbpassword,
  $dbschema   = $netrisk::params::dbschema,

  #Server
  $server_logging          = $netrisk::params::server_logging,
  $server_https_port       = $netrisk::params::server_https_port,
  $server_certificate_file = $netrisk::params::server_certificate_file,
  $server_certificate_pwd  = $netrisk::params::server_certificate_pwd,
  $security_allow_development_certificate = $netrisk::params::security_allow_development_certificate,

  $user = $netrisk::params::user,
  $uid  = $netrisk::params::uid,
  
  
) inherits netrisk::params  {

  # Security finding NR-2026-025. The database credential is written to an environment file with
  # mode 0600 owned by the service account rather than into appsettings.json, and the entrypoint
  # sources it before starting the process. `show_diff => false` keeps the secret out of the Puppet
  # run report, which is otherwise a second place it ends up in plaintext.
  file{'/netrisk/netrisk.env':
    ensure    => file,
    owner     => $user,
    mode      => '0600',
    show_diff => false,
    content   => epp('netrisk/env/netrisk.env.epp', {
      'db_server'   => $dbserver,
      'db_user'     => $dbuser,
      'db_port'     => $dbport,
      'db_password' => $dbpassword,
      'db_schema'   => $dbschema,
    })
  }

  file{'/netrisk/appsettings.json':
    ensure  => file,
    owner   => $user,
    content => epp('netrisk/website/appsettings.json.epp', {
      'server_url'     => $netrisk_url,
      'server_logging' => $server_logging,
      'db_port'     => Integer($dbport),
      'server_https_port'       => $server_https_port,
      'server_certificate_file' => '/netrisk/website.pfx',
      'server_certificate_pwd'  => $server_certificate_pwd,
      'security_allow_development_certificate' => $security_allow_development_certificate
    })
  }
}
