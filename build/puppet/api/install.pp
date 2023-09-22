package {'libicu':
  ensure => 'installed'
}

user {'netrisk':
  home => '/netrisk',
  shell => '/bin/bash',
  uid => '7070',
}

-> file{'/var/log/nrserver':
  ensure => 'directory',
  mode => '755',
  owner => 'netrisk',
  recurse => true,
}


file{'/netrisk':
  ensure => 'directory',
  mode => '755',
  owner => 'netrisk',
  recurse => true,
}->
file{'/var/netrisk':
  ensure => 'directory',
  mode => '755',
  owner => 'netrisk',
}

exec {'erase cache':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => 'rm -rf /var/cache/*'
} ->
exec {'erase logs':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => 'rm -rf /var/log/*'
}