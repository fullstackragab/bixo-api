-- Migration: 017_SkillLevels
-- Description: Add skill level (Primary/Secondary) to support curated skill presentation

-- Add skill_level column: 0 = Primary (core skills), 1 = Secondary (supporting tools)
ALTER TABLE candidate_skills
ADD COLUMN IF NOT EXISTS skill_level INTEGER NOT NULL DEFAULT 1;

-- Create index for filtering by level
CREATE INDEX IF NOT EXISTS idx_candidate_skills_level ON candidate_skills(candidate_id, skill_level);

-- Add comments
COMMENT ON COLUMN candidate_skills.skill_level IS 'Skill priority: 0 = Primary (max 7), 1 = Secondary (unlimited)';

-- Note: Max 7 Primary skills is enforced at application level
