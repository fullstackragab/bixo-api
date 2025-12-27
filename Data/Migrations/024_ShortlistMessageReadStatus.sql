-- Migration: 024_ShortlistMessageReadStatus
-- Adds read tracking to shortlist messages for unread badge

ALTER TABLE shortlist_messages
ADD COLUMN IF NOT EXISTS is_read BOOLEAN DEFAULT FALSE;

-- Index for counting unread messages efficiently
CREATE INDEX IF NOT EXISTS idx_shortlist_messages_unread
ON shortlist_messages(candidate_id, is_read) WHERE is_read = FALSE;

COMMENT ON COLUMN shortlist_messages.is_read IS 'Whether the candidate has read this message';
