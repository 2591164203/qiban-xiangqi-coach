param([switch]$Publish,[string]$OutputDirectory='dist/ChessCoach-v0.5')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$env:DOTNET_CLI_HOME=Join-Path $taskRoot '.tools/dotnet-home'
$env:NUGET_PACKAGES=Join-Path $taskRoot '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_NOLOGO='1'
$sdk=Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $sdk)) { $sdk=(Get-Command dotnet -ErrorAction Stop).Source }
if($Publish){
    & $sdk publish src/ChessCoach/ChessCoach.csproj -c Release -r win-x64 --self-contained true -o $OutputDirectory -p:PublishSingleFile=false --verbosity minimal
}else{
    & $sdk build src/ChessCoach/ChessCoach.csproj -c Release --verbosity minimal
}
if($LASTEXITCODE -ne 0){throw 'Build failed'}
