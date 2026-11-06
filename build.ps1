$ErrorActionPreference = 'Stop'

dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist

Write-Host "Готово: $PSScriptRoot\dist\OpenCodeWidget.exe"
