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
CREATE INDEX IDX_REPORTEDHASHRATES_POOL_MINER_WORKER_CREATED_HASHRATE on reported_hashrate(poolid, miner, worker, created desc, hashrate);


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
