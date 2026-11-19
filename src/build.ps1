$ErrorActionPreference = 'Stop'
$petRoot = Split-Path -Parent $PSScriptRoot
dotnet publish "$PSScriptRoot\NuonotaPet.csproj" -c Release -r win-x64 --self-contained true -o "$petRoot\app" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Start-Process -FilePath "$petRoot\app\NuonotaPet.exe" -ArgumentList '--self-test' -Wait -WindowStyle Hidden
Get-Content "$petRoot\data\self-test.txt"
