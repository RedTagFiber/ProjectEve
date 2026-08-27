$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $repo "DATA\ProjectEveGoldenNpcAdultLifePopulation.cs"
$program = Join-Path $repo "Program.cs"

if (-not (Test-Path $source)) {
    throw "Missing DATA\ProjectEveGoldenNpcAdultLifePopulation.cs"
}

if (-not (Test-Path $program)) {
    throw "Program.cs was not found at repo root."
}

$text = [System.IO.File]::ReadAllText($program)

if ($text -match 'case\s+"golden-populate-adult-life"') {
    Write-Host "golden-populate-adult-life command already exists. Program.cs not changed."
}
else {
    $anchor = '            case "repair-relationships":'
    $index = $text.IndexOf($anchor)

    if ($index -lt 0) {
        throw 'Could not find the repair-relationships switch anchor. Program.cs was not changed.'
    }

    $block = @'
            case "golden-populate-adult-life":
            case "golden-populate-eve-adult-life":
                {
                    ProjectEveGoldenNpcAdultLifePopulation.PopulateEveAdultLife();
                    break;
                }

'@

    $text = $text.Insert($index, $block)
    [System.IO.File]::WriteAllText(
        $program,
        $text,
        [System.Text.UTF8Encoding]::new($true))

    Write-Host "Added golden-populate-adult-life command to Program.cs."
}

Write-Host ""
Write-Host "Golden NPC 2B-4 files are in place."
Write-Host "Next run:"
Write-Host "  dotnet build"
Write-Host "  dotnet run -- golden-populate-adult-life"
Write-Host "  dotnet run -- golden-audit 1"
