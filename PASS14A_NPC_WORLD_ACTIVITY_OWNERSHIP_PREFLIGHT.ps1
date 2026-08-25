$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

$plannerRel = 'World\SmallTown\Activity\ActivityPlanner.cs'
$engineRel  = 'World\SmallTown\Activity\WorldActivityEngine.cs'
$setupRel   = 'DATA\ProjectEveDatabaseSetup.cs'

foreach ($rel in @($plannerRel,$engineRel,$setupRel)) {
    if (-not (Test-Path (Join-Path $root $rel))) {
        throw "Required file not found: $rel"
    }
}

function Write-Context {
    param(
        [System.Collections.Generic.List[string]]$Output,
        [string]$Path,
        [int]$Line,
        [int]$Radius = 45
    )

    $lines = Get-Content $Path
    $start = [Math]::Max(1, $Line - $Radius)
    $end   = [Math]::Min($lines.Count, $Line + $Radius)

    $Output.Add("## $([System.IO.Path]::GetFileName($Path)) : line $Line")
    $Output.Add("")
    $Output.Add('```csharp')
    for ($n=$start; $n -le $end; $n++) {
        $mark = if ($n -eq $Line) { '>>' } else { '  ' }
        $Output.Add(('{0} {1,5}: {2}' -f $mark, $n, $lines[$n-1]))
    }
    $Output.Add('```')
    $Output.Add("")
}

$plannerPath = Join-Path $root $plannerRel
$enginePath  = Join-Path $root $engineRel
$setupPath   = Join-Path $root $setupRel

$plannerLines = Get-Content $plannerPath
$engineLines  = Get-Content $enginePath
$setupLines   = Get-Content $setupPath

$plannerWrite = -1
for ($i=0; $i -lt $plannerLines.Count; $i++) {
    if ($plannerLines[$i] -match '(?i)INSERT\s+INTO\s+NpcWorldActivity') {
        $plannerWrite = $i + 1
        break
    }
}

$engineWrite = -1
$engineCreate = -1
for ($i=0; $i -lt $engineLines.Count; $i++) {
    if ($engineWrite -lt 0 -and $engineLines[$i] -match '(?i)INSERT\s+INTO\s+NpcWorldActivity') {
        $engineWrite = $i + 1
    }
    if ($engineCreate -lt 0 -and $engineLines[$i] -match '(?i)CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+NpcWorldActivity') {
        $engineCreate = $i + 1
    }
}

if ($plannerWrite -lt 0) { throw 'ActivityPlanner NpcWorldActivity write not found.' }
if ($engineWrite -lt 0)  { throw 'WorldActivityEngine NpcWorldActivity write not found.' }
if ($engineCreate -lt 0) { throw 'WorldActivityEngine NpcWorldActivity CREATE not found.' }

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$outRoot = Join-Path 'D:\ProjectEve\Audit' ("Pass14A_NpcWorldActivity_Ownership_Preflight_" + $stamp)
New-Item -ItemType Directory -Path $outRoot -Force | Out-Null

$ctx = New-Object 'System.Collections.Generic.List[string]'
$ctx.Add('# Pass 14A NpcWorldActivity Ownership Context')
$ctx.Add('')
Write-Context -Output $ctx -Path $plannerPath -Line $plannerWrite -Radius 55
Write-Context -Output $ctx -Path $enginePath -Line $engineWrite -Radius 55
Write-Context -Output $ctx -Path $enginePath -Line $engineCreate -Radius 55
$ctx | Set-Content (Join-Path $outRoot 'PASS14A_NPC_WORLD_ACTIVITY_CONTEXT.md') -Encoding UTF8

# Capture method signatures and database connection definitions.
$api = New-Object 'System.Collections.Generic.List[string]'
$api.Add('# Pass 14A World Activity API / DB Context')
$api.Add('')

foreach ($pair in @(
    @{Name=$plannerRel; Path=$plannerPath},
    @{Name=$engineRel; Path=$enginePath}
)) {
    $lines = Get-Content $pair.Path
    $api.Add("## $($pair.Name)")
    $api.Add('')

    for ($i=0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match 'ConnStr|DbPath|LocationDatabasePath|MainDatabasePath' -or
            $line -match '^\s*(public|private|internal)\s+static\s+.*\(' -or
            $line -match '^\s*public\s+sealed\s+class\s+ActivityState') {

            $start = [Math]::Max(0, $i - 3)
            $end = [Math]::Min($lines.Count - 1, $i + 10)

            $api.Add('```csharp')
            for ($n=$start; $n -le $end; $n++) {
                $api.Add(('{0,5}: {1}' -f ($n+1), $lines[$n]))
            }
            $api.Add('```')
            $api.Add('')
        }
    }
}
$api | Set-Content (Join-Path $outRoot 'PASS14A_WORLD_ACTIVITY_API_CONTEXT.md') -Encoding UTF8

# Determine if canonical setup already owns the schema.
$setupHasTable = $false
foreach ($line in $setupLines) {
    if ($line -match '(?i)NpcWorldActivity') {
        $setupHasTable = $true
        break
    }
}

$summary = @()
$summary += '# Pass 14A NpcWorldActivity Ownership Preflight'
$summary += ''
$summary += 'Current runtime writers:'
$summary += '- World\SmallTown\Activity\ActivityPlanner.cs'
$summary += '- World\SmallTown\Activity\WorldActivityEngine.cs'
$summary += ''
$summary += 'Current schema owner:'
$summary += '- World\SmallTown\Activity\WorldActivityEngine.cs'
$summary += ''
$summary += "ProjectEveDatabaseSetup already mentions NpcWorldActivity: $setupHasTable"
$summary += ''
$summary += 'Target architecture:'
$summary += '- ProjectEveDatabaseSetup = single schema owner'
$summary += '- WorldActivityEngine = single runtime state writer/gateway'
$summary += '- ActivityPlanner = planning/orchestration only; calls WorldActivityEngine'
$summary += ''
$summary += 'No source files, databases, world activity rows, or NPCs were modified.'

$summary | Set-Content (Join-Path $outRoot 'PASS14A_NPC_WORLD_ACTIVITY_SUMMARY.md') -Encoding UTF8

Write-Host ''
Write-Host 'Pass 14A NpcWorldActivity ownership preflight complete.' -ForegroundColor Green
Write-Host ('Output: ' + $outRoot)
Write-Host ''
Write-Host 'Current writers: 2'
Write-Host '  World\SmallTown\Activity\ActivityPlanner.cs'
Write-Host '  World\SmallTown\Activity\WorldActivityEngine.cs'
Write-Host 'Current schema owner: WorldActivityEngine.cs'
Write-Host ('ProjectEveDatabaseSetup mentions NpcWorldActivity: ' + $setupHasTable)
Write-Host ''
Write-Host 'Upload these three files next:'
Write-Host '  PASS14A_NPC_WORLD_ACTIVITY_SUMMARY.md'
Write-Host '  PASS14A_NPC_WORLD_ACTIVITY_CONTEXT.md'
Write-Host '  PASS14A_WORLD_ACTIVITY_API_CONTEXT.md'
Write-Host ''
Write-Host 'No source files, databases, world activity rows, or NPCs were modified.'
