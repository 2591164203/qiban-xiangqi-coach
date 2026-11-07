param([switch]$Integration,[switch]$RenderUi)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$env:DOTNET_CLI_HOME=Join-Path $taskRoot '.tools/dotnet-home'
$env:NUGET_PACKAGES=Join-Path $taskRoot '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$sdk=Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $sdk)) { $sdk=(Get-Command dotnet -ErrorAction Stop).Source }
$testArguments=@('run','--project','tests/ChessCoach.Tests','-c','Release','--')
if($RenderUi){$testArguments+=@('--render-ui',(Join-Path $taskRoot 'docs/ui-v0.5'))}elseif($Integration){$testArguments+='--integration'}
& $sdk @testArguments
if($LASTEXITCODE -ne 0){throw 'Tests failed'}
