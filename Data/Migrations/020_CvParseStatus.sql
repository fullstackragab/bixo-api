-- Migration: 020_CvParseStatus
-- Description: Track CV parsing status for admin review workflow

-- Add CV parsing status tracking
ALTER TABLE candidates ADD COLUMN IF NOT EXISTS cv_parse_status VARCHAR(20) DEFAULT 'pending';
ALTER TABLE candidates ADD COLUMN IF NOT EXISTS cv_parse_error TEXT;
ALTER TABLE candidates ADD COLUMN IF NOT EXISTS cv_parsed_at TIMESTAMP WITH TIME ZONE;

-- Index for admin to find profiles needing review
CREATE INDEX IF NOT EXISTS idx_candidates_parse_status ON candidates(cv_parse_status)
    WHERE cv_parse_status IN ('failed', 'partial');

COMMENT ON COLUMN candidates.cv_parse_status IS 'CV parsing status: pending, success, partial, failed';
COMMENT ON COLUMN candidates.cv_parse_error IS 'Error message if parsing failed';
COMMENT ON COLUMN candidates.cv_parsed_at IS 'When CV was last parsed';
