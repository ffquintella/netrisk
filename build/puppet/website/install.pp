package {'libicu':
  ensure => 'installed'
}

file{'/netrisk':
  ensure => 'directory',
  mode => '755'
}->
file{'/var/netrisk':
  ensure => 'directory',
  mode => '755'
}

user {'netrisk':
  home => '/netrisk',
  shell => '/bin/bash',
  uid => '7070',
}

file{'/var/log/netrisk':
  ensure => 'directory',
  mode => '755',
  owner => 'netrisk',
  recurse => true,
}

exec {'erase cache':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => 'rm -rf /var/cache/*'
} 