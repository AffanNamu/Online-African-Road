-- Garage, step 1: a ledger kind for paint skins.
-- ALTER TYPE ... ADD VALUE cannot be used in the transaction that adds it, so it lives in its own migration (every file is applied in its own transaction).
alter type transaction_kind add value if not exists 'SKIN_PURCHASE';
