-- Admin User Seeding
-- NOTE: Admin user is now seeded automatically via DatabaseSeeder.cs on application startup
-- This ensures proper BCrypt password hashing

-- Admin credentials:
-- Email: admin@bixo.com
-- Password: Admin123!

-- If you need to manually insert an admin, generate a BCrypt hash first:
-- Use cost factor 11 (default in BCrypt.Net)
-- Example: dotnet script to generate hash:
--   BCrypt.Net.BCrypt.HashPassword("Admin123!")

-- Manual fallback (uncomment and replace HASH with actual BCrypt hash):
-- INSERT INTO users (id, email, password_hash, user_type, is_active, created_at, updated_at, last_active_at)
-- VALUES (
--     'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
--     'admin@bixo.com',
--     'BCRYPT_HASH_HERE',
--     2,
--     TRUE,
--     NOW(),
--     NOW(),
--     NOW()
-- )
-- ON CONFLICT (email) DO NOTHING;
