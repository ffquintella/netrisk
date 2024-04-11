update settings SET value = '27' where name = 'db_version';

delete from settings where name like 'custom_auth%';
delete from settings where name = 'alert_timeout';
delete from settings where name = 'default_asset_valuation';
delete from settings where name = 'risk_model';
delete from settings where name = 'risk_mapping_required';
delete from settings where name = 'risk_mapping_required';
delete from settings where name = 'registration_registered';
delete from settings where name = 'default_desired_maturity';
delete from settings where name = 'api';