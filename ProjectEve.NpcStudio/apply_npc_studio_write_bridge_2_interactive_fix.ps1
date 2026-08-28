$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$pagePath = Join-Path $repo "Components\Pages\CanonicalProfessional.razor"
$verifyPath = Join-Path $repo "Components\Pages\CanonicalFoundation.razor"

if (-not (Test-Path $pagePath)) {
    throw "Missing Components\Pages\CanonicalProfessional.razor"
}

function Ensure-InteractiveServer([string]$Path) {
    if (-not (Test-Path $Path)) {
        return
    }

    $text = [System.IO.File]::ReadAllText($Path)

    if ($text -match '(?m)^@rendermode\s+InteractiveServer\s*$') {
        Write-Host "InteractiveServer already present: $Path"
        return
    }

    $pageMatch = [regex]::Match($text, '(?m)^@page\s+.+$')

    if (-not $pageMatch.Success) {
        throw "Could not find @page directive in $Path"
    }

    $insertAt = $pageMatch.Index + $pageMatch.Length
    $text = $text.Insert($insertAt, "`r`n@rendermode InteractiveServer")

    [System.IO.File]::WriteAllText(
        $Path,
        $text,
        [System.Text.UTF8Encoding]::new($true))

    Write-Host "Added @rendermode InteractiveServer: $Path"
}

Ensure-InteractiveServer $pagePath
Ensure-InteractiveServer $verifyPath

Write-Host ""
Write-Host "Canonical pages are now InteractiveServer."
Write-Host ""
Write-Host "Next:"
Write-Host '  Get-Process ProjectEve.NpcStudio -ErrorAction SilentlyContinue | Stop-Process -Force'
Write-Host '  dotnet build'
Write-Host '  dotnet run'
Write-Host ""
Write-Host "Then hard-refresh the browser (Ctrl+F5) and open:"
Write-Host "  http://localhost:5123/canonical-professional/1"
