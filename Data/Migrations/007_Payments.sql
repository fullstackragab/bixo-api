-- Payments Table
CREATE TABLE IF NOT EXISTS payments (
    id UUID PRIMARY KEY,
    company_id UUID NOT NULL REFERENCES companies(id),
    shortlist_request_id UUID NOT NULL REFERENCES shortlist_requests(id),
    provider TEXT NOT NULL, -- stripe | paypal | usdc
    provider_reference TEXT NOT NULL,
    amount_authorized NUMERIC NOT NULL,
    amount_captured NUMERIC DEFAULT 0,
    currency TEXT NOT NULL DEFAULT 'USD',
    status TEXT NOT NULL DEFAULT 'initiated',
    -- Status values: initiated | authorized | escrowed | captured | partial | released | failed
    error_message TEXT,
    metadata JSONB,
    created_at TIMESTAMP DEFAULT now(),
    updated_at TIMESTAMP DEFAULT now()
);

-- Indexes for payments
CREATE INDEX IF NOT EXISTS idx_payments_company_id ON payments(company_id);
CREATE INDEX IF NOT EXISTS idx_payments_shortlist_request_id ON payments(shortlist_request_id);
CREATE INDEX IF NOT EXISTS idx_payments_provider ON payments(provider);
CREATE INDEX IF NOT EXISTS idx_payments_status ON payments(status);

-- Extend shortlist_requests with payment fields
ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS payment_id UUID REFERENCES payments(id),
ADD COLUMN IF NOT EXISTS final_price NUMERIC;

-- Note: pricing_type already exists from previous migration (004_VersionedShortlists.sql)

-- Payment audit log for tracking state transitions
CREATE TABLE IF NOT EXISTS payment_audit_log (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    payment_id UUID NOT NULL REFERENCES payments(id),
    previous_status TEXT,
    new_status TEXT NOT NULL,
    action TEXT NOT NULL,
    provider_response JSONB,
    created_at TIMESTAMP DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_payment_audit_log_payment_id ON payment_audit_log(payment_id);
