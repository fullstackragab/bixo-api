# PowerShell script to remove duplicate methods from ShortlistService.cs
# This fixes the compilation errors caused by duplicate method definitions

$filePath = "Services\ShortlistService.cs"
Write-Host "Fixing duplicate methods in $filePath" -ForegroundColor Cyan

# Read the entire file
$content = Get-Content $filePath -Raw

# Find the position where duplicates start (after SendNoMatchNotificationAsync method)
# The pattern we're looking for is the closing brace of SendNoMatchNotificationAsync followed by the duplicate MarkAsPaidAsync
$pattern = @'
            _logger\.LogError\(ex, "Failed to send no-match notification for shortlist \{ShortlistId\}", shortlistRequestId\);
        \}
    \}

    /// <summary>
    /// Admin marks shortlist as paid
'@

$matchInfo = [regex]::Match($content, $pattern)

if ($matchInfo.Success) {
    $endOfGoodCode = $matchInfo.Index + $matchInfo.Length - (" /// <summary>" + "`r`n" + "    /// Admin marks shortlist as paid").Length
    
    Write-Host "Found duplicate code starting at position: $endOfGoodCode" -ForegroundColor Yellow
    
    # Find where ParseTechStack method ends (this should be the last method)
    $parsePattern = @'
    private List<string> ParseTechStack\(string\? techStackJson\)
    \{
        if \(string\.IsNullOrEmpty\(techStackJson\)\) return new List<string>\(\);

        try
        \{
            return JsonSerializer\.Deserialize<List<string>>\(techStackJson\) \?\? new List<string>\(\);
        \}
        catch
        \{
            return new List<string>\(\);
        \}
    \}
\}
'@
    
    $parseMatch = [regex]::Match($content, $parsePattern)
    
    if ($parseMatch.Success) {
        # Keep everything up to the end of SendNoMatchNotificationAsync
        $goodContent = $content.Substring(0, $endOfGoodCode)
        
        # Add the ParseTechStack method and closing brace
        $goodContent += @'


    private List<string> ParseTechStack(string? techStackJson)
    {
        if (string.IsNullOrEmpty(techStackJson)) return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(techStackJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
'@
        
        # Backup original file
        $backupPath = "$filePath.backup"
        Copy-Item $filePath $backupPath -Force
        Write-Host "Created backup: $backupPath" -ForegroundColor Green
        
        # Write the cleaned content
        Set-Content -Path $filePath -Value $goodContent -NoNewline
        
        $originalLines = ($content -split "`n").Count
        $newLines = ($goodContent -split "`n").Count
        $removedLines = $originalLines - $newLines
        
        Write-Host ""
        Write-Host "? Fixed $filePath" -ForegroundColor Green
        Write-Host "  Original lines: $originalLines" -ForegroundColor Gray
        Write-Host "  New lines: $newLines" -ForegroundColor Gray
        Write-Host "  Removed lines: $removedLines" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Backup saved to: $backupPath" -ForegroundColor Cyan
        
    } else {
        Write-Host "Could not find ParseTechStack method" -ForegroundColor Red
        exit 1
    }
    
} else {
    Write-Host "Could not find the pattern to identify where duplicates start" -ForegroundColor Red
    Write-Host "The file may have already been fixed or has a different structure" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "Next step: Run 'dotnet build' to verify the fix" -ForegroundColor Cyan
