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

exec {'erase cache':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => 'rm -rf /var/cache/*'
} ->
exec {'erase logs':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => 'rm -rf /var/log/*'
}

