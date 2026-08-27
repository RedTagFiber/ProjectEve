$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $repo "DATA\ProjectEveGoldenNpcProfessionalPopulation.cs"
$program = Join-Path $repo "Program.cs"

if (-not (Test-Path $source)) {
    throw "Missing DATA\ProjectEveGoldenNpcProfessionalPopulation.cs"
}

if (-not (Test-Path $program)) {
    throw "Program.cs was not found at repo root."
}

$text = [System.IO.File]::ReadAllText($program)

if ($text -match 'case\s+"golden-populate-professional"') {
    Write-Host "golden-populate-professional command already exists. Program.cs not changed."
}
else {
    $anchor = '            case "repair-relationships":'
    $index = $text.IndexOf($anchor)

    if ($index -lt 0) {
        throw 'Could not find the repair-relationships switch anchor. Program.cs was not changed.'
    }

    $block = @'
            case "golden-populate-professional":
            case "golden-populate-eve-professional":
                {
                    ProjectEveGoldenNpcProfessionalPopulation.PopulateEveProfessional();
                    break;
                }

'@

    $text = $text.Insert($index, $block)
    [System.IO.File]::WriteAllText(
        $program,
        $text,
        [System.Text.UTF8Encoding]::new($true))

    Write-Host "Added golden-populate-professional command to Program.cs."
}

Write-Host ""
Write-Host "Golden NPC 2B-3 files are in place."
Write-Host "Next run:"
Write-Host "  dotnet build"
Write-Host "  dotnet run -- golden-populate-professional"
Write-Host "  dotnet run -- golden-audit 1"
