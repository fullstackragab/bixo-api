using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using bixo_api.Configuration;
using bixo_api.Data;
using bixo_api.Services;
using bixo_api.Services.Interfaces;
using bixo_api.Services.Payments;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<AwsSettings>(builder.Configuration.GetSection("AWS"));
builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));

// Database - Dapper with Npgsql
builder.Services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();

// Services
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICandidateService, CandidateService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IS3StorageService, S3StorageService>();
builder.Services.AddScoped<ICvParsingService, CvParsingService>();
builder.Services.AddScoped<IShortlistService, ShortlistService>();
builder.Services.AddScoped<IMatchingService, MatchingService>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();

// GitHub enrichment
builder.Services.AddHttpClient("GitHub", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Bixo-API");
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
});
builder.Services.AddScoped<IGitHubEnrichmentService, GitHubEnrichmentService>();

// Payment providers
builder.Services.AddHttpClient("PayPal");
builder.Services.AddHttpClient("Solana");
builder.Services.AddScoped<IPaymentProviderService, StripePaymentProvider>();
builder.Services.AddScoped<IPaymentProviderService, PayPalPaymentProvider>();
builder.Services.AddScoped<IPaymentProviderService, UsdcPaymentProvider>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IShortlistEventService, ShortlistEventService>();
builder.Services.AddScoped<IPricingService, PricingService>();

// Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:3000" };
        policy.WithOrigins(origins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

// OpenAPI (built-in .NET 10)
builder.Services.AddOpenApi();

var app = builder.Build();

// Seed database
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
