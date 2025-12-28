-- Migration: 028_PublicWorkSummaryRequest
-- Adds request tracking for candidate-initiated public work summary
-- Supports the opt-in flow: candidate requests → Bixo prepares → candidate approves

-- Track when candidate requested the summary (null = never requested)
ALTER TABLE candidates
ADD COLUMN IF NOT EXISTS github_summary_requested_at TIMESTAMPTZ;

-- Index for finding pending requests (requested but not yet generated)
CREATE INDEX IF NOT EXISTS idx_candidates_github_summary_pending
ON candidates(github_summary_requested_at)
WHERE github_summary_requested_at IS NOT NULL
  AND github_summary IS NULL;
