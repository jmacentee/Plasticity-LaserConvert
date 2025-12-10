REM all 3 STP files have been validated as correct with the geometry described below. never assume there is a problem in an STP file and that is why test is failing.
REM when successful, the groups in the SVG files will have the ids Box1 (in all cases) and Box2 (in the 2nd and 3rd case)
REM the box ordering in the SVG file is not important

REM This file contains a single object named Box1 which is 170x150x3mm and should result in a single square in the SVG of 170x150mm.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\1box.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\1box.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\1box_output.txt"

REM This file contains both the previous Box1 and a second box named Box2 which is 110.215x170x3mm and should result in both the 1st square a second square in the SVG of 110x170mm.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\2boxes.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\2boxes.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\2boxes_output.txt"

REM This file contains the previous two boxes as well as a third box named Box3 which is 167x170x150mm which should be filtered out as too large. The output should be identical to the previous test.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxes.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxes.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxes_output.txt"


@echo off
setlocal enabledelayedexpansion

rem Hard-coded directory path
set INPUTDIR=C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files
set OUTPUT=%INPUTDIR%\all_results.txt

rem Clear any existing output file
> "%OUTPUT%" echo Concatenated results

rem Loop through all .svg and .txt files, skipping the output file
for %%F in ("%INPUTDIR%\*.svg" "%INPUTDIR%\*.txt") do (
    if /I not "%%~nxF"=="all_results.txt" (
        echo. >> "%OUTPUT%"
        echo REM File: %%~nxF >> "%OUTPUT%"
        type "%%F" >> "%OUTPUT%"
    )
)

echo Done. Results written to %OUTPUT%