-- Migration: 025_PricingSuggestion
-- Adds pricing suggestion and factors tracking to shortlist_requests

-- Add pricing suggestion fields
ALTER TABLE shortlist_requests
ADD COLUMN IF NOT EXISTS suggested_price DECIMAL(10,2),
ADD COLUMN IF NOT EXISTS pricing_factors JSONB,
ADD COLUMN IF NOT EXISTS is_rare_role BOOLEAN DEFAULT FALSE,
ADD COLUMN IF NOT EXISTS price_overridden BOOLEAN DEFAULT FALSE;

COMMENT ON COLUMN shortlist_requests.suggested_price IS 'System-calculated suggested price based on seniority, size, rarity';
COMMENT ON COLUMN shortlist_requests.pricing_factors IS 'JSON with breakdown: seniority, basePrice, candidateCount, sizeAdjustment, isRare, rarePremium';
COMMENT ON COLUMN shortlist_requests.is_rare_role IS 'Admin flag: true if role is rare/hard-to-fill (adds premium)';
COMMENT ON COLUMN shortlist_requests.price_overridden IS 'True if admin changed the suggested price';
