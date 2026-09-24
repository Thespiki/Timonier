# Environnement de build (sans droits admin) - a dot-sourcer : . .\tools\env.ps1
# Utilise le SDK .NET et MinGit installes pour l'utilisateur s'ils existent, sinon ceux du systeme (CI).
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$userDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet"
if (Test-Path "$userDotnet\dotnet.exe") {
    $env:DOTNET_ROOT = $userDotnet
    $env:PATH = "$userDotnet;$env:PATH"
}
$userGit = "$env:LOCALAPPDATA\Programs\MinGit\cmd"
if (Test-Path "$userGit\git.exe") { $env:PATH = "$userGit;$env:PATH" }
