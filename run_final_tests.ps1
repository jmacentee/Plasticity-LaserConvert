#!/usr/bin/env powershell

$exePath = 'C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe'
$sampleDir = 'C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files'

$testCases = @(
    @{ input = '1box.stp'; output = '1box_final.svg'; name = 'Box1 (4 verts, simple)' },
    @{ input = 'CBox.stp'; output = 'CBox_final.svg'; name = 'CBox (4 verts + hole)' },
    @{ input = 'CBoxR.stp'; output = 'CBoxR_final.svg'; name = 'CBoxR (rotated + hole)' },
    @{ input = 'KBox.stp'; output = 'KBox_final.svg'; name = 'KBox (12 verts, tabs)' },
    @{ input = 'KCBox.stp'; output = 'KCBox_final.svg'; name = 'KCBox (33 verts, complex)' },
    @{ input = 'KCBoxFlat.stp'; output = 'KCBoxFlat_final.svg'; name = 'KCBoxFlat (32 verts, complex)' }
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "FINAL TEST RESULTS WITH IMPROVED ALGORITHM" -ForegroundColor Cyan  
Write-Host "========================================" -ForegroundColor Cyan

foreach ($test in $testCases) {
    $inputPath = Join-Path $sampleDir $test.input
    $outputPath = Join-Path $sampleDir $test.output
    
    Write-Host "`nTesting: $($test.name)" -ForegroundColor Yellow
    Write-Host "Input: $($test.input)" -ForegroundColor Gray
    
    $output = & $exePath $inputPath $outputPath 2>&1
    
    # Extract ordering method
    $orderLine = $output | Select-String "ORDER"
    if ($orderLine -match "edge-walking") {
        Write-Host "[?] Used edge-walking algorithm" -ForegroundColor Green
    } else {
        Write-Host "[?] Fell back to polar angle ordering" -ForegroundColor Yellow
    }
    
    # Extract SVG generation
    $svgLine = $output | Select-String "SVG.*Generated"
    if ($svgLine) {
        $match = $svgLine -match "(\d+) vertices"
        if ($match) {
            $vertCount = [int]$matches[1]
            Write-Host "[?] Generated SVG with $vertCount vertices" -ForegroundColor Green
        }
    }
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Test run complete!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
