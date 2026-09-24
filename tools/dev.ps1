# Outil de développement : . .\tools\dev.ps1 ; puis Build / Run / Smoke
#   Build        compile (Debug) et affiche uniquement les erreurs/avertissements uniques
#   Run          arrête PC Pilot, compile, lance l'app (non élevée) et affiche la fin du log
#   Smoke        lance l'app, vérifie qu'elle tourne 8 s sans exception, puis la ferme
. "$PSScriptRoot\env.ps1"
$script:Root = Resolve-Path "$PSScriptRoot\.."
$script:Proj = Join-Path $Root "src\PCPilot\PCPilot.csproj"
$script:Exe = Join-Path $Root "src\PCPilot\bin\Debug\net10.0-windows10.0.22621.0\PCPilot.exe"
$script:LogFile = Join-Path $env:LOCALAPPDATA "PCPilot\logs\pcpilot.log"

function Stop-Pilot { Get-Process PCPilot -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $script:Exe } | Stop-Process -Force; Start-Sleep -Milliseconds 300 }

function Build {
    param([switch]$Quiet)
    Stop-Pilot
    $out = dotnet build $script:Proj -c Debug -nologo -v q 2>&1
    $code = $LASTEXITCODE
    $out | Select-String -Pattern ' error | warning ' | ForEach-Object {
        ($_.Line -replace '\s*\[[^\]]*\.csproj\]$', '') -replace [regex]::Escape("$($script:Root)\"), ''
    } | Select-Object -Unique | ForEach-Object { Write-Host $_ }
    if ($code -ne 0) { Write-Host "BUILD FAILED"; return $false }
    if (-not $Quiet) { Write-Host "BUILD OK" }
    return $true
}

function Run {
    if (-not (Build -Quiet)) { return }
    Remove-Item $script:LogFile -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $script:Exe -PassThru
    Start-Sleep -Seconds 6
    "alive=$(-not $p.HasExited) mem=$([math]::Round((Get-Process -Id $p.Id -ErrorAction SilentlyContinue).WorkingSet64/1MB,1))MB"
    Get-Content $script:LogFile -Tail 20 -ErrorAction SilentlyContinue
}

function Capture {
    # Capture -Page "privacy" -Theme dark -Out "$env:TEMP\pcp-shots\privacy.png" [-Param "tweak:xyz"] [-Scroll 1200]
    #   -Param  : paramètre de navigation transmis à la page (INavigationAware)
    #   -Scroll : défilement vertical (pixels) du premier ScrollViewer de la page avant la capture
    param([string]$Page = "", [string]$Theme = "light", [string]$Out = "$env:TEMP\pcp-shots\capture.png", [string]$Param = "", [int]$Scroll = 0, [switch]$NoBuild)
    if (-not $NoBuild) { if (-not (Build -Quiet)) { return } }
    New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
    Remove-Item $Out -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $script:Exe -ArgumentList @('--capture', "`"$Out`"", $(if ($Page) { $Page } else { '""' }), $Theme, $(if ($Param) { "`"$Param`"" } else { '""' }), "$Scroll") -PassThru
    if (-not $p.WaitForExit(40000)) { $p.Kill(); "CAPTURE TIMEOUT" }
    if (Test-Path $Out) { "CAPTURE OK: $Out" } else { "CAPTURE FAILED"; Get-Content $script:LogFile -Tail 10 -ErrorAction SilentlyContinue }
}

function Smoke {
    if (-not (Build -Quiet)) { return }
    Remove-Item $script:LogFile -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $script:Exe -PassThru
    Start-Sleep -Seconds 8
    $alive = -not $p.HasExited
    $errors = Get-Content $script:LogFile -ErrorAction SilentlyContinue | Select-String '\[ERROR\]'
    Stop-Pilot
    "SMOKE alive=$alive errors=$($errors.Count)"
    $errors | ForEach-Object { $_.Line }
}
