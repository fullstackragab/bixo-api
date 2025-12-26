-- Migration: 014_ShortlistOutcome
-- Description: Add outcome tracking for shortlist requests to handle "no suitable candidates" scenario
-- Outcome is immutable once set and determines payment behavior

-- Add outcome tracking columns
ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS outcome INTEGER NOT NULL DEFAULT 0;

ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS outcome_reason TEXT;

ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS outcome_decided_at TIMESTAMP;

ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS outcome_decided_by UUID REFERENCES users(id);

-- Add index for outcome queries
CREATE INDEX IF NOT EXISTS idx_shortlist_requests_outcome ON shortlist_requests(outcome);

-- Add check constraint to ensure outcome_reason is provided for terminal outcomes
ALTER TABLE shortlist_requests
ADD CONSTRAINT chk_outcome_reason CHECK (
    CASE 
        WHEN outcome IN (1, 2, 3, 4) THEN outcome_reason IS NOT NULL
        ELSE TRUE
    END
);

-- Migrate existing data:
-- Delivered shortlists (status = 5) should have outcome = Delivered (1)
UPDATE shortlist_requests
SET outcome = 1,
    outcome_reason = 'Shortlist delivered',
    outcome_decided_at = COALESCE(completed_at, delivered_at, created_at),
    outcome_decided_by = delivered_by
WHERE status = 5 AND outcome = 0;

-- Completed shortlists (status = 6) should have outcome = Delivered (1)
UPDATE shortlist_requests
SET outcome = 1,
    outcome_reason = 'Shortlist delivered',
    outcome_decided_at = COALESCE(completed_at, delivered_at, created_at),
    outcome_decided_by = delivered_by
WHERE status = 6 AND outcome = 0;

-- Cancelled shortlists (status = 7) should have outcome = Cancelled (4)
UPDATE shortlist_requests
SET outcome = 4,
    outcome_reason = 'Cancelled',
    outcome_decided_at = COALESCE(completed_at, created_at)
WHERE status = 7 AND outcome = 0;

-- Add comments for documentation
COMMENT ON COLUMN shortlist_requests.outcome IS 'Shortlist outcome: 0=Pending, 1=Delivered, 2=Partial, 3=NoMatch, 4=Cancelled (immutable once set)';
COMMENT ON COLUMN shortlist_requests.outcome_reason IS 'Admin explanation for outcome decision (required for terminal outcomes)';
COMMENT ON COLUMN shortlist_requests.outcome_decided_at IS 'Timestamp when outcome was decided';
COMMENT ON COLUMN shortlist_requests.outcome_decided_by IS 'Admin user who decided the outcome';
