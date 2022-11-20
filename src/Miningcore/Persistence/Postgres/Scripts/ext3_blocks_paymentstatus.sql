ALTER TABLE blocks ADD COLUMN paymentstatus TEXT NULL;

UPDATE blocks
SET paymentstatus = (
    CASE "status"
        WHEN 'confirmed' THEN 'paid'
        WHEN 'orphaned' THEN 'orphaned'
        ELSE 'pending'
    END
)
WHERE paymentstatus IS NULL;

ALTER TABLE blocks ALTER COLUMN paymentstatus SET NOT NULL;

CREATE INDEX IDX_BLOCKS_POOL_BLOCK_STATUS_PAYMENTSTATUS on blocks(poolid, status, paymentstatus);
