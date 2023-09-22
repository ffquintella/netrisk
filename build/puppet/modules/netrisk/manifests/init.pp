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


}
