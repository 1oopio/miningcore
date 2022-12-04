-- create the new table
CREATE TABLE reported_hashrate
(
	id BIGSERIAL NOT NULL PRIMARY KEY,
	poolid TEXT NOT NULL,
	miner TEXT NOT NULL,
	worker TEXT NOT NULL,
	hashrate DOUBLE PRECISION NOT NULL DEFAULT 0,
	created TIMESTAMPTZ NOT NULL
);


-- create new indexes
CREATE INDEX IDX_REPORTEDHASHRATE_POOL_MINER_CREATED on reported_hashrate(poolid, miner, created);


-- copy data from minerstats to reported_hashrate
INSERT INTO reported_hashrate 
    (poolid, miner, worker, hashrate, created)
SELECT poolid, miner, worker, hashrate, created 
FROM minerstats
WHERE hashratetype = 'reported';


-- delete data from minerstats
DELETE FROM minerstats WHERE hashratetype = 'reported';


-- remove hashratetype column from minerstats
ALTER TABLE minerstats DROP COLUMN hashratetype;


-- if there is still time: optimize the current table and indexes
VACUUM ANALYZE minerstats;
VACUUM ANALYZE reported_hashrate;

REINDEX INDEX idx_minerstats_pool_miner_worker_created_hashrate;
REINDEX index idx_minerstats_pool_miner_created;
REINDEX index idx_minerstats_pool_created;
