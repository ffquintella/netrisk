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

-> file{'/var/log/netrisk':
  ensure => 'directory',
  mode => '755',
  owner => 'netrisk',
  recurse => true,
}


# Payload ownership is set at COPY time (--chown=7070:7070) in the Dockerfile, so
# we only manage the top-level directory here. Recursing would re-chown every file
# (including the 177 MB OpenFaceONNX.dll) into a second layer that fails to extract
# under the overlayfs/containerd snapshotter on image export.
file{'/netrisk':
  ensure => 'directory',
  mode => '755',
  owner => 'netrisk',
}->
file{'/var/netrisk':
  ensure => 'directory',
  mode => '755',
  owner => 'netrisk',
}

exec {'erase cache':
  path  => '/bin:/sbin:/usr/bin:/usr/sbin',
  command => 'rm -rf /var/cache/*'
} 