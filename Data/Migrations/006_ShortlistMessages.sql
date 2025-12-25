-- Shortlist Messages Table
CREATE TABLE IF NOT EXISTS shortlist_messages (
    id UUID PRIMARY KEY,
    shortlist_id UUID NOT NULL REFERENCES shortlist_requests(id),
    company_id UUID NOT NULL REFERENCES companies(id),
    candidate_id UUID NOT NULL REFERENCES candidates(id),
    message TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT now()
);

-- Indexes for efficient queries
CREATE INDEX IF NOT EXISTS idx_shortlist_messages_shortlist_id ON shortlist_messages(shortlist_id);
CREATE INDEX IF NOT EXISTS idx_shortlist_messages_candidate_id ON shortlist_messages(candidate_id);
CREATE INDEX IF NOT EXISTS idx_shortlist_messages_company_id ON shortlist_messages(company_id);
CREATE INDEX IF NOT EXISTS idx_shortlist_messages_created_at ON shortlist_messages(created_at DESC);
