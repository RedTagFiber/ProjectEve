$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run this script from the folder containing ProjectEve.csproj.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot = Join-Path 'D:\ProjectEve\Backups\CanonicalPass4Fix' $stamp
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

function Backup-Relative([string]$relativePath) {
    $src = Join-Path $root $relativePath
    if (-not (Test-Path $src)) { return }
    $dst = Join-Path $backupRoot $relativePath
    New-Item -ItemType Directory -Path (Split-Path $dst -Parent) -Force | Out-Null
    Copy-Item $src $dst -Force
}

function Find-MatchingBrace([string]$text, [int]$openIndex) {
    $depth = 0
    $inString = $false
    $stringChar = [char]0
    $escape = $false

    for ($i = $openIndex; $i -lt $text.Length; $i++) {
        $c = $text[$i]

        if ($inString) {
            if ($escape) {
                $escape = $false
                continue
            }
            if ($c -eq '\') {
                $escape = $true
                continue
            }
            if ($c -eq $stringChar) {
                $inString = $false
            }
            continue
        }

        if ($c -eq '"' -or $c -eq "'") {
            $inString = $true
            $stringChar = $c
            continue
        }

        if ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') {
            $depth--
            if ($depth -eq 0) { return $i }
        }
    }

    return -1
}

function Replace-MethodBody {
    param(
        [string]$Text,
        [string]$MethodToken,
        [string]$Body,
        [string]$AlreadyMarker = ''
    )

    if (-not [string]::IsNullOrWhiteSpace($AlreadyMarker) -and
        $Text.IndexOf($AlreadyMarker, [System.StringComparison]::Ordinal) -ge 0) {
        return $Text
    }

    $methodIndex = $Text.IndexOf($MethodToken, [System.StringComparison]::Ordinal)
    if ($methodIndex -lt 0) {
        throw ('Could not find method token: ' + $MethodToken)
    }

    $open = $Text.IndexOf('{', $methodIndex)
    if ($open -lt 0) {
        throw ('Could not find opening brace for: ' + $MethodToken)
    }

    $close = Find-MatchingBrace $Text $open
    if ($close -lt 0) {
        throw ('Could not find closing brace for: ' + $MethodToken)
    }

    $nl = [Environment]::NewLine
    $replacement = '{' + $nl + $Body.TrimEnd() + $nl + '    }'
    return $Text.Substring(0, $open) + $replacement + $Text.Substring($close + 1)
}

Backup-Relative 'Characters\Base\CharacterRepository.cs'
Backup-Relative 'Program.cs'
Backup-Relative 'DATA\ProjectEveDatabaseSetup.cs'

$nl = [Environment]::NewLine

# ------------------------------------------------------------
# Ensure canonical trait-control schema exists.
# ------------------------------------------------------------
$setupPath = Join-Path $root 'DATA\ProjectEveDatabaseSetup.cs'
$setup = Get-Content $setupPath -Raw

if ($setup -notmatch 'CREATE TABLE IF NOT EXISTS NpcTraitControl') {
    $anchor = 'CREATE TABLE IF NOT EXISTS NpcEmotionTriggers'
    $idx = $setup.IndexOf($anchor, [System.StringComparison]::Ordinal)
    if ($idx -lt 0) {
        throw 'Could not find NpcEmotionTriggers schema anchor.'
    }

    $schema = @'
        CREATE TABLE IF NOT EXISTS NpcTraitControl
        (
            NpcId INTEGER NOT NULL,
            TraitId TEXT NOT NULL,
            Control INTEGER NOT NULL DEFAULT 50,
            LastUpdatedRealAt TEXT NOT NULL DEFAULT '',
            PRIMARY KEY (NpcId, TraitId),
            FOREIGN KEY (NpcId) REFERENCES Characters(Id) ON DELETE CASCADE
        );

'@
    $setup = $setup.Insert($idx, $schema)
    Set-Content $setupPath $setup -Encoding UTF8
}

# ------------------------------------------------------------
# CharacterRepository - robust method-body replacement.
# ------------------------------------------------------------
$repoPath = Join-Path $root 'Characters\Base\CharacterRepository.cs'
$repo = Get-Content $repoPath -Raw

if ($repo -notmatch 'using\s+ProjectEve\.Characters\.Traits\s*;') {
    $firstUsing = $repo.IndexOf('using ')
    $repo = $repo.Insert($firstUsing, 'using ProjectEve.Characters.Traits;' + $nl)
}

$loadTraitsBody = @'
            npc.Traits ??= new NpcTraits();

            var loaded = NpcTraitRepository.LoadAll(npc.Id);
            if (loaded.Count == 0)
                return;

            var fast = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var mid = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var slow = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in loaded)
            {
                if (pair.Key.StartsWith("mid.", StringComparison.OrdinalIgnoreCase))
                    mid[pair.Key] = pair.Value;
                else if (pair.Key.StartsWith("slow.", StringComparison.OrdinalIgnoreCase))
                    slow[pair.Key] = pair.Value;
                else
                    fast[pair.Key] = pair.Value;
            }

            if (fast.Count == 0)
                fast = TraitJsonLoader.BuildFastDefaults(45f);

            npc.Traits.InitializeFromLayers(fast, mid, slow);
'@

$repo = Replace-MethodBody `
    -Text $repo `
    -MethodToken 'private static void LoadTraits(' `
    -Body $loadTraitsBody `
    -AlreadyMarker 'NpcTraitRepository.LoadAll(npc.Id)'

$saveTraitsBody = @'
            if (traits == null)
                return;

            NpcTraitRepository.SaveAll(npcId, traits);
'@

$repo = Replace-MethodBody `
    -Text $repo `
    -MethodToken 'public static void SaveTraits(' `
    -Body $saveTraitsBody `
    -AlreadyMarker 'NpcTraitRepository.SaveAll(npcId, traits)'

$loadRelationshipsBody = @'
            npc.Relationships = RelationshipRepository.LoadForSource(npc.Id);
'@

$repo = Replace-MethodBody `
    -Text $repo `
    -MethodToken 'private static void LoadRelationships(' `
    -Body $loadRelationshipsBody `
    -AlreadyMarker 'RelationshipRepository.LoadForSource(npc.Id)'

Set-Content $repoPath $repo -Encoding UTF8

# ------------------------------------------------------------
# Program - robust method-body replacement.
# ------------------------------------------------------------
$programPath = Join-Path $root 'Program.cs'
$program = Get-Content $programPath -Raw

$saveStudioTraitsBody = @'
        if (npc?.Traits == null)
            return;

        CharacterRepository.SaveTraits(npc.Id, npc.Traits);
'@

$program = Replace-MethodBody `
    -Text $program `
    -MethodToken 'static void SaveNpcTraitsToStudioTable(' `
    -Body $saveStudioTraitsBody `
    -AlreadyMarker 'CharacterRepository.SaveTraits(npc.Id, npc.Traits)'

$upsertBody = @'
        RelationshipRepository.Upsert(
            npcId,
            targetCharacterId: null,
            targetName,
            relationshipType,
            trust,
            respect,
            affection,
            attraction,
            tension,
            notes);
'@

$program = Replace-MethodBody `
    -Text $program `
    -MethodToken 'static void UpsertRelationship(' `
    -Body $upsertBody `
    -AlreadyMarker 'RelationshipRepository.Upsert('

$existsBody = @'
        return RelationshipRepository.Exists(npcId, targetName, relationshipType);
'@

$program = Replace-MethodBody `
    -Text $program `
    -MethodToken 'static bool RelationshipExists(' `
    -Body $existsBody `
    -AlreadyMarker 'RelationshipRepository.Exists(npcId, targetName, relationshipType)'

# Remove duplicate canonical table creation from Program if still present.
$canonicalTables = @(
    'NpcRelationships',
    'NpcAppearanceProfiles',
    'NpcVoiceProfiles',
    'NpcTraitValues',
    'NpcBuildRevisions'
)

foreach ($table in $canonicalTables) {
    $pattern = '(?s)\s*Execute\s*\(\s*conn\s*,\s*"""\s*CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+' +
               [regex]::Escape($table) +
               '\b.*?"""\s*\)\s*;'
    $program = [regex]::Replace($program, $pattern, '', 1)
}

if ($program -notmatch 'ProjectEveOwnershipVerifier\.PrintToConsole\(\);') {
    $needle = 'ProjectEveFinanceVerifier.PrintToConsole();'
    $idx = $program.IndexOf($needle, [System.StringComparison]::Ordinal)

    if ($idx -ge 0) {
        $program = $program.Insert(
            $idx + $needle.Length,
            $nl + '        ProjectEveOwnershipVerifier.PrintToConsole();'
        )
    }
}

Set-Content $programPath $program -Encoding UTF8

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 4 repair/continuation applied.' -ForegroundColor Green
Write-Host ('Backup: ' + $backupRoot)
Write-Host ''
Write-Host 'This script is idempotent: already-migrated methods were skipped.'
Write-Host ''
Write-Host 'Run next:'
Write-Host '  dotnet build'
Write-Host '  dotnet run -- verify'
