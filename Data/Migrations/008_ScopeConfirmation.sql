-- Migration: 008_ScopeConfirmation
-- Adds scope confirmation fields for proper payment authorization flow
-- Authorization ONLY happens after explicit company approval

-- Add scope confirmation fields to shortlist_requests
ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS proposed_candidates INT,
ADD COLUMN IF NOT EXISTS proposed_price NUMERIC,
ADD COLUMN IF NOT EXISTS scope_proposed_at TIMESTAMP,
ADD COLUMN IF NOT EXISTS scope_approved_at TIMESTAMP,
ADD COLUMN IF NOT EXISTS scope_approval_notes TEXT;

-- Add index for finding shortlists awaiting approval
CREATE INDEX IF NOT EXISTS idx_shortlist_requests_scope_proposed
ON shortlist_requests(status)
WHERE status = 1; -- ScopeProposed

-- Update payment status values in existing records
-- Old: Initiated=0, Authorized=1, Escrowed=2, Captured=3, Partial=4, Released=5, Failed=6
-- New: PendingApproval=0, Authorized=1, Captured=2, Partial=3, Released=4, Canceled=5, Failed=6
UPDATE payments SET status = 'pending_approval' WHERE status = 'initiated';
UPDATE payments SET status = 'captured' WHERE status = 'escrowed'; -- Escrowed funds treated as captured

-- Update shortlist status values
-- Old: Pending=0, Processing=1, Completed=2, Cancelled=3
-- New: PendingScope=0, ScopeProposed=1, ScopeApproved=2, Processing=3, Delivered=4, Canceled=5, NoCharge=6
UPDATE shortlist_requests SET status = 4 WHERE status = 2; -- Completed -> Delivered
UPDATE shortlist_requests SET status = 5 WHERE status = 3; -- Cancelled -> Canceled
UPDATE shortlist_requests SET status = 3 WHERE status = 1; -- Processing stays Processing (but now value 3)
-- Pending (0) becomes PendingScope (0) - no change needed
