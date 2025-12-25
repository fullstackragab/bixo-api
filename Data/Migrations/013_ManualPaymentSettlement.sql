-- Migration: 013_ManualPaymentSettlement
-- Description: Add separate payment_status field and audit fields for manual payment settlement
-- This keeps shortlist.status (business lifecycle) separate from payment settlement

-- Migrate any status=4 (old Authorized) to status=3 (Approved)
-- This is because we're removing the Authorized status from the enum
UPDATE shortlist_requests SET status = 3 WHERE status = 4;

-- Add payment_status column (separate from business status)
-- 0 = not_required, 1 = pending, 2 = paid
ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS payment_status INTEGER NOT NULL DEFAULT 1;

-- Add audit fields for tracking who did what
ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS priced_by UUID REFERENCES users(id);

ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS priced_at TIMESTAMP;

ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS delivered_by UUID REFERENCES users(id);

ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS paid_confirmed_by UUID REFERENCES users(id);

ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS paid_at TIMESTAMP;

ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS payment_note TEXT;

-- Update existing shortlists:
-- If status >= 5 (Delivered) and has a payment, set payment_status = paid
UPDATE shortlist_requests
SET payment_status = 2
WHERE status >= 5 AND (payment_id IS NOT NULL OR price_paid IS NOT NULL);

-- If status >= 5 (Delivered) but no payment, set payment_status = not_required (for testing data)
UPDATE shortlist_requests
SET payment_status = 0
WHERE status >= 5 AND payment_id IS NULL AND price_paid IS NULL;

-- Add index for payment status queries
CREATE INDEX IF NOT EXISTS idx_shortlist_requests_payment_status ON shortlist_requests(payment_status);

-- Add composite index for admin dashboard queries
CREATE INDEX IF NOT EXISTS idx_shortlist_requests_status_payment ON shortlist_requests(status, payment_status);

COMMENT ON COLUMN shortlist_requests.payment_status IS 'Payment settlement status: 0=not_required, 1=pending, 2=paid';
COMMENT ON COLUMN shortlist_requests.priced_by IS 'Admin who set the price';
COMMENT ON COLUMN shortlist_requests.delivered_by IS 'Admin who delivered the shortlist';
COMMENT ON COLUMN shortlist_requests.paid_confirmed_by IS 'Admin who confirmed payment received';
