class netrisk (
  # SRNet Settings
  $netrisk_url     = '',

  # Database Settings
  $dbserver   = '127.0.0.1',
  $dbuser     = 'simplerisk',
  $dbport     = '3306',
  $dbpassword = '',
  $dbschema   = 'simplerisk',

  #SAML Settings
  $enable_saml       = false,
  $idp_entity_id     = 'https://stubidp.sustainsys.com',
  $idp_name          = 'stubidp.sustainsys',
  $idp_sso_service   = 'https://stubidp.sustainsys.com/',
  $idp_ssout_service = 'https://stubidp.sustainsys.com/Logout',
  $idp_artifact_resolve_srvc = 'https://stubidp.sustainsys.com/ArtifactResolve',
  $idp_certificate_file      = 'Certificates/stubidp.sustainsys.com.cer',
  $sp_certificate_file = 'Certificates/demowebapp.local.pfx',
  $sp_certificate_pwd  = 'pass',

  #Server
  $server_logging          = 'Information',
  $server_https_port       = 5443,
  $server_certificate_file = "Certificates/certificate.pfx",
  $server_certificate_pwd  = "pass"

) inherits srnet::params {

# UPDATE THIS EVERY RELEASE
$srnetmaxdbver = 2

#$dbpwd = String(file('/passwords/pass_netrisk.txt'), "%t")
#$srnetdbver = String(file('/configurations/srnetdb.version'), "%t")

$n_srvdbver = 0 + $srnetdbver

if ( $dbpassword == '') {
  $dbpw_fin = $dbpwd
}else{
  $dbpw_fin = $dbpassword
}

if($n_srvdbver != $srnetmaxdbver) {
  Integer[$n_srvdbver, $srnetmaxdbver].each |$x| {
    #notice("updating DB version ${x}")
    exec{"updating DB version ${x}":
      command => "/bin/bash -c 'MYSQL_PWD=${dbpw_fin} mysql -u${dbuser} -e \"use netrisk; \\. /scripts/netrisk-db/DB-SQL-${x}.sql\" && echo ${x} > /configurations/netriskdb.version'"
    }
  }
}


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

file{'/srnet/SRNET-Server/appsettings.json': 
  ensure  => file,
  content => epp('srnet/server/appsettings.json.epp', {
    'server_url'     => $srnet_url,
    'enable_saml'    => $enable_saml,
    'server_logging' => $server_logging,
    'sp_certificate_file'   => $sp_certificate_file,
    'sp_certificate_pwd'    => $sp_certificate_pwd,
    'idp_entity_id'             => $idp_entity_id,
    'idp_name'                  => $idp_name,
    'idp_sso_service'           => $idp_sso_service,
    'idp_ssout_service'         => $idp_ssout_service,
    'idp_artifact_resolve_srvc' => $idp_artifact_resolve_srvc,
    'idp_certificate_file'      => $idp_certificate_file,
    'db_server'   => $dbserver,
    'db_user'     => $dbuser,
    'db_port'     => $dbport ,
    'db_password' => $dbpw_fin ,
    'db_schema'   => $dbschema,
    'server_https_port'       => $server_https_port,
    'server_certificate_file' => $server_certificate_file,
    'server_certificate_pwd'  => $server_certificate_pwd
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
