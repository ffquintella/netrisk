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
  $user = $netrisk::params::user,
  $uid  = $netrisk::params::uid,
  

  
) inherits netrisk::params  {

  file{'/netrisk/appsettings.json':
    ensure  => file,
    owner   => $user,
    content => epp('netrisk/console/appsettings.json.epp', {
      'server_logging' => $server_logging,
      'db_server'   => $dbserver,
      'db_user'     => $dbuser,
      'db_port'     => Integer($dbport),
      'db_password' => $dbpassword ,
      'db_schema'   => $dbschema
    })
  }

  exec{'Console Keep Alive':
    cwd         => '/netrisk/',
    command     => '/bin/tail -f /dev/null',
    logoutput   => false
  }

}
