
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

user{ $netrisk_user:
  ensure => 'present',
  home => '/netrisk',
  shell => '/bin/bash',
  managehome => true,
  uid => $netrisk_uid
}

class { 'netrisk::website':
  netrisk_url             => $netrisk_url,
  dbserver                => $dbserver,
  dbuser                  => $dbuser,
  dbport                  => $dbport,
  dbpassword              => $dbpassword,
  dbschema                => $dbschema,
  server_logging          => $server_logging,
  server_https_port       => $server_https_port.scanf('%d')[0],
  server_certificate_file => $server_certificate_file,
  server_certificate_pwd  => $server_certificate_pwd,
  user                    => $netrisk_user,
  uid                     => $netrisk_uid,
}

