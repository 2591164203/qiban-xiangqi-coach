$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$locked=Get-Content -LiteralPath 'scripts/dependencies.lock.json' -Raw | ConvertFrom-Json
$proxyArguments=@{}
$proxySettings=Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
if($proxySettings.ProxyEnable -eq 1 -and $proxySettings.ProxyServer -match '^([^;=]+:\d+)$'){
    $proxyArguments.Proxy='http://'+$Matches[1]
    $env:HTTPS_PROXY=$proxyArguments.Proxy
    $env:HTTP_PROXY=$proxyArguments.Proxy
}
$env:DOTNET_CLI_HOME=Join-Path $taskRoot '.tools/dotnet-home'
$env:NUGET_PACKAGES=Join-Path $taskRoot '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
New-Item -ItemType Directory -Force -Path '.tools','downloads','assets/models','assets/engine','assets/fixtures','licenses' | Out-Null
function Receive-CheckedFile($Url,$Destination,$Hash,$Algorithm='SHA256'){
    if(!(Test-Path -LiteralPath $Destination)) { Invoke-WebRequest -Uri $Url -OutFile $Destination @proxyArguments }
    if((Get-FileHash -LiteralPath $Destination -Algorithm $Algorithm).Hash -ne $Hash){throw "Checksum mismatch: $Destination. Remove only that file and retry."}
}
if(!(Test-Path -LiteralPath '.tools/dotnet/dotnet.exe')){
    Receive-CheckedFile $locked.sdk.url 'downloads/dotnet-sdk.zip' $locked.sdk.sha512 'SHA512'
    Expand-Archive -LiteralPath 'downloads/dotnet-sdk.zip' -DestinationPath '.tools/dotnet' -Force
}
if(!(Test-Path -LiteralPath 'vendor/VinXiangQi/.git')){
    $gitArgs=@()
    if($proxyArguments.Proxy){$gitArgs+=@('-c',"http.proxy=$($proxyArguments.Proxy)")}
    & git @gitArgs clone --depth 1 --branch v1.4.0 https://github.com/Vincentzyx/VinXiangQi.git vendor/VinXiangQi
    if($LASTEXITCODE -ne 0){throw 'VinXiangQi clone failed'}
}
$actualCommit=& git -C vendor/VinXiangQi rev-parse HEAD
if($actualCommit -ne $locked.vinCommit){throw 'VinXiangQi source commit differs from locked version'}
Receive-CheckedFile $locked.vin.url 'downloads/VinXiangQi.v1.4.0.zip' $locked.vin.sha256
Receive-CheckedFile $locked.pikafish.url 'downloads/Pikafish.2026-09-06.7z' $locked.pikafish.sha256
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$stream=[IO.File]::OpenRead((Join-Path $taskRoot 'downloads/VinXiangQi.v1.4.0.zip'))
$archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Read,$false,[Text.Encoding]::GetEncoding(936))
try{
    foreach($entry in $archive.Entries | Where-Object {$_.FullName.EndsWith('.onnx')}){
        $rotationWord=-join [char[]](0x65CB,0x8F6C)
        $destination=if($entry.Length -lt 10000000){'assets/models/vin-fast.onnx'}elseif($entry.FullName.Contains($rotationWord)){'assets/models/vin-rotated.onnx'}else{'assets/models/vin-accurate.onnx'}
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $taskRoot $destination),$true)
    }
}finally{$archive.Dispose()}
& tar -xf downloads/Pikafish.2026-09-06.7z -C assets/engine Pikafish-Windows-x86-64-universal.exe pikafish.nnue Copying.txt NNUE-License.md README.md
if($LASTEXITCODE -ne 0){throw 'Pikafish extraction failed'}
Copy-Item -LiteralPath 'vendor/VinXiangQi/assets/image-20220711145437807.png' -Destination 'assets/fixtures/vin-screen.png' -Force
Copy-Item -LiteralPath 'vendor/VinXiangQi/LICENSE' -Destination 'licenses/VinXiangQi-GPL-3.0.txt' -Force
Copy-Item -LiteralPath 'assets/engine/Copying.txt' -Destination 'licenses/Pikafish-GPL-3.0.txt' -Force
Copy-Item -LiteralPath 'assets/engine/NNUE-License.md' -Destination 'licenses/Pikafish-NNUE-License.md' -Force
& '.tools/dotnet/dotnet.exe' restore src/ChessCoach/ChessCoach.csproj --packages '.tools/nuget' --verbosity minimal
if($LASTEXITCODE -ne 0){throw 'NuGet restore failed'}
Copy-Item -LiteralPath '.tools/nuget/microsoft.ml.onnxruntime/1.20.1/LICENSE' -Destination 'licenses/ONNX-Runtime-MIT.txt' -Force
Copy-Item -LiteralPath '.tools/nuget/microsoft.ml.onnxruntime/1.20.1/ThirdPartyNotices.txt' -Destination 'licenses/ONNX-Runtime-ThirdPartyNotices.txt' -Force
Copy-Item -LiteralPath '.tools/dotnet/LICENSE.txt' -Destination 'licenses/DotNet-LICENSE.txt' -Force
Copy-Item -LiteralPath '.tools/dotnet/ThirdPartyNotices.txt' -Destination 'licenses/DotNet-ThirdPartyNotices.txt' -Force
Write-Output 'Dependencies prepared. Run scripts/build.ps1 -Publish.'
