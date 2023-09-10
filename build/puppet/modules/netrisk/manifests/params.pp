# == Class: srnet::params
#
# Defines default values for srnet module
#
class netrisk::params {

  Exec { path => [ '/bin/', '/sbin/' , '/usr/bin/', '/usr/sbin/' ] }

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
$server_logging          = 'Information'
$server_https_port       = 5443
$server_certificate_file = "Certificates/certificate.pfx"
$server_certificate_pwd  = "pass"  

}
