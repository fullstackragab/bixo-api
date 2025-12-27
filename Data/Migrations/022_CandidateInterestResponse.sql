-- Migration: 022_CandidateInterestResponse
-- Adds candidate interest response tracking to shortlist messages

-- Add interest response fields to shortlist_messages
ALTER TABLE shortlist_messages
ADD COLUMN IF NOT EXISTS interest_status TEXT,
ADD COLUMN IF NOT EXISTS interest_responded_at TIMESTAMP;

-- interest_status values:
-- NULL - No response yet
-- 'interested' - Candidate is interested
-- 'not_interested' - Candidate is not interested
-- 'interested_later' - Candidate may be interested later

-- Index for filtering by interest status
CREATE INDEX IF NOT EXISTS idx_shortlist_messages_interest_status
ON shortlist_messages(interest_status) WHERE interest_status IS NOT NULL;

COMMENT ON COLUMN shortlist_messages.interest_status IS 'Candidate response: interested, not_interested, interested_later, or NULL for no response';
COMMENT ON COLUMN shortlist_messages.interest_responded_at IS 'When the candidate responded to this message';
