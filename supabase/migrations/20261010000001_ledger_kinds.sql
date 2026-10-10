-- Backend foundation, step 1: new ledger kinds.
-- ALTER TYPE ... ADD VALUE cannot be used in the transaction that adds it, so the values live in their own migration
-- (every migration file is applied in its own transaction, see supabase/apply.sh).
alter type transaction_kind add value if not exists 'ADJUSTMENT';        -- administrative correction (reason required)
alter type transaction_kind add value if not exists 'REFUND';            -- reversal of a purchase
alter type transaction_kind add value if not exists 'CHALLENGE_REWARD';  -- daily challenge payout
