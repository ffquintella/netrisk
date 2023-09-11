# == Class: srnet::params
#
# Defines default values for srnet module
#
class netrisk::api (
  $netrisk_url = $netrisk::params::netrisk_url,
  
  # Database Settings
  $dbserver   = $netrisk::params::dbserver,
  $dbuser     = $netrisk::params::dbuser,
  $dbport     = $netrisk::params::dbport,
  $dbpassword = $netrisk::params::dbpassword,
  $dbschema   = $netrisk::params::dbschema,

  #SAML Settings
  $enable_saml       = $netrisk::params::enable_saml,
  $idp_entity_id     = $netrisk::params::idp_entity_id,
  $idp_name          = $netrisk::params::idp_name,
  $idp_sso_service   = $netrisk::params::idp_sso_service,
  $idp_ssout_service = $netrisk::params::idp_ssout_service,
  $idp_artifact_resolve_srvc = $netrisk::params::idp_artifact_resolve_srvc,
  $idp_certificate_file      = $netrisk::params::idp_certificate_file,
  $sp_certificate_file = $netrisk::params::sp_certificate_file,
  $sp_certificate_pwd  = $netrisk::params::sp_certificate_pwd,

  #Server
  $server_logging          = $netrisk::params::server_logging,
  $server_https_port       = $netrisk::params::server_https_port,
  $server_certificate_file = $netrisk::params::server_certificate_file,
  $server_certificate_pwd  = $netrisk::params::server_certificate_pwd,
  
  #Email
  $email_from = $netrisk::params::email_from,
  $email_server = $netrisk::params::email_server,
  $email_port = $netrisk::params::email_port,
  
  #Website
  $website_protocol = $netrisk::params::website_protocol,
  $website_host = $netrisk::params::website_host,
  $website_port = $netrisk::params::website_port,
  
) inherits netrisk::params  {

  file{'/netrisk/appsettings.json':
    ensure  => file,
    content => epp('netrisk/api/appsettings.json.epp', {
      'server_url'     => $netrisk_url,
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
      'db_password' => $dbpassword ,
      'db_schema'   => $dbschema,
      'server_https_port'       => $server_https_port,
      'server_certificate_file' => $server_certificate_file,
      'server_certificate_pwd'  => $server_certificate_pwd,
      'email_from'              => $email_from,
      'email_server'            => $email_server,
      'email_port'              => 0 + $email_port,
      'website_protocol'        => $website_protocol,
      'website_host'            => $website_host,
      'website_port'            => 0 + $website_port,
    })
  }



  exec{'Starting NetRisk API Server':
    cwd         => '/netrisk/',
    command     => '/netrisk/API &',
    environment => ['ASPNETCORE_ENVIRONMENT=production','DOTNET_USER_SECRETS_FALLBACK_DIR=/tmp'],
    user        => root,
    logoutput   => true
  }

}
