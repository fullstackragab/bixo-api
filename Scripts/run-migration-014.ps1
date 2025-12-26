# PowerShell script to run migration 014_ShortlistOutcome
# Usage: .\Scripts\run-migration-014.ps1 -ConnectionString "your_connection_string"

param(
    [Parameter(Mandatory=$false)]
    [string]$ConnectionString = $env:DATABASE_CONNECTION_STRING,
    
    [Parameter(Mandatory=$false)]
    [switch]$DryRun = $false
)

# Check if npgsql is available (you may need to install it)
# Install-Package Npgsql -ProviderName NuGet -Scope CurrentUser

if ([string]::IsNullOrEmpty($ConnectionString)) {
    Write-Error "Connection string not provided. Use -ConnectionString parameter or set DATABASE_CONNECTION_STRING environment variable"
    exit 1
}

Write-Host "Migration 014: ShortlistOutcome" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Read the SQL file
$sqlFile = "Data/Migrations/014_ShortlistOutcome.sql"
if (-not (Test-Path $sqlFile)) {
    Write-Error "Migration file not found: $sqlFile"
    exit 1
}

$sql = Get-Content $sqlFile -Raw

if ($DryRun) {
    Write-Host "DRY RUN MODE - SQL that would be executed:" -ForegroundColor Yellow
    Write-Host $sql
    Write-Host ""
    Write-Host "No changes made to database (dry run)" -ForegroundColor Yellow
    exit 0
}

try {
    # Load Npgsql assembly
    Add-Type -Path "$env:USERPROFILE\.nuget\packages\npgsql\*\lib\net*\Npgsql.dll" -ErrorAction Stop
    
    Write-Host "Connecting to database..." -ForegroundColor Green
    $connection = New-Object Npgsql.NpgsqlConnection($ConnectionString)
    $connection.Open()
    
    Write-Host "Executing migration..." -ForegroundColor Green
    $command = $connection.CreateCommand()
    $command.CommandText = $sql
    $command.CommandTimeout = 300  # 5 minutes
    
    $rowsAffected = $command.ExecuteNonQuery()
    
    Write-Host ""
    Write-Host "? Migration 014 executed successfully!" -ForegroundColor Green
    Write-Host "  Rows affected: $rowsAffected" -ForegroundColor Gray
    
    $connection.Close()
    
} catch {
    Write-Host ""
    Write-Host "? Migration failed!" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Stack trace:" -ForegroundColor Gray
    Write-Host $_.Exception.StackTrace -ForegroundColor Gray
    exit 1
}

Write-Host ""
Write-Host "Database schema updated successfully." -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Fix remaining duplicate methods in ShortlistService.cs" -ForegroundColor White
Write-Host "  2. Build and test the application" -ForegroundColor White
Write-Host "  3. Test the new /admin/shortlists/{id}/no-match endpoint" -ForegroundColor White
