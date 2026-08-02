# PostToolUse hook: compile check after C# edits.
# A successful build is the project's main automated verification. Exit 2 feeds
# the build errors back to Claude.
# The hook exits 0 silently whenever a check is not possible (no dotnet SDK) so it
# never produces false failures on machines without the toolchain.
try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
} catch {
    exit 0
}

$path = $payload.tool_input.file_path
if (-not $path -or $path -notmatch '\.cs$') { exit 0 }
if ($path -match '[\\/](bin|obj)[\\/]') { exit 0 }

# .claude/hooks -> repo root
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sln = Join-Path $repoRoot 'src\WinRestoreKit.sln'
if (-not (Test-Path $sln)) { exit 0 }

$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if (-not $dotnet) { exit 0 }

# bin/ and obj/ are gitignored, so building in place no longer dirties the tree.
$buildOutput = & $dotnet.Source build $sln --nologo -v:q 2>&1

if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("dotnet build compile check FAILED after editing $path")
    [Console]::Error.WriteLine(($buildOutput | Out-String))
    exit 2
}
exit 0
