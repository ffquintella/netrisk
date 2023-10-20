

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

file{'/var/log/netrisk':
  ensure => 'directory',
  mode => '755',
  owner => $netrisk_user,
  recurse => true,
}


# This file will do the initial configuration of netrisk and start the service
class { 'netrisk::backgroundjobs':
  netrisk_url  => $netrisk_url,
  dbserver   => $dbserver,
  dbuser     => $dbuser,
  dbport     => Integer($dbport),
  dbpassword => $dbpassword,
  dbschema   => $dbschema,
  user                    => $netrisk_user,
  uid                     => $netrisk_uid,
}

