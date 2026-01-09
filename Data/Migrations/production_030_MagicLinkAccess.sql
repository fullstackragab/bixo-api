-- ============================================================================
-- PRODUCTION MIGRATION: 030_MagicLinkAccess
-- Description: Add shortlist access tokens for magic-link company access
--
-- INSTRUCTIONS:
--   1. Backup your database before running
--   2. Execute this script: psql -h <host> -U <user> -d <database> -f production_030_MagicLinkAccess.sql
--   3. Verify the changes with the verification queries at the bottom
--
-- Note: This script is idempotent (safe to run multiple times)
-- ============================================================================

BEGIN;

-- ============================================================================
-- 1. Create shortlist_access_tokens table
-- These are SERVICE tokens for magic-link access, separate from auth tokens
-- ============================================================================

CREATE TABLE IF NOT EXISTS shortlist_access_tokens (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shortlist_request_id UUID NOT NULL REFERENCES shortlist_requests(id) ON DELETE CASCADE,
    token VARCHAR(128) NOT NULL UNIQUE,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    used_at TIMESTAMP WITH TIME ZONE NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE shortlist_access_tokens IS 'Magic-link tokens for company access to shortlists without authentication';
COMMENT ON COLUMN shortlist_access_tokens.token IS 'Unique secure token (64 bytes base64 encoded)';
COMMENT ON COLUMN shortlist_access_tokens.expires_at IS 'Token expiration (typically 7 days from creation)';
COMMENT ON COLUMN shortlist_access_tokens.used_at IS 'Set when token is used for approval (single-use for approval, multi-use for viewing)';

-- ============================================================================
-- 2. Create indexes for shortlist_access_tokens
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_shortlist_access_tokens_token
    ON shortlist_access_tokens(token);

CREATE INDEX IF NOT EXISTS idx_shortlist_access_tokens_shortlist_id
    ON shortlist_access_tokens(shortlist_request_id);

-- ============================================================================
-- 3. Make company.user_id nullable
-- Companies can now exist without a linked user account (passwordless operation)
-- ============================================================================

ALTER TABLE companies ALTER COLUMN user_id DROP NOT NULL;

-- ============================================================================
-- 4. Add contact_email column to companies
-- For passwordless companies identified by email only
-- ============================================================================

ALTER TABLE companies ADD COLUMN IF NOT EXISTS contact_email VARCHAR(255);

COMMENT ON COLUMN companies.contact_email IS 'Contact email for passwordless companies (no linked user account)';

-- ============================================================================
-- 5. Create index for contact_email lookup
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_companies_contact_email
    ON companies(contact_email);

COMMIT;

-- ============================================================================
-- VERIFICATION QUERIES (run after migration to confirm success)
-- ============================================================================

-- Check shortlist_access_tokens table exists with correct columns
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'shortlist_access_tokens'
ORDER BY ordinal_position;

-- Check companies.user_id is now nullable
SELECT column_name, is_nullable
FROM information_schema.columns
WHERE table_name = 'companies' AND column_name = 'user_id';

-- Check companies.contact_email column exists
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'companies' AND column_name = 'contact_email';

-- Check indexes were created
SELECT indexname, indexdef
FROM pg_indexes
WHERE tablename IN ('shortlist_access_tokens', 'companies')
AND indexname LIKE 'idx_%';
