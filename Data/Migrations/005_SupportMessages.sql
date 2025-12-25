-- Support Messages Table
CREATE TABLE IF NOT EXISTS support_messages (
    id UUID PRIMARY KEY,
    user_id UUID NULL,
    user_type TEXT NOT NULL, -- candidate | company | anonymous
    subject TEXT NOT NULL,
    message TEXT NOT NULL,
    email TEXT NULL, -- optional reply-to
    status TEXT NOT NULL DEFAULT 'new', -- new | read | replied
    created_at TIMESTAMP DEFAULT now()
);

-- Index for faster queries
CREATE INDEX IF NOT EXISTS idx_support_messages_status ON support_messages(status);
CREATE INDEX IF NOT EXISTS idx_support_messages_created_at ON support_messages(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_support_messages_user_id ON support_messages(user_id) WHERE user_id IS NOT NULL;
