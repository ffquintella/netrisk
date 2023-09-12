
if $dbserver == undef {
  fail('dbserver is not defined')
}
if $dbuser == undef {
  fail('dbuser is not defined')
}
if $dbport == undef {
  fail('dbport is not defined')
}
if $dbpassword == undef {
  fail('dbpassword is not defined')
}
if $dbschema == undef {
  fail('dbschema is not defined')
}

if $server_certificate_file == undef {
  # This file will do the initial configuration of netrisk and start the service
  class { 'netrisk::console':
    netrisk_url       => $netrisk_url,
    dbserver          => $dbserver,
    dbuser            => $dbuser,
    dbport            => $dbport,
    dbpassword        => $dbpassword,
    dbschema          => $dbschema,
    server_logging    => $server_logging,
    server_https_port => 0 + $server_https_port
  }
}else{
  class { 'netrisk::console':
    netrisk_url       => $netrisk_url,
    dbserver          => $dbserver,
    dbuser            => $dbuser,
    dbport            => $dbport,
    dbpassword        => $dbpassword,
    dbschema          => $dbschema,
    server_logging    => $server_logging,
    server_https_port => 0 + $server_https_port,
    server_certificate_file => $server_certificate_file,
    server_certificate_pwd  => $server_certificate_pwd
  }
}
