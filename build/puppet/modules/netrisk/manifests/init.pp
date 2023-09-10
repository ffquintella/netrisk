class netrisk (
  # NetRisk Settings



) inherits netrisk::params {

# UPDATE THIS EVERY RELEASE
#$nriskmaxdbver = 2

#$dbpwd = String(file('/passwords/pass_netrisk.txt'), "%t")
#$nriskdbver = String(file('/configurations/srnetdb.version'), "%t")
#$n_srvdbver = 0 + $srnetdbver

#if ( $dbpassword == '') {
#  $dbpw_fin = $dbpwd
#}else{
#  $dbpw_fin = $dbpassword
#}

#if($n_srvdbver != $srnetmaxdbver) {
#  Integer[$n_srvdbver, $srnetmaxdbver].each |$x| {
    #notice("updating DB version ${x}")
#    exec{"updating DB version ${x}":
#      command => "/bin/bash -c 'MYSQL_PWD=${dbpw_fin} mysql -u${dbuser} -e \"use netrisk; \\. /scripts/netrisk-db/DB-SQL-${x}.sql\" && echo ${x} > /configurations/netriskdb.version'"
#    }
#  }
#}


file{'/srnet/SRNET-ConsoleClient/appsettings.json': 
  ensure  => file,
  content => epp('srnet/consoleClient/appsettings.json.epp', {
    'db_server'   => $dbserver,
    'db_user'     => $dbuser,
    'db_port'     => $dbport ,
    'db_password' => $dbpw_fin ,
    'db_schema'   => $dbschema
  })
}

file{'/srnet/SRNET-GUIClient-lin/appsettings.json': 
  ensure  => file,
  content => epp('srnet/guiClient/appsettings.json.epp', {
    'server_url'   => $srnet_url
  })
}

file{'/srnet/SRNET-GUIClient-win/appsettings.json': 
  ensure  => file,
  content => epp('srnet/guiClient/appsettings.json.epp', {
    'server_url'   => $srnet_url
  })
}

file{'/srnet/SRNET-GUIClient-osx/appsettings.json': 
  ensure  => file,
  content => epp('srnet/guiClient/appsettings.json.epp', {
    'server_url'   => $srnet_url
  })
}



# Compressing the pagages 

file {'/var/www/simplerisk/extras/srnet':
  ensure => directory
}-> 
file {'/var/www/simplerisk/extras/srnet/index.html':
  ensure => file,
  source => "puppet:///modules/srnet/index.html",
}



exec{'Compress GUIClient - linux':
  require => [File['/var/www/simplerisk/extras/srnet'],File['/srnet/SRNET-GUIClient-lin/appsettings.json']],
  command => "/usr/bin/zip -r /var/www/simplerisk/extras/srnet/SRNET-GUIClient-lin.zip /srnet/SRNET-GUIClient-lin && chown www-data:www-data /var/www/simplerisk/extras/srnet/SRNET-GUIClient-lin.zip",
  creates => '/var/www/simplerisk/extras/srnet/SRNET-GUIClient-lin.zip',
}

exec{'Compress GUIClient - windows':
  require => [File['/var/www/simplerisk/extras/srnet'],File['/srnet/SRNET-GUIClient-win/appsettings.json']],
  command => "/usr/bin/zip -r /var/www/simplerisk/extras/srnet/SRNET-GUIClient-win.zip /srnet/SRNET-GUIClient-win && chown www-data:www-data /var/www/simplerisk/extras/srnet/SRNET-GUIClient-win.zip",
  creates => '/var/www/simplerisk/extras/srnet/SRNET-GUIClient-win.zip'
}

exec{'Compress GUIClient - osx':
  require => [File['/var/www/simplerisk/extras/srnet'],File['/srnet/SRNET-GUIClient-osx/appsettings.json']],
  command => "/usr/bin/zip -r /var/www/simplerisk/extras/srnet/SRNET-GUIClient-osx.zip /srnet/SRNET-GUIClient-osx && chown www-data:www-data /var/www/simplerisk/extras/srnet/SRNET-GUIClient-osx.zip",
  creates => '/var/www/simplerisk/extras/srnet/SRNET-GUIClient-osx.zip'
}


exec{'Starting SRNet Server':
  cwd         => '/srnet/SRNET-Server/',
  command     => '/srnet/SRNET-Server/API &',
  environment => ['ASPNETCORE_ENVIRONMENT=production','DOTNET_USER_SECRETS_FALLBACK_DIR=/tmp'],
  user        => root,
  logoutput   => true
}


}
