-- create the new table
CREATE TABLE payback_pools
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	poolname TEXT NOT NULL,
	amount decimal(28,12) NULL CHECK (amount >= 0),
	available decimal(28,12) NULL,
	sharepercentage FLOAT NULL,
	activefrom TIMESTAMPTZ NOT NULL,
	updated TIMESTAMPTZ NOT NULL,
	created TIMESTAMPTZ NOT NULL,
	UNIQUE (poolid, poolname)
);

-- create new indexes
CREATE INDEX IDX_PAYBACKPOOLS_POOLID_POOLNAME on payback_pools(poolid, poolname);
CREATE INDEX IDX_PAYBACKPOOLS_POOLID_AVAILABLE_ACTIVEFROM on payback_pools(poolid, available, activefrom);