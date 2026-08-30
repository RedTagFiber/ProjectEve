$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$pagePath = Join-Path $repo "Components\Pages\CanonicalProfessional.razor"

if (-not (Test-Path $pagePath)) {
    throw "Missing Components\Pages\CanonicalProfessional.razor"
}

$page = [System.IO.File]::ReadAllText($pagePath)

# 1. Change Professional Notes binding to update while typing.
$oldNotes = '<textarea rows="3" @bind="_bundle.ProfessionalProfile.Notes"></textarea>'
$newNotes = '<textarea rows="3" @bind="_bundle.ProfessionalProfile.Notes" @bind:event="oninput"></textarea>'

if ($page.Contains($oldNotes)) {
    $page = $page.Replace($oldNotes, $newNotes)
    Write-Host "Professional Notes now uses oninput binding."
}
elseif ($page.Contains($newNotes)) {
    Write-Host "Professional Notes oninput binding already present."
}
else {
    throw "Could not find Professional Notes textarea anchor."
}

# 2. Add a local Save & Verify button under Professional Notes.
$anchor = @'
            <CanonicalField Label="Professional Notes">
                <textarea rows="3" @bind="_bundle.ProfessionalProfile.Notes" @bind:event="oninput"></textarea>
            </CanonicalField>
'@

$replacement = @'
            <CanonicalField Label="Professional Notes">
                <textarea rows="3" @bind="_bundle.ProfessionalProfile.Notes" @bind:event="oninput"></textarea>
            </CanonicalField>

            <div class="save-verify-row">
                <button class="btn primary" @onclick="SaveAndVerifyProfileAsync" disabled="@_busy">
                    Save & Verify Profile
                </button>
            </div>
'@

if ($page.Contains($anchor) -and $page -notmatch 'SaveAndVerifyProfileAsync') {
    $page = $page.Replace($anchor, $replacement)
    Write-Host "Added Save & Verify Profile button."
}

# 3. Add handler.
$handlerAnchor = @'
    private Task SaveProfileAsync()
        => _bundle is null ? Task.CompletedTask : RunSave(
            () => Repo.SaveCanonicalProfessionalProfileAsync(_bundle.ProfessionalProfile),
            "Professional profile saved.");
'@

$handlerReplacement = @'
    private Task SaveProfileAsync()
        => _bundle is null ? Task.CompletedTask : RunSave(
            () => Repo.SaveCanonicalProfessionalProfileAsync(_bundle.ProfessionalProfile),
            "Professional profile saved.");

    private async Task SaveAndVerifyProfileAsync()
    {
        if (_bundle is null) return;

        _busy = true;
        _message = "";
        _error = "";

        try
        {
            var expected = _bundle.ProfessionalProfile.Notes ?? "";

            var saved = await Repo.SaveAndVerifyCanonicalProfessionalProfileAsync(
                _bundle.ProfessionalProfile);

            _bundle = await Repo.GetCanonicalProfessionalBundleAsync(NpcId);

            var reloaded = _bundle?.ProfessionalProfile.Notes ?? "";

            if (!string.Equals(saved, reloaded, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Save succeeded, but page reload returned a different Notes value.");
            }

            _message =
                "SAVE VERIFIED: SQLite stored the Professional Notes and the page reloaded the exact same value.";
        }
        catch (Exception ex)
        {
            _error = ex.ToString();
        }
        finally
        {
            _busy = false;
        }
    }
'@

if ($page.Contains($handlerAnchor) -and $page -notmatch 'SAVE VERIFIED') {
    $page = $page.Replace($handlerAnchor, $handlerReplacement)
    Write-Host "Added SaveAndVerifyProfileAsync handler."
}

[System.IO.File]::WriteAllText(
    $pagePath,
    $page,
    [System.Text.UTF8Encoding]::new($true))

Write-Host ""
Write-Host "Write Bridge 2 save verification fix installed."
Write-Host ""
Write-Host "Next:"
Write-Host '  Get-Process ProjectEve.NpcStudio -ErrorAction SilentlyContinue | Stop-Process -Force'
Write-Host '  dotnet build'
Write-Host '  dotnet run'
Write-Host ""
Write-Host "Then open:"
Write-Host "  http://localhost:5123/canonical-professional/1"
