$ErrorActionPreference = 'Stop'

$root = (Get-Location).Path
$path = Join-Path $root 'World\SmallTown\Population\FamilyFriendWebSystem.cs'

if (-not (Test-Path (Join-Path $root 'ProjectEve.csproj'))) {
    throw 'Run from D:\ProjectEve\code\ProjectEve_Clean\ProjectEve_Clean'
}

if (-not (Test-Path $path)) {
    throw 'FamilyFriendWebSystem.cs was not found.'
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupRoot = Join-Path 'D:\ProjectEve\Backups\Pass5WarningsFix' $stamp
$backupPath = Join-Path $backupRoot 'World\SmallTown\Population\FamilyFriendWebSystem.cs'
New-Item -ItemType Directory -Path (Split-Path $backupPath -Parent) -Force | Out-Null
Copy-Item $path $backupPath -Force

$text = Get-Content $path -Raw

$old = @'
            MirrorToCanonicalRelationshipState(
                ownerNpcId,
                targetNpcId,
                relationshipType,
                webTier,
                notes);
'@

$new = @'
            MirrorToCanonicalRelationshipState(
                ownerNpcId,
                targetNpcId,
                relationshipType ?? "",
                webTier,
                notes ?? "");
'@

if ($text.Contains($old)) {
    $text = $text.Replace($old, $new)
}
elseif ($text -notmatch 'relationshipType \?\? ""') {
    throw 'Expected MirrorToCanonicalRelationshipState call was not found.'
}

Set-Content $path $text -Encoding UTF8

Remove-Item -Recurse -Force (Join-Path $root 'bin'), (Join-Path $root 'obj') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Pass 5 nullable warnings repaired.' -ForegroundColor Green
Write-Host ('Backup: ' + $backupRoot)
Write-Host ''
Write-Host 'Run:'
Write-Host '  dotnet build'
