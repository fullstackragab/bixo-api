-- Migration: 015_ShortlistEmailTracking
-- Description: Track email events sent for shortlist requests (idempotent email sending)

-- Create table to track sent emails
CREATE TABLE IF NOT EXISTS shortlist_emails (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shortlist_request_id UUID NOT NULL REFERENCES shortlist_requests(id) ON DELETE CASCADE,
    email_event INTEGER NOT NULL, -- 1=PricingReady, 2=AuthorizationRequired, 3=Delivered, 4=NoMatch
    sent_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    sent_to VARCHAR(255) NOT NULL, -- Email address
    sent_by UUID REFERENCES users(id), -- Admin who triggered resend (NULL for automatic)
    is_resend BOOLEAN NOT NULL DEFAULT FALSE
);

-- Ensure each event type is sent only once per shortlist (for non-resends)
-- Using partial unique index instead of constraint
CREATE UNIQUE INDEX idx_shortlist_emails_unique_event
    ON shortlist_emails(shortlist_request_id, email_event)
    WHERE is_resend = FALSE;

-- Index for fast lookups
CREATE INDEX idx_shortlist_emails_request ON shortlist_emails(shortlist_request_id);

-- Add comments for documentation
COMMENT ON TABLE shortlist_emails IS 'Tracks email events sent for shortlist requests to prevent duplicate sends';
COMMENT ON COLUMN shortlist_emails.email_event IS 'Email event type: 1=PricingReady, 2=AuthorizationRequired, 3=Delivered, 4=NoMatch';
COMMENT ON COLUMN shortlist_emails.is_resend IS 'TRUE if this was a manual resend by admin';
