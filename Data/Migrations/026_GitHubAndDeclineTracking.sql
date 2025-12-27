-- Migration: 026_GitHubAndDeclineTracking
-- Adds GitHub profile enrichment fields and shortlist decline tracking

-- GitHub profile fields for candidates
ALTER TABLE candidates
ADD COLUMN IF NOT EXISTS github_url VARCHAR(500),
ADD COLUMN IF NOT EXISTS github_summary TEXT,
ADD COLUMN IF NOT EXISTS github_summary_generated_at TIMESTAMPTZ;

-- Shortlist decline tracking columns
-- Used when company declines a shortlist (different from price negotiation)
ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS decline_reason VARCHAR(50),
ADD COLUMN IF NOT EXISTS decline_feedback TEXT,
ADD COLUMN IF NOT EXISTS declined_at TIMESTAMPTZ;

-- Index for finding shortlists by decline reason
CREATE INDEX IF NOT EXISTS idx_shortlist_requests_decline_reason
ON shortlist_requests(decline_reason) WHERE decline_reason IS NOT NULL;
