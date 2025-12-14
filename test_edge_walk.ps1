#!/usr/bin/env pwsh
cd "C:\Users\jdm\source\repos\Plasticity-LaserConvert"

Write-Host "Building project..." -ForegroundColor Cyan
dotnet build LaserConvert -c Release | Out-Null

Write-Host "Testing 1box.stp..." -ForegroundColor Cyan
$output = dotnet run --project LaserConvert -- "sample files/1box.stp" "sample files/1box_edgewalk.svg" 2>&1
Write-Host $output

Write-Host "`nTesting KCBoxFlat.stp (complex shape)..." -ForegroundColor Yellow
$output = dotnet run --project LaserConvert -- "sample files/KCBoxFlat.stp" "sample files/KCBoxFlat_edgewalk.svg" 2>&1
$lines = $output -split "`n"
$lines | Select-Object -First 100 | ForEach-Object { Write-Host $_ }
