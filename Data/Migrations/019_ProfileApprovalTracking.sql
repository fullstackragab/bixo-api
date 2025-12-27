-- Migration: 019_ProfileApprovalTracking
-- Description: Track when and by whom a candidate profile was approved

-- Add approval tracking columns
ALTER TABLE candidates ADD COLUMN IF NOT EXISTS profile_approved_at TIMESTAMP WITH TIME ZONE;
ALTER TABLE candidates ADD COLUMN IF NOT EXISTS profile_approved_by UUID REFERENCES users(id);

-- Add index for finding approved profiles
CREATE INDEX IF NOT EXISTS idx_candidates_approved ON candidates(profile_approved_at) WHERE profile_approved_at IS NOT NULL;

COMMENT ON COLUMN candidates.profile_approved_at IS 'When the profile was approved by admin';
COMMENT ON COLUMN candidates.profile_approved_by IS 'Admin user who approved the profile';
