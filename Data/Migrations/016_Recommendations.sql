-- Migration: 016_Recommendations
-- Description: Add recommendations system for candidates to request private recommendations

-- Create recommendations table
CREATE TABLE IF NOT EXISTS recommendations (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    candidate_id UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,

    -- Recommender info
    recommender_name VARCHAR(200) NOT NULL,
    recommender_email VARCHAR(255) NOT NULL,
    relationship VARCHAR(50) NOT NULL, -- Manager, Tech Lead, Founder, Peer, Colleague

    -- Recommendation content
    content TEXT,

    -- Status tracking
    is_submitted BOOLEAN NOT NULL DEFAULT FALSE,
    is_approved_by_candidate BOOLEAN NOT NULL DEFAULT FALSE,

    -- Secure token for recommender access (one-time use concept)
    access_token VARCHAR(100) NOT NULL,
    token_expires_at TIMESTAMP WITH TIME ZONE NOT NULL,

    -- Timestamps
    submitted_at TIMESTAMP WITH TIME ZONE,
    approved_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

-- Enforce max 3 recommendations per candidate (via application logic, but add index for queries)
CREATE INDEX idx_recommendations_candidate_id ON recommendations(candidate_id);

-- Enforce one recommendation per (candidate + recommender email)
CREATE UNIQUE INDEX idx_recommendations_candidate_email ON recommendations(candidate_id, recommender_email);

-- Index for token lookups
CREATE UNIQUE INDEX idx_recommendations_token ON recommendations(access_token);

-- Index for finding pending recommendations
CREATE INDEX idx_recommendations_pending ON recommendations(candidate_id) WHERE is_submitted = FALSE;

-- Index for finding approved recommendations
CREATE INDEX idx_recommendations_approved ON recommendations(candidate_id) WHERE is_approved_by_candidate = TRUE;

-- Add comments
COMMENT ON TABLE recommendations IS 'Private recommendations requested by candidates from their professional network';
COMMENT ON COLUMN recommendations.relationship IS 'Professional relationship: Manager, Tech Lead, Founder, Peer, Colleague';
COMMENT ON COLUMN recommendations.access_token IS 'Secure one-time token for recommender to submit recommendation without login';
COMMENT ON COLUMN recommendations.is_approved_by_candidate IS 'Candidate must approve before recommendation is visible to companies';
