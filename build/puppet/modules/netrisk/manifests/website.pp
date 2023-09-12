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
  

  
) inherits netrisk::params  {

  file{'/netrisk/appsettings.json':
    ensure  => file,
    content => epp('netrisk/api/appsettings.json.epp', {
      'server_url'     => $netrisk_url,
      'server_logging' => $server_logging,
      'sp_certificate_file'   => $sp_certificate_file,
      'sp_certificate_pwd'    => $sp_certificate_pwd,
      'db_server'   => $dbserver,
      'db_user'     => $dbuser,
      'db_port'     => $dbport ,
      'db_password' => $dbpassword ,
      'db_schema'   => $dbschema,
      'server_https_port'       => $server_https_port,
      'server_certificate_file' => $server_certificate_file,
      'server_certificate_pwd'  => $server_certificate_pwd
    })
  }



  exec{'Starting NetRisk Website Server':
    cwd         => '/netrisk/',
    command     => '/netrisk/WebSite &',
    environment => ['ASPNETCORE_ENVIRONMENT=production','DOTNET_USER_SECRETS_FALLBACK_DIR=/tmp'],
    user        => root,
    logoutput   => true
  }

}
