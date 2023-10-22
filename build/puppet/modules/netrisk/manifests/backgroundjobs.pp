# == Class: srnet::params
#
# Defines default values for srnet module
#
class netrisk::backgroundjobs (
  $netrisk_url = $netrisk::params::netrisk_url,
  
  # Database Settings
  $dbserver   = $netrisk::params::dbserver,
  $dbuser     = $netrisk::params::dbuser,
  $dbport     = $netrisk::params::dbport,
  $dbpassword = $netrisk::params::dbpassword,
  $dbschema   = $netrisk::params::dbschema,

  #Server
  $server_logging          = $netrisk::params::server_logging,


  $user = $netrisk::params::user,
  $uid  = $netrisk::params::uid,
  
  
) inherits netrisk::params  {

  file{'/netrisk/appsettings.json':
    ensure  => file,
    owner   => $user,
    content => epp('netrisk/backgroundJobs/appsettings.json.epp', {
      'server_url'     => $netrisk_url,
      'server_logging' => $server_logging,
      'db_server'   => $dbserver,
      'db_user'     => $dbuser,
      'db_port'     => $dbport ,
      'db_password' => $dbpassword ,
      'db_schema'   => $dbschema
    })
  }
}
