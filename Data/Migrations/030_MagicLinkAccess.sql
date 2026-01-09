-- Migration: 030_MagicLinkAccess
-- Description: Add shortlist access tokens for magic-link company access
-- Note: These are SERVICE tokens, completely separate from auth/password reset tokens

-- Shortlist access tokens table
CREATE TABLE IF NOT EXISTS shortlist_access_tokens (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shortlist_request_id UUID NOT NULL REFERENCES shortlist_requests(id) ON DELETE CASCADE,
    token VARCHAR(128) NOT NULL UNIQUE,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    used_at TIMESTAMP WITH TIME ZONE NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_shortlist_access_tokens_token ON shortlist_access_tokens(token);
CREATE INDEX IF NOT EXISTS idx_shortlist_access_tokens_shortlist_id ON shortlist_access_tokens(shortlist_request_id);

-- Make company.user_id nullable (companies don't need user accounts)
ALTER TABLE companies ALTER COLUMN user_id DROP NOT NULL;

-- Add contact_email for passwordless companies (email-identified entities)
ALTER TABLE companies ADD COLUMN IF NOT EXISTS contact_email VARCHAR(255);

-- Index for looking up companies by contact email
CREATE INDEX IF NOT EXISTS idx_companies_contact_email ON companies(contact_email);
