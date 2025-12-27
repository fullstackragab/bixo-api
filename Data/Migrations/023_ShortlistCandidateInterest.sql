-- Migration: 023_ShortlistCandidateInterest
-- Adds candidate interest response to shortlist_candidates (source of truth)
-- Interest status on shortlist_messages is for message-level tracking,
-- but shortlist_candidates is the canonical record.

-- Add interest response fields to shortlist_candidates
ALTER TABLE shortlist_candidates
ADD COLUMN IF NOT EXISTS interest_status TEXT,
ADD COLUMN IF NOT EXISTS interest_responded_at TIMESTAMP;

-- interest_status values:
-- NULL - No response yet (pending)
-- 'interested' - Candidate is interested
-- 'not_interested' - Candidate is not interested
-- 'interested_later' - Candidate may be interested later

-- Index for filtering shortlists by interest status
CREATE INDEX IF NOT EXISTS idx_shortlist_candidates_interest_status
ON shortlist_candidates(interest_status);

-- Index for finding candidates who responded
CREATE INDEX IF NOT EXISTS idx_shortlist_candidates_interest_responded
ON shortlist_candidates(interest_responded_at) WHERE interest_responded_at IS NOT NULL;

COMMENT ON COLUMN shortlist_candidates.interest_status IS 'Candidate response: interested, not_interested, interested_later, or NULL for pending';
COMMENT ON COLUMN shortlist_candidates.interest_responded_at IS 'When the candidate responded to this shortlist opportunity';
