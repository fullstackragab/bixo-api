-- Migration: 010_PaymentsSchemaUpdate
-- Updates payments table to support authorization-first payment flow

-- Add new columns for authorization flow
ALTER TABLE payments
ADD COLUMN IF NOT EXISTS provider TEXT,
ADD COLUMN IF NOT EXISTS provider_reference TEXT,
ADD COLUMN IF NOT EXISTS amount_authorized NUMERIC DEFAULT 0,
ADD COLUMN IF NOT EXISTS amount_captured NUMERIC DEFAULT 0,
ADD COLUMN IF NOT EXISTS shortlist_request_id UUID REFERENCES shortlist_requests(id),
ADD COLUMN IF NOT EXISTS error_message TEXT,
ADD COLUMN IF NOT EXISTS metadata JSONB,
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP DEFAULT now();

-- Migrate existing data: copy amount to amount_authorized
UPDATE payments
SET amount_authorized = amount,
    provider = CASE
        WHEN stripe_payment_intent_id IS NOT NULL THEN 'stripe'
        ELSE 'unknown'
    END,
    provider_reference = COALESCE(stripe_payment_intent_id, stripe_subscription_id, '')
WHERE amount_authorized = 0 OR amount_authorized IS NULL;

-- Add index for shortlist lookups
CREATE INDEX IF NOT EXISTS idx_payments_shortlist_request_id ON payments(shortlist_request_id);
CREATE INDEX IF NOT EXISTS idx_payments_provider ON payments(provider);
