-- Bixo Job Platform - Initial Database Schema
-- PostgreSQL

-- Enable UUID extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Users table
CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    email VARCHAR(255) NOT NULL UNIQUE,
    password_hash VARCHAR(255),
    user_type INTEGER NOT NULL DEFAULT 0, -- 0=Candidate, 1=Company, 2=Admin
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    last_active_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_users_email ON users(email);
CREATE INDEX idx_users_user_type ON users(user_type);

-- Refresh tokens table
CREATE TABLE refresh_tokens (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token VARCHAR(500) NOT NULL UNIQUE,
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    revoked_at TIMESTAMP WITH TIME ZONE,
    replaced_by_token VARCHAR(500)
);

CREATE INDEX idx_refresh_tokens_user_id ON refresh_tokens(user_id);
CREATE INDEX idx_refresh_tokens_token ON refresh_tokens(token);

-- Candidates table
CREATE TABLE candidates (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    first_name VARCHAR(100),
    last_name VARCHAR(100),
    linkedin_url VARCHAR(500),
    cv_file_key VARCHAR(500),
    cv_original_file_name VARCHAR(255),
    desired_role VARCHAR(500),
    location_preference VARCHAR(200),
    remote_preference INTEGER, -- 0=Remote, 1=Onsite, 2=Hybrid, 3=Flexible
    availability INTEGER NOT NULL DEFAULT 0, -- 0=Open, 1=NotNow, 2=Passive
    open_to_opportunities BOOLEAN NOT NULL DEFAULT TRUE,
    profile_visible BOOLEAN NOT NULL DEFAULT TRUE,
    seniority_estimate INTEGER, -- 0=Junior, 1=Mid, 2=Senior, 3=Lead, 4=Principal
    parsed_profile_json TEXT,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_candidates_user_id ON candidates(user_id);
CREATE INDEX idx_candidates_availability ON candidates(availability);
CREATE INDEX idx_candidates_profile_visible ON candidates(profile_visible);
CREATE INDEX idx_candidates_seniority ON candidates(seniority_estimate);

-- Candidate skills table
CREATE TABLE candidate_skills (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    candidate_id UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
    skill_name VARCHAR(100) NOT NULL,
    confidence_score DECIMAL(3,2) NOT NULL DEFAULT 0.5,
    category INTEGER NOT NULL DEFAULT 5, -- 0=Language, 1=Framework, 2=Tool, 3=Database, 4=Cloud, 5=Other
    is_verified BOOLEAN NOT NULL DEFAULT FALSE,
    UNIQUE(candidate_id, skill_name)
);

CREATE INDEX idx_candidate_skills_candidate_id ON candidate_skills(candidate_id);
CREATE INDEX idx_candidate_skills_skill_name ON candidate_skills(skill_name);

-- Companies table
CREATE TABLE companies (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    company_name VARCHAR(200) NOT NULL,
    industry VARCHAR(100),
    company_size VARCHAR(50),
    website VARCHAR(500),
    logo_file_key VARCHAR(500),
    subscription_tier INTEGER NOT NULL DEFAULT 0, -- 0=Free, 1=Starter, 2=Pro
    subscription_expires_at TIMESTAMP WITH TIME ZONE,
    messages_remaining INTEGER NOT NULL DEFAULT 5,
    stripe_customer_id VARCHAR(100),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_companies_user_id ON companies(user_id);
CREATE INDEX idx_companies_subscription_tier ON companies(subscription_tier);

-- Company members table
CREATE TABLE company_members (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    company_id UUID NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role INTEGER NOT NULL DEFAULT 2, -- 0=Owner, 1=Admin, 2=Recruiter
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    UNIQUE(company_id, user_id)
);

CREATE INDEX idx_company_members_company_id ON company_members(company_id);
CREATE INDEX idx_company_members_user_id ON company_members(user_id);

-- Shortlist requests table
CREATE TABLE shortlist_requests (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    company_id UUID NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
    role_title VARCHAR(200) NOT NULL,
    tech_stack_required JSONB,
    seniority_required INTEGER,
    location_preference VARCHAR(200),
    remote_allowed BOOLEAN NOT NULL DEFAULT TRUE,
    additional_notes TEXT,
    status INTEGER NOT NULL DEFAULT 0, -- 0=Pending, 1=Processing, 2=Completed, 3=Cancelled
    price_paid DECIMAL(18,2),
    payment_intent_id VARCHAR(100),
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMP WITH TIME ZONE
);

CREATE INDEX idx_shortlist_requests_company_id ON shortlist_requests(company_id);
CREATE INDEX idx_shortlist_requests_status ON shortlist_requests(status);

-- Shortlist candidates table
CREATE TABLE shortlist_candidates (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    shortlist_request_id UUID NOT NULL REFERENCES shortlist_requests(id) ON DELETE CASCADE,
    candidate_id UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
    match_score INTEGER NOT NULL DEFAULT 0,
    match_reason TEXT,
    rank INTEGER NOT NULL DEFAULT 0,
    admin_approved BOOLEAN NOT NULL DEFAULT FALSE,
    added_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    UNIQUE(shortlist_request_id, candidate_id)
);

CREATE INDEX idx_shortlist_candidates_request_id ON shortlist_candidates(shortlist_request_id);
CREATE INDEX idx_shortlist_candidates_candidate_id ON shortlist_candidates(candidate_id);

-- Saved candidates table
CREATE TABLE saved_candidates (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    company_id UUID NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
    candidate_id UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
    notes TEXT,
    saved_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    UNIQUE(company_id, candidate_id)
);

CREATE INDEX idx_saved_candidates_company_id ON saved_candidates(company_id);
CREATE INDEX idx_saved_candidates_candidate_id ON saved_candidates(candidate_id);

-- Messages table
CREATE TABLE messages (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    from_user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    to_user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    subject VARCHAR(200),
    content TEXT NOT NULL,
    is_read BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_messages_from_user_id ON messages(from_user_id);
CREATE INDEX idx_messages_to_user_id ON messages(to_user_id);
CREATE INDEX idx_messages_created_at ON messages(created_at DESC);

-- Candidate recommendations table
CREATE TABLE candidate_recommendations (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    candidate_id UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
    recommender_email VARCHAR(255) NOT NULL,
    recommender_name VARCHAR(200),
    type INTEGER NOT NULL DEFAULT 0, -- 0=WorkedWith, 1=WouldRecommend, 2=SeenTheirWork
    verified_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_candidate_recommendations_candidate_id ON candidate_recommendations(candidate_id);

-- Candidate profile views table
CREATE TABLE candidate_profile_views (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    candidate_id UUID NOT NULL REFERENCES candidates(id) ON DELETE CASCADE,
    company_id UUID NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
    viewed_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_candidate_profile_views_candidate_id ON candidate_profile_views(candidate_id);
CREATE INDEX idx_candidate_profile_views_company_id ON candidate_profile_views(company_id);

-- Subscription plans table
CREATE TABLE subscription_plans (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name VARCHAR(100) NOT NULL,
    monthly_price DECIMAL(18,2) NOT NULL,
    yearly_price DECIMAL(18,2) NOT NULL,
    messages_per_month INTEGER NOT NULL,
    features JSONB,
    stripe_price_id_monthly VARCHAR(100),
    stripe_price_id_yearly VARCHAR(100),
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

-- Shortlist pricing table
CREATE TABLE shortlist_pricing (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name VARCHAR(100) NOT NULL,
    price DECIMAL(18,2) NOT NULL,
    shortlist_count INTEGER NOT NULL,
    discount_percent DECIMAL(5,2) NOT NULL DEFAULT 0,
    stripe_price_id VARCHAR(100),
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

-- Payments table
CREATE TABLE payments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    company_id UUID NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
    type INTEGER NOT NULL DEFAULT 0, -- 0=Subscription, 1=Shortlist
    amount DECIMAL(18,2) NOT NULL,
    currency VARCHAR(10) NOT NULL DEFAULT 'USD',
    stripe_payment_intent_id VARCHAR(100),
    stripe_subscription_id VARCHAR(100),
    status INTEGER NOT NULL DEFAULT 0, -- 0=Pending, 1=Completed, 2=Failed, 3=Refunded
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_payments_company_id ON payments(company_id);
CREATE INDEX idx_payments_status ON payments(status);

-- Notifications table
CREATE TABLE notifications (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    type VARCHAR(50) NOT NULL,
    title VARCHAR(200) NOT NULL,
    message TEXT,
    data JSONB,
    is_read BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_notifications_user_id ON notifications(user_id);
CREATE INDEX idx_notifications_is_read ON notifications(is_read);
CREATE INDEX idx_notifications_created_at ON notifications(created_at DESC);

-- Seed data for subscription plans
INSERT INTO subscription_plans (id, name, monthly_price, yearly_price, messages_per_month, features, is_active)
VALUES
    ('11111111-1111-1111-1111-111111111111', 'Starter', 49.00, 470.00, 20, '["Browse talent", "20 messages/month", "Save profiles", "Basic filters"]', TRUE),
    ('22222222-2222-2222-2222-222222222222', 'Pro', 149.00, 1430.00, 100, '["Unlimited messages", "Advanced filters", "See recommendations", "Priority access", "Analytics"]', TRUE);

-- Seed data for shortlist pricing
INSERT INTO shortlist_pricing (id, name, price, shortlist_count, discount_percent, is_active)
VALUES
    ('33333333-3333-3333-3333-333333333333', 'Single', 299.00, 1, 0, TRUE),
    ('44444444-4444-4444-4444-444444444444', 'Bundle5', 1299.00, 5, 13, TRUE),
    ('55555555-5555-5555-5555-555555555555', 'Bundle10', 2299.00, 10, 23, TRUE);
