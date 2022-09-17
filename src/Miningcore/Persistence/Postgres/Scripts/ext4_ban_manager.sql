CREATE TABLE ban_manager
(
	ipaddress TEXT NOT NULL,
	reason TEXT NULL,
	expires TIMESTAMPTZ NOT NULL,
	created TIMESTAMPTZ NOT NULL,

	primary key(ipaddress)
);