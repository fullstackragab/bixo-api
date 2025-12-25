-- Migration: 009_SystemMessages
-- Adds support for system-generated, immutable messages

-- Add system message fields to shortlist_messages
ALTER TABLE shortlist_messages
ADD COLUMN IF NOT EXISTS is_system BOOLEAN DEFAULT FALSE,
ADD COLUMN IF NOT EXISTS message_type TEXT DEFAULT 'company';

-- message_type values:
-- 'company' - Message from company to candidate
-- 'shortlisted' - System message when candidate is shortlisted
-- 'declined' - System message when candidate declines

-- Index for filtering system messages
CREATE INDEX IF NOT EXISTS idx_shortlist_messages_is_system
ON shortlist_messages(is_system) WHERE is_system = TRUE;

-- Add decline tracking to shortlist_candidates
ALTER TABLE shortlist_candidates
ADD COLUMN IF NOT EXISTS declined_at TIMESTAMP,
ADD COLUMN IF NOT EXISTS decline_reason TEXT;
