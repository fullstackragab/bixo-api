-- Bixo Location Handling Migration
-- Implements location scoring (ranking > filtering) for candidates and companies

-- ============================================================================
-- Candidate Locations Table
-- Stores detailed location information for candidates
-- ============================================================================
CREATE TABLE IF NOT EXISTS candidate_locations (
    candidate_id UUID PRIMARY KEY REFERENCES candidates(id) ON DELETE CASCADE,
    country VARCHAR(100),
    city VARCHAR(200),
    timezone VARCHAR(50),
    latitude DOUBLE PRECISION,
    longitude DOUBLE PRECISION,
    willing_to_relocate BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Index for location-based queries
CREATE INDEX IF NOT EXISTS idx_candidate_locations_country ON candidate_locations(country);
CREATE INDEX IF NOT EXISTS idx_candidate_locations_city ON candidate_locations(city);
CREATE INDEX IF NOT EXISTS idx_candidate_locations_timezone ON candidate_locations(timezone);
CREATE INDEX IF NOT EXISTS idx_candidate_locations_willing_to_relocate ON candidate_locations(willing_to_relocate);

-- ============================================================================
-- Company Locations Table
-- Stores company HQ/office location (static, for display and context)
-- ============================================================================
CREATE TABLE IF NOT EXISTS company_locations (
    company_id UUID PRIMARY KEY REFERENCES companies(id) ON DELETE CASCADE,
    country VARCHAR(100),
    city VARCHAR(200),
    timezone VARCHAR(50),
    latitude DOUBLE PRECISION,
    longitude DOUBLE PRECISION,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Index for location-based queries
CREATE INDEX IF NOT EXISTS idx_company_locations_country ON company_locations(country);
CREATE INDEX IF NOT EXISTS idx_company_locations_city ON company_locations(city);

-- ============================================================================
-- Shortlist Requests - Add hiring location fields
-- Each shortlist request can have its own location requirements
-- ============================================================================
ALTER TABLE shortlist_requests
    ADD COLUMN IF NOT EXISTS location_country VARCHAR(100),
    ADD COLUMN IF NOT EXISTS location_city VARCHAR(200),
    ADD COLUMN IF NOT EXISTS location_timezone VARCHAR(50);

-- Rename remote_allowed to is_remote for clarity (if column exists)
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'shortlist_requests' AND column_name = 'remote_allowed'
    ) AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'shortlist_requests' AND column_name = 'is_remote'
    ) THEN
        ALTER TABLE shortlist_requests RENAME COLUMN remote_allowed TO is_remote;
    END IF;
END $$;

-- Index for shortlist location queries
CREATE INDEX IF NOT EXISTS idx_shortlist_requests_location_country ON shortlist_requests(location_country);
CREATE INDEX IF NOT EXISTS idx_shortlist_requests_is_remote ON shortlist_requests(is_remote);

-- ============================================================================
-- Note on work_mode / RemotePreference
-- The existing remote_preference column in candidates table already maps to:
-- - Remote (0) -> remote_only
-- - Onsite (1) -> onsite
-- - Hybrid (2) -> hybrid
-- - Flexible (3) -> flexible
-- No schema changes needed for work_mode concept.
-- ============================================================================

-- ============================================================================
-- Migration for existing data
-- Copy location_preference text to new structured fields if possible
-- ============================================================================

-- For candidates: Extract location_preference to candidate_locations if it exists
-- This is a best-effort migration - actual normalization should happen async via geocoding API
INSERT INTO candidate_locations (candidate_id, city, country, created_at, updated_at)
SELECT
    c.id,
    c.location_preference,  -- Store as city initially, can be normalized later
    NULL,
    NOW(),
    NOW()
FROM candidates c
WHERE c.location_preference IS NOT NULL
    AND c.location_preference != ''
    AND NOT EXISTS (SELECT 1 FROM candidate_locations cl WHERE cl.candidate_id = c.id)
ON CONFLICT (candidate_id) DO NOTHING;
