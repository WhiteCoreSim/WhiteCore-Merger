BEGIN;

ALTER TABLE prims ADD COLUMN Material INTEGER NOT NULL default 3;

COMMIT;
