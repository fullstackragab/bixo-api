-- Migration: 018_RecommendationAdminApproval
-- Description: Add admin approval and additional recommender details for company-facing display

-- Add recommender role and company for display
ALTER TABLE recommendations
ADD COLUMN IF NOT EXISTS recommender_role VARCHAR(100);

ALTER TABLE recommendations
ADD COLUMN IF NOT EXISTS recommender_company VARCHAR(200);

-- Add admin approval (required before visible to companies)
ALTER TABLE recommendations
ADD COLUMN IF NOT EXISTS is_admin_approved BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE recommendations
ADD COLUMN IF NOT EXISTS admin_approved_at TIMESTAMP WITH TIME ZONE;

ALTER TABLE recommendations
ADD COLUMN IF NOT EXISTS admin_approved_by UUID REFERENCES users(id);

-- Add admin rejection tracking
ALTER TABLE recommendations
ADD COLUMN IF NOT EXISTS is_rejected BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE recommendations
ADD COLUMN IF NOT EXISTS rejection_reason TEXT;

-- Create index for admin-approved recommendations (visible to companies)
CREATE INDEX IF NOT EXISTS idx_recommendations_company_visible
ON recommendations(candidate_id)
WHERE is_submitted = TRUE AND is_approved_by_candidate = TRUE AND is_admin_approved = TRUE AND is_rejected = FALSE;

-- Add comments
COMMENT ON COLUMN recommendations.recommender_role IS 'Professional role of recommender (e.g., Engineering Manager)';
COMMENT ON COLUMN recommendations.recommender_company IS 'Company where recommender worked with candidate';
COMMENT ON COLUMN recommendations.is_admin_approved IS 'Admin must approve before recommendation is visible to companies';
COMMENT ON COLUMN recommendations.is_rejected IS 'Admin rejected recommendation (low quality, exaggerated, etc.)';
