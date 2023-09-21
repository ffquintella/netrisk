# == Class: srnet::params
#
# Defines default values for srnet module
#
class netrisk::console (
  $server_logging = $netrisk::params::server_logging,
  
  # Database Settings
  $dbserver   = $netrisk::params::dbserver,
  $dbuser     = $netrisk::params::dbuser,
  $dbport     = $netrisk::params::dbport,
  $dbpassword = $netrisk::params::dbpassword,
  $dbschema   = $netrisk::params::dbschema,
  

  
) inherits netrisk::params  {

  file{'/netrisk/appsettings.json':
    ensure  => file,
    content => epp('netrisk/console/appsettings.json.epp', {
      'server_logging' => $server_logging,
      'db_server'   => $dbserver,
      'db_user'     => $dbuser,
      'db_port'     => $dbport ,
      'db_password' => $dbpassword ,
      'db_schema'   => $dbschema
    })
  }


}
