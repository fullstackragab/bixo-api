namespace bixo_api.Services;

/// <summary>
/// Static capability mapping for presentation purposes only.
/// Skills are stored atomically for matching; capabilities are derived for display.
/// This does NOT affect the matching algorithm.
/// </summary>
public static class CapabilityMapping
{
    private static readonly Dictionary<string, string[]> CapabilityMap = new()
    {
        { "Frontend", new[] {
            "Angular", "React", "Next.js", "Vue", "Vue.js", "Svelte", "TypeScript", "JavaScript",
            "HTML", "CSS", "SCSS", "Sass", "Tailwind", "Bootstrap", "jQuery", "Redux", "MobX",
            "Webpack", "Vite", "Nuxt", "Gatsby", "Remix"
        }},
        { "Backend", new[] {
            "NestJS", ".NET", "Node.js", "Express", "Spring", "Django", "Flask", "FastAPI",
            "Ruby on Rails", "Laravel", "PHP", "Go", "Golang", "Rust", "Java", "Kotlin",
            "Python", "C#", "ASP.NET", "GraphQL", "REST", "gRPC"
        }},
        { "Infrastructure", new[] {
            "PostgreSQL", "MongoDB", "MySQL", "Redis", "Elasticsearch", "AWS", "Azure", "GCP",
            "Docker", "Kubernetes", "Terraform", "Linux", "Nginx", "Apache", "RabbitMQ", "Kafka",
            "S3", "Lambda", "EC2", "CloudFormation", "Vercel", "Heroku", "DigitalOcean"
        }},
        { "Mobile", new[] {
            "React Native", "Flutter", "Swift", "SwiftUI", "Kotlin", "iOS", "Android",
            "Xamarin", "Ionic", "Capacitor", "Expo"
        }},
        { "Data", new[] {
            "SQL", "NoSQL", "ETL", "Data Modeling", "BigQuery", "Snowflake", "Databricks",
            "Apache Spark", "Pandas", "NumPy", "Data Analysis", "Machine Learning", "TensorFlow",
            "PyTorch", "Scikit-learn", "Power BI", "Tableau"
        }},
        { "Practices", new[] {
            "System Design", "APIs", "Payments", "CI/CD", "DevOps", "Agile", "Scrum",
            "TDD", "Unit Testing", "Integration Testing", "Code Review", "Git", "GitHub",
            "GitLab", "Microservices", "Event-Driven", "Security", "OAuth", "Authentication"
        }}
    };

    // Reverse lookup: skill -> capability group
    private static readonly Dictionary<string, string> SkillToCapability;

    static CapabilityMapping()
    {
        SkillToCapability = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (capability, skills) in CapabilityMap)
        {
            foreach (var skill in skills)
            {
                // First match wins (skill belongs to at most one group)
                if (!SkillToCapability.ContainsKey(skill))
                {
                    SkillToCapability[skill] = capability;
                }
            }
        }
    }

    /// <summary>
    /// Derive capability groups from a list of atomic skills.
    /// Unknown skills are ignored (they still work for matching).
    /// Returns only non-empty groups.
    /// </summary>
    public static Dictionary<string, List<string>> DeriveCapabilities(IEnumerable<string> skills)
    {
        var result = new Dictionary<string, List<string>>();

        foreach (var skill in skills)
        {
            var capability = FindCapability(skill);
            if (capability != null)
            {
                if (!result.ContainsKey(capability))
                {
                    result[capability] = new List<string>();
                }

                // Avoid duplicates within a group
                if (!result[capability].Contains(skill, StringComparer.OrdinalIgnoreCase))
                {
                    result[capability].Add(skill);
                }
            }
            // Unknown skills are ignored for display but still used by matching
        }

        return result;
    }

    /// <summary>
    /// Find which capability group a skill belongs to.
    /// Uses fuzzy matching for partial matches.
    /// </summary>
    private static string? FindCapability(string skill)
    {
        if (string.IsNullOrWhiteSpace(skill))
            return null;

        // Exact match first
        if (SkillToCapability.TryGetValue(skill, out var capability))
        {
            return capability;
        }

        // Fuzzy match: check if skill contains or is contained by any known skill
        foreach (var (knownSkill, cap) in SkillToCapability)
        {
            if (skill.Contains(knownSkill, StringComparison.OrdinalIgnoreCase) ||
                knownSkill.Contains(skill, StringComparison.OrdinalIgnoreCase))
            {
                return cap;
            }
        }

        return null;
    }
}
