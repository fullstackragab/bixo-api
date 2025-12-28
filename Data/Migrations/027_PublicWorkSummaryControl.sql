-- Migration: 027_PublicWorkSummaryControl
-- Adds candidate control over public work summary visibility
-- Default is FALSE (opt-in) to require explicit consent

-- Add the control column
ALTER TABLE candidates
ADD COLUMN IF NOT EXISTS github_summary_enabled BOOLEAN NOT NULL DEFAULT FALSE;

-- Existing candidates with summaries also default to FALSE (require explicit opt-in)
-- This is intentional: retroactive visibility would break trust

-- Index for efficient filtering
CREATE INDEX IF NOT EXISTS idx_candidates_github_summary_enabled
ON candidates(github_summary_enabled) WHERE github_summary_enabled = TRUE;
