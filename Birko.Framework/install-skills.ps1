# Installs the SHAREABLE Birko skills into ~/.claude/skills as directory junctions
# pointing back into this repo's .claude/skills — the repo stays the single source
# of truth, so `git pull` updates the live skills with no re-install.
#
# Only the consumer-facing skills are shared user-level (they're needed OUTSIDE this
# repo — scaffolding a new consumer, prototyping in a consumer app). The rest of
# .claude/skills (new-birko-subproject, new-store-backend, verify-birko-conventions,
# the roll-changelog shadow) stay project-local: Claude Code auto-loads them only
# inside this repo, which is exactly their scope.
#
# These skills BUILD ON TOP of the generic project-lifecycle-skills set
# (github.com -> project-lifecycle-skills; install that one first) — e.g.
# birko-new-project hands off to the generic new-project for the universal layer.
#
# Usage:  ./install-skills.ps1      (idempotent; safe to re-run)

$shared = @('birko-new-project', 'new-birko-web-page', 'new-birko-web-component', 'design-agent')

$repoSkills = Join-Path $PSScriptRoot '.claude\skills'
$target = Join-Path $HOME '.claude\skills'

if (-not (Test-Path $target)) { New-Item -ItemType Directory -Force $target | Out-Null }

foreach ($name in $shared) {
    $source = Join-Path $repoSkills $name
    if (-not (Test-Path $source)) { Write-Warning "${name}: missing in $repoSkills — skipped"; continue }
    $link = Join-Path $target $name
    if (Test-Path $link) {
        $existing = Get-Item $link -Force
        if ($existing.LinkType) {
            if ($existing.Target -ne $source) {
                Write-Warning "${name}: links elsewhere ($($existing.Target)) — remove it and re-run to relink here"
            } else {
                Write-Host "= $name (already linked)"
            }
            continue
        }
        Write-Warning "${name}: a real directory already exists at $link — move it aside and re-run"
        continue
    }
    New-Item -ItemType Junction -Path $link -Target $source | Out-Null
    Write-Host "+ $name -> $source"
}

Write-Host "`nDone. Shared skills resolve from this repo via junctions; edit here, they're live immediately."
