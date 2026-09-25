# Publication de Timonier : application autonome (win-x64, ReadyToRun), archive portable, installeur.
#   .\tools\publish.ps1                  version lue dans le .csproj
#   .\tools\publish.ps1 -Version 0.2.0   force la version
#   .\tools\publish.ps1 -NoInstaller     sans l'installeur (Inno Setup absent)
# Résultat dans artifacts\ : Timonier-Setup-<v>-x64.exe, Timonier-<v>-win-x64-portable.zip, SHA256SUMS.txt
param(
    [string]$Version = "",
    [switch]$NoInstaller
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\env.ps1"

$root = (Resolve-Path "$PSScriptRoot\..").Path
# Chemins de compilation au-delà de 260 caractères (dépôt dans un dossier profond) : MSBuild échoue sur certains
# fichiers. On compile alors à travers une jonction courte dans %TEMP% (aucun droit administrateur requis).
$junction = $null
if ($root.Length -gt 90) {
    $junction = Join-Path $env:TEMP "tmn-build-$([Math]::Abs($root.GetHashCode()))"
    if (-not (Test-Path $junction)) { New-Item -ItemType Junction -Path $junction -Target $root | Out-Null }
    $buildRoot = $junction
} else { $buildRoot = $root }
$proj = Join-Path $buildRoot "src\Timonier\Timonier.csproj"
$artifacts = Join-Path $root "artifacts"
$publishDir = Join-Path $artifacts "publish\win-x64"

if (-not $Version) { $Version = ([xml](Get-Content $proj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version invalide : '$Version' (attendu : X.Y.Z)" }
Write-Host "Timonier $Version"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force $artifacts | Out-Null

Write-Host "Publication (Release, win-x64, autonome, ReadyToRun)..."
dotnet publish $proj -c Release -r win-x64 --self-contained true -nologo -v q `
    -p:PublishReadyToRun=true -p:Version=$Version -p:DebugType=embedded -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué" }

$zip = Join-Path $artifacts "Timonier-$Version-win-x64-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Copy-Item (Join-Path $root "LICENSE") (Join-Path $publishDir "LICENSE.txt")
Compress-Archive -Path "$publishDir\*" -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Archive portable : $zip"

$outputs = @($zip)
if (-not $NoInstaller) {
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 introuvable (ou utilisez -NoInstaller)" }
    & $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$artifacts" (Join-Path $root "installer\Timonier.iss")
    if ($LASTEXITCODE -ne 0) { throw "ISCC a échoué" }
    $setup = Join-Path $artifacts "Timonier-Setup-$Version-x64.exe"
    Write-Host "Installeur : $setup"
    $outputs += $setup
}

$sums = Join-Path $artifacts "SHA256SUMS.txt"
$outputs | ForEach-Object { "$((Get-FileHash $_ -Algorithm SHA256).Hash.ToLower())  $(Split-Path $_ -Leaf)" } | Set-Content $sums -Encoding ascii
Get-Content $sums

# Supprime uniquement le lien (DirectoryInfo.Delete sans récursion ne touche pas au dossier cible).
if ($junction -and (Test-Path $junction)) { (Get-Item $junction).Delete() }
