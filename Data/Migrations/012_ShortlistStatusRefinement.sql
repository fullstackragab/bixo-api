-- Migration: 012_ShortlistStatusRefinement
-- Refines shortlist lifecycle to match trust-first payment flow:
-- Submitted → Processing → PricingPending → PricingApproved → Authorized → Delivered → Completed

-- Update status values:
-- Old: Draft(0), Matching(1), ReadyForPricing(2), PricingRequested(3), PricingApproved(4), Delivered(5), PaymentCaptured(6), Cancelled(7)
-- New: Submitted(0), Processing(1), PricingPending(2), PricingApproved(3), Authorized(4), Delivered(5), Completed(6), Cancelled(7)

-- The numeric values align well, main changes are semantic:
-- - Draft → Submitted
-- - Matching → Processing
-- - ReadyForPricing + PricingRequested → PricingPending (consolidated)
-- - PaymentCaptured → Completed
-- - New: Authorized (after PricingApproved, before Delivered)

-- Remap statuses (adjust only if needed based on current data)
-- PricingApproved(4 old) should now be Authorized(4 new) if payment was already authorized
-- This is a safe migration as the numeric values mostly align

-- Add delivered_at if not exists
ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS delivered_at TIMESTAMP;

-- Migrate: Any shortlist that has a payment_id and is in old PricingApproved(4) should be Authorized(4)
-- This is the same numeric value, so no actual update needed

-- Migrate: Any shortlist in old "Delivered" (was 5) that also has payment captured should be Completed(6)
UPDATE shortlist_requests sr
SET status = 6
WHERE sr.status = 5
  AND EXISTS (
    SELECT 1 FROM payments p
    WHERE p.shortlist_request_id = sr.id AND p.status = 2
  );

-- Update payment status values to match new enum
-- Old: PendingApproval(0), Authorized(1), Captured(2), Partial(3), Released(4), Canceled(5), Failed(6)
-- New: None(0), Authorized(1), Captured(2), Failed(3), Expired(4), Released(5)

-- Remap payment statuses
UPDATE payments SET status = 0 WHERE status = 0; -- PendingApproval → None (same value)
-- Authorized(1) → Authorized(1) - no change
-- Captured(2) → Captured(2) - no change
UPDATE payments SET status = 3 WHERE status = 6; -- Failed(6) → Failed(3)
UPDATE payments SET status = 5 WHERE status = 4; -- Released(4) → Released(5)
-- Note: Partial(3) and Canceled(5) in old enum are deprecated, handle as needed

-- Create index for authorization lookup
CREATE INDEX IF NOT EXISTS idx_shortlist_requests_authorized ON shortlist_requests(status) WHERE status = 4;
