# == Class: srnet::params
#
# Defines default values for srnet module
#
class netrisk::params {

  Exec { path => [ '/bin/', '/sbin/' , '/usr/bin/', '/usr/sbin/' ] }
  
  $netrisk_url = ''
  
  # Database Settings
  $dbserver   = '127.0.0.1'
  $dbuser     = 'netrisk'
  $dbport     = '3306'
  $dbpassword = ''
  $dbschema   = 'netrisk'
  
  #SAML Settings
  $enable_saml       = false
  $idp_entity_id     = 'https://stubidp.sustainsys.com'
  $idp_name          = 'stubidp.sustainsys'
  $idp_sso_service   = 'https://stubidp.sustainsys.com/'
  $idp_ssout_service = 'https://stubidp.sustainsys.com/Logout'
  $idp_artifact_resolve_srvc = 'https://stubidp.sustainsys.com/ArtifactResolve'
  $idp_certificate_file      = 'Certificates/stubidp.sustainsys.com.cer'
  $sp_certificate_file = 'Certificates/demowebapp.local.pfx'
  $sp_certificate_pwd  = 'pass'
  
  #Server
  #
  # Track 7 finding NR-2026-003: these used to default to 'Certificates/certificate.pfx' and 'pass' —
  # a certificate whose private key is committed to the NetRisk repository, with the password
  # published beside it. A Release build now refuses to start with either, so the defaults would have
  # produced a service that dies on boot rather than one that serves insecurely. Both are better as
  # deliberate values than as anything that could be inherited.
  #
  # There is no safe default for a TLS certificate, so these point at paths an operator has to create.
  # See docs/security/SECRETS.md § 3.3 for how to supply the password out of band; setting
  # $security_allow_development_certificate is only for a local sandbox.
  $server_logging          = 'Information'
  $server_https_port       = 5443
  $server_certificate_file = '/etc/netrisk/netrisk.pfx'
  $server_certificate_pwd  = ''

  # Permits the repository's committed development certificate. Never true on a real deployment: it
  # turns off the guard that stops the service serving with a published private key.
  $security_allow_development_certificate = false
  
  #Email
  $email_from = 'netrisk@localhost.com'
  $email_server = 'localhost'
  $email_port = 25
  
  #Website
  $website_protocol = 'https'
  $website_host = 'localhost'
  $website_port = 6443
  
  $user = 'netrisk'
  $uid  = 7070

}
