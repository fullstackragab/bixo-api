-- Migration: 021_ShortlistAdjustmentOptions
-- Description: Support for suggest-adjustment and extend-search workflows

-- Add columns for adjustment suggestion (Option A)
ALTER TABLE shortlist_requests ADD COLUMN IF NOT EXISTS adjustment_suggestion TEXT;

-- Add columns for search extension (Option B)
ALTER TABLE shortlist_requests ADD COLUMN IF NOT EXISTS search_extended_at TIMESTAMP WITH TIME ZONE;
ALTER TABLE shortlist_requests ADD COLUMN IF NOT EXISTS search_deadline TIMESTAMP WITH TIME ZONE;
ALTER TABLE shortlist_requests ADD COLUMN IF NOT EXISTS extension_notes TEXT;

COMMENT ON COLUMN shortlist_requests.adjustment_suggestion IS 'Admin message suggesting brief adjustments when no match found';
COMMENT ON COLUMN shortlist_requests.search_extended_at IS 'When the search window was extended';
COMMENT ON COLUMN shortlist_requests.search_deadline IS 'Extended deadline for candidate search';
COMMENT ON COLUMN shortlist_requests.extension_notes IS 'Admin message explaining search extension';
