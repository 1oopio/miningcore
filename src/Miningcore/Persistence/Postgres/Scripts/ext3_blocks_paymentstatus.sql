ALTER TABLE blocks ADD COLUMN paymentstatus TEXT NOT NULL;

CREATE INDEX IDX_BLOCKS_POOL_BLOCK_STATUS_PAYMENTSTATUS on blocks(poolid, status, paymentstatus);
