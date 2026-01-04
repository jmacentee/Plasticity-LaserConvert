REM the correct way to run these tests is to execute this batch file from the command line.
REM the correct way to read the test results is to read the contents of bb_results.txt after the test run is complete.


"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\campipe\Cam Pipe Clamp Sliced.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\campipe\Cam Pipe Clamp Sliced.svg"  debugMode=true thickness=3 tolerance=2 > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\campipe\CamPipe_output.txt"





@echo off
setlocal enabledelayedexpansion

rem Hard-coded directory path
set INPUTDIR=C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\campipe
set OUTPUT=%INPUTDIR%\campipe_results.txt

rem Clear any existing output file
> "%OUTPUT%" echo Concatenated results

rem Loop through all .svg and .txt files, skipping the output file
for %%F in ("%INPUTDIR%\*.svg" "%INPUTDIR%\*.txt") do (
    if /I not "%%~nxF"=="campipe_results.txt" (
        echo. >> "%OUTPUT%"
        echo REM File: %%~nxF >> "%OUTPUT%"
        type "%%F" >> "%OUTPUT%"
    )
)

echo Done. Results written to %OUTPUT%