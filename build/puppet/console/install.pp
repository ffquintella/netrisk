package {'libicu':
  ensure => 'installed'
}

package {'sudo':
  ensure => 'installed'
}

package {'procps':
  ensure => 'installed'
}

package {'iputils':
  ensure => 'installed'
}

user {'netrisk':
  home => '/netrisk',
  shell => '/bin/bash',
  uid => '7070',
}

file{'/netrisk':
  ensure => 'directory',
  mode => '755'
}->
file{'/var/netrisk':
  ensure => 'directory',
  mode => '755'
}

file{'/var/log/netrisk':
  ensure => 'directory',
  mode => '755',
  recurse => true,
}

exec {'erase cache':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => 'rm -rf /var/cache/*'
} 

