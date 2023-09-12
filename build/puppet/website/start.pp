
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

# This file will do the initial configuration of netrisk and start the service
class { 'netrisk::website':
  netrisk_url  => $netrisk_url,
  dbserver   => $dbserver,
  dbuser     => $dbuser,
  dbport     => $dbport,
  dbpassword => $dbpassword,
  dbschema   => $dbschema,
  sp_certificate_file       => $sp_certificate_file,
  sp_certificate_pwd        => $sp_certificate_pwd,
  server_logging          => $server_logging,
  server_https_port       => 0 + $server_https_port
}
