if $enable_saml == 'true' {
  $samlen = true
} else{
  $samlen = false
  $idp_entity_id = undef
  $idp_name = undef
  $idp_sso_service = ''
  $idp_ssout_service = ''
  $idp_artifact_resolve_srvc = ''
  $idp_certificate_file = ''
  $sp_certificate_file = ''
  $sp_certificate_pwd = ''
}

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

if $server_certificate_file == undef{
  # This file will do the initial configuration of netrisk and start the service
  class { 'netrisk::api':
    netrisk_url  => $netrisk_url,
    dbserver   => $dbserver,
    dbuser     => $dbuser,
    dbport     => Integer($dbport),
    dbpassword => $dbpassword,
    dbschema   => $dbschema,
    enable_saml               => $samlen,
    idp_entity_id             => $idp_entity_id,
    idp_name                  => $idp_name,
    idp_sso_service           => $idp_sso_service,
    idp_ssout_service         => $idp_ssout_service,
    idp_artifact_resolve_srvc => $idp_artifact_resolve_srvc,
    idp_certificate_file      => $idp_certificate_file,
    sp_certificate_file       => $sp_certificate_file,
    sp_certificate_pwd        => $sp_certificate_pwd,
    server_logging          => $server_logging,
    server_https_port       => Integer($server_https_port),
    email_from              => $email_from,
    email_server            => $email_server,
    email_port              => Integer($email_port),
    website_protocol        => $website_protocol,
    website_host            => $website_host,
    website_port            => Integer($website_port),
    user                    => $netrisk_user,
    uid                     => $netrisk_uid,
  }
}else{
  # This file will do the initial configuration of netrisk and start the service
  class { 'netrisk::api':
    netrisk_url  => $netrisk_url,
    dbserver   => $dbserver,
    dbuser     => $dbuser,
    dbport     => Integer($dbport),
    dbpassword => $dbpassword,
    dbschema   => $dbschema,
    enable_saml               => $samlen,
    idp_entity_id             => $idp_entity_id,
    idp_name                  => $idp_name,
    idp_sso_service           => $idp_sso_service,
    idp_ssout_service         => $idp_ssout_service,
    idp_artifact_resolve_srvc => $idp_artifact_resolve_srvc,
    idp_certificate_file      => $idp_certificate_file,
    sp_certificate_file       => $sp_certificate_file,
    sp_certificate_pwd        => $sp_certificate_pwd,
    server_logging          => $server_logging,
    server_https_port       => Integer($server_https_port),
    server_certificate_file => $server_certificate_file,
    server_certificate_pwd  => $server_certificate_pwd,
    email_from              => $email_from,
    email_server            => $email_server,
    email_port              => Integer($email_port),
    website_protocol        => $website_protocol,
    website_host            => $website_host,
    website_port            => Integer($website_port),
    user                    => $netrisk_user,
    uid                     => $netrisk_uid,
  }
}
