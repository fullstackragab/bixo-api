using Dapper;

namespace bixo_api.Data;

public class DatabaseSeeder
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(IDbConnectionFactory db, ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        await SeedAdminUserAsync();
    }

    private async Task SeedAdminUserAsync()
    {
        using var connection = _db.CreateConnection();

        var adminExists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM users WHERE email = @Email)",
            new { Email = "admin@bixo.com" });

        if (adminExists)
        {
            _logger.LogInformation("Admin user already exists");
            return;
        }

        var adminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var now = DateTime.UtcNow;
        var passwordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!");

        await connection.ExecuteAsync(@"
            INSERT INTO users (id, email, password_hash, user_type, is_active, created_at, updated_at, last_active_at)
            VALUES (@Id, @Email, @PasswordHash, @UserType, TRUE, @Now, @Now, @Now)",
            new
            {
                Id = adminId,
                Email = "admin@bixo.com",
                PasswordHash = passwordHash,
                UserType = 2, // Admin
                Now = now
            });

        _logger.LogInformation("Admin user created successfully");
    }
}
