-- Bixo Versioned Shortlist Requests Migration
-- Implements follow-up shortlist handling with candidate deduplication

-- ============================================================================
-- Shortlist Requests - Add versioning and pricing fields
-- ============================================================================

-- Add previous_request_id for linking follow-up shortlists
ALTER TABLE shortlist_requests
    ADD COLUMN IF NOT EXISTS previous_request_id UUID REFERENCES shortlist_requests(id) ON DELETE SET NULL;

-- Add pricing_type to track shortlist pricing category
-- Values: 'new', 'follow_up', 'free_regen'
ALTER TABLE shortlist_requests
    ADD COLUMN IF NOT EXISTS pricing_type VARCHAR(20) DEFAULT 'new';

-- Add follow_up_discount to store the discount applied
ALTER TABLE shortlist_requests
    ADD COLUMN IF NOT EXISTS follow_up_discount DECIMAL(5,2) DEFAULT 0;

-- Index for efficient follow-up chain queries
CREATE INDEX IF NOT EXISTS idx_shortlist_requests_previous_request_id
    ON shortlist_requests(previous_request_id);

-- Index for finding similar shortlists for the same company
CREATE INDEX IF NOT EXISTS idx_shortlist_requests_company_role
    ON shortlist_requests(company_id, role_title);

-- ============================================================================
-- Shortlist Candidates - Add is_new flag for tracking new vs repeated candidates
-- ============================================================================

-- Add is_new flag to track whether candidate is new in this shortlist
ALTER TABLE shortlist_candidates
    ADD COLUMN IF NOT EXISTS is_new BOOLEAN NOT NULL DEFAULT TRUE;

-- Add previous_shortlist_id to track where candidate was previously recommended
ALTER TABLE shortlist_candidates
    ADD COLUMN IF NOT EXISTS previously_recommended_in UUID REFERENCES shortlist_requests(id) ON DELETE SET NULL;

-- Add re_inclusion_reason for cases where previously recommended candidates are re-included
ALTER TABLE shortlist_candidates
    ADD COLUMN IF NOT EXISTS re_inclusion_reason VARCHAR(100);

-- Index for efficient queries on new candidates
CREATE INDEX IF NOT EXISTS idx_shortlist_candidates_is_new
    ON shortlist_candidates(shortlist_request_id, is_new);

-- ============================================================================
-- Follow-up pricing configuration table
-- ============================================================================

CREATE TABLE IF NOT EXISTS follow_up_pricing_rules (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    days_threshold INTEGER NOT NULL, -- Number of days for follow-up eligibility
    discount_percent DECIMAL(5,2) NOT NULL, -- Discount percentage for follow-up
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Default follow-up pricing rules
INSERT INTO follow_up_pricing_rules (id, days_threshold, discount_percent, is_active)
VALUES
    (uuid_generate_v4(), 7, 30.00, TRUE),   -- 30% discount within 7 days
    (uuid_generate_v4(), 14, 20.00, TRUE),  -- 20% discount within 14 days
    (uuid_generate_v4(), 30, 10.00, TRUE)   -- 10% discount within 30 days
ON CONFLICT DO NOTHING;

-- ============================================================================
-- Shortlist similarity tracking view (for detecting follow-up candidates)
-- ============================================================================

-- This view helps identify potential follow-up shortlists based on role similarity
CREATE OR REPLACE VIEW shortlist_similarity AS
SELECT
    sr1.id AS current_request_id,
    sr2.id AS previous_request_id,
    sr1.company_id,
    sr1.role_title AS current_role,
    sr2.role_title AS previous_role,
    sr1.created_at AS current_created_at,
    sr2.created_at AS previous_created_at,
    EXTRACT(DAY FROM (sr1.created_at - sr2.created_at)) AS days_apart,
    -- Basic similarity calculation
    CASE
        WHEN LOWER(sr1.role_title) = LOWER(sr2.role_title) THEN 100
        WHEN LOWER(sr1.role_title) LIKE '%' || LOWER(sr2.role_title) || '%'
             OR LOWER(sr2.role_title) LIKE '%' || LOWER(sr1.role_title) || '%' THEN 80
        ELSE 0
    END AS role_similarity,
    CASE
        WHEN sr1.seniority_required = sr2.seniority_required THEN 100
        WHEN sr1.seniority_required IS NULL OR sr2.seniority_required IS NULL THEN 50
        ELSE 0
    END AS seniority_similarity,
    CASE
        WHEN sr1.is_remote = sr2.is_remote THEN 100
        ELSE 50
    END AS remote_similarity
FROM shortlist_requests sr1
JOIN shortlist_requests sr2 ON sr1.company_id = sr2.company_id
    AND sr2.id != sr1.id
    AND sr2.created_at < sr1.created_at
    AND sr2.status = 2 -- Only completed shortlists
WHERE sr1.previous_request_id IS NULL; -- Only for new requests not yet linked

-- ============================================================================
-- Function to calculate shortlist similarity score
-- ============================================================================

CREATE OR REPLACE FUNCTION calculate_shortlist_similarity(
    current_request_id UUID,
    previous_request_id UUID
) RETURNS INTEGER AS $$
DECLARE
    role_sim INTEGER := 0;
    seniority_sim INTEGER := 0;
    remote_sim INTEGER := 0;
    tech_sim INTEGER := 0;
    location_sim INTEGER := 0;
    current_rec RECORD;
    previous_rec RECORD;
BEGIN
    -- Get current request
    SELECT role_title, seniority_required, is_remote, tech_stack_required, location_country, location_city
    INTO current_rec
    FROM shortlist_requests WHERE id = current_request_id;

    -- Get previous request
    SELECT role_title, seniority_required, is_remote, tech_stack_required, location_country, location_city
    INTO previous_rec
    FROM shortlist_requests WHERE id = previous_request_id;

    -- Role similarity (30%)
    IF LOWER(current_rec.role_title) = LOWER(previous_rec.role_title) THEN
        role_sim := 30;
    ELSIF LOWER(current_rec.role_title) LIKE '%' || LOWER(previous_rec.role_title) || '%'
          OR LOWER(previous_rec.role_title) LIKE '%' || LOWER(current_rec.role_title) || '%' THEN
        role_sim := 20;
    END IF;

    -- Seniority similarity (20%)
    IF current_rec.seniority_required = previous_rec.seniority_required THEN
        seniority_sim := 20;
    ELSIF current_rec.seniority_required IS NULL OR previous_rec.seniority_required IS NULL THEN
        seniority_sim := 10;
    END IF;

    -- Remote similarity (15%)
    IF current_rec.is_remote = previous_rec.is_remote THEN
        remote_sim := 15;
    ELSE
        remote_sim := 5;
    END IF;

    -- Location similarity (15%)
    IF current_rec.location_country = previous_rec.location_country
       AND current_rec.location_city = previous_rec.location_city THEN
        location_sim := 15;
    ELSIF current_rec.location_country = previous_rec.location_country THEN
        location_sim := 10;
    END IF;

    -- Tech stack similarity would require JSONB comparison (simplified here)
    -- In production, use proper JSONB array intersection
    IF current_rec.tech_stack_required IS NOT NULL AND previous_rec.tech_stack_required IS NOT NULL THEN
        tech_sim := 10; -- Simplified: assume some overlap
    END IF;

    RETURN role_sim + seniority_sim + remote_sim + location_sim + tech_sim;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Function to get previous candidates for exclusion
-- ============================================================================

CREATE OR REPLACE FUNCTION get_previous_candidates(
    p_previous_request_id UUID
) RETURNS TABLE(candidate_id UUID) AS $$
BEGIN
    RETURN QUERY
    SELECT sc.candidate_id
    FROM shortlist_candidates sc
    WHERE sc.shortlist_request_id = p_previous_request_id;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- Comments for documentation
-- ============================================================================

COMMENT ON COLUMN shortlist_requests.previous_request_id IS 'Links to the previous shortlist request for follow-up chains';
COMMENT ON COLUMN shortlist_requests.pricing_type IS 'Pricing category: new, follow_up, or free_regen';
COMMENT ON COLUMN shortlist_candidates.is_new IS 'TRUE if candidate is new in this shortlist, FALSE if previously recommended';
COMMENT ON COLUMN shortlist_candidates.previously_recommended_in IS 'References the shortlist where this candidate was previously recommended';
COMMENT ON COLUMN shortlist_candidates.re_inclusion_reason IS 'Reason for re-including a previously recommended candidate';
