

delete from settings where name = 'auto_verify_new_assets';
delete from settings where name = 'currency';
delete from settings where name = 'default_current_maturity';
delete from settings where name = 'default_date_format';
delete from settings where name = 'next_review_date_uses';
delete from settings where name = 'plan_projects_show_all';
delete from settings where name = 'risk_appetite';


update settings SET value = '29' where name = 'db_version';