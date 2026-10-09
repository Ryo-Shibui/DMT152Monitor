$ErrorActionPreference = "Stop"
dotnet build ".\DMT152Monitor.sln" -c Release -p:Platform=x64
Write-Host "Release build completed."
