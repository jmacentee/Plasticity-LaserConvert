REM the correct way to run these tests is to execute this batch file from the command line.
REM the correct way to read the test results is to read the contents of bb_results.txt after the test run is complete.

REM 1.	discover the shortest line segment between to verticies on different faces
REM 2.	discover the 3d rotation of the solid based on the angle between those two vertex
REM 3.	apply a transform to the entire 3d solid to rotate it in memory so that the short segment is now along the z axis
REM 4.	pick the topmost face along the z axis
REM 5.  apply a transform to the entire 3d solid to rotate it so that the 3mm edge is aligned with the z axis and 1 edge is aligned with the x axis. This rotation should include all the holes in the solid at the same time, so they can be properly made into SVG paths in the next step.
REM 6. Project to 2D (X, Y only after rotation/normalization)
REM 7. RECONSTRUCT PERIMETER ORDER IN 2D using computational geometry
REM 8. Output to SVG

REM Normalize the projection so the rectangles appear axis-aligned in the SVG.  
REM start each object at 0,0 in the svg so we can read easily it's width and height in the SVG.  
REM round the coordinate output in the SGV to the nearest whole number.

REM not "projecting" a solid onto a plane, you are rotating, perhaps multiple times, and moving to put the large face of the solid onto the plane.  Then you are describing the outside edge of the face and any holes in it.

REM there should be no "fallback bounding box cases".  Either read the geometry correctly or skip the solid entirely.

REM Swapped X-Y dimensions in the SVG are not a problem, as long as the entire solid and it's cutuouts are rotated together.
REM shapes being mirror images are not important, since the purpose of this is to laser cut the shape it will be the same on the back and front no matter what.
REM The holes in the solids in a STP file are clearly part of the standard. When I load KCBox.stp into https://3dviewer.net/ I see the solid with two holes in it, the same as I saw in Plasticity.  If any tests fail, try to look for the root problem and the general solution instead of any hacks.

REM we shouldn't have any special cases, we should just handle everything generally.  why do we have any "thresholds" at all?  We need to understand the real geometory generaly (what is an outside edge, what is the edge of a hole) and deal with it the same way from our most simple example to our most complex

REM TEST CASES START HERE

REM This file contains 3 objects
REM "Three" is 3x25x25mm
REM "Six" is 6x60x60mm
REM "Nine" is 9x95x95mm
REM Using the new "thickness" parameter in the command lines below to specify the thickness of each object, each of the
REM 3 output SVG files should only contain one of the three objects.  The other two objects should be ignored because they don't match the specified thickness.
REM the first case does not have the "thickness" parameter, so it should default to 3mm and only output the "Three" object.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\var_thickness\var_thickness_test.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\var_thickness\vt3.svg"  debugMode=true > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\var_thickness\vt3_output.txt"
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\var_thickness\var_thickness_test.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\var_thickness\vt6.svg"  debugMode=true thickness=6 > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\var_thickness\vt6_output.txt"
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\var_thickness\var_thickness_test.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\var_thickness\vt9.svg"  debugMode=true thickness=9 > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\var_thickness\vt9_output.txt"





@echo off
setlocal enabledelayedexpansion

rem Hard-coded directory path
set INPUTDIR=C:\Users\jdm\source\repos\Plasticity-LaserConvert\tests\var_thickness
set OUTPUT=%INPUTDIR%\vt_results.txt

rem Clear any existing output file
> "%OUTPUT%" echo Concatenated results

rem Loop through all .svg and .txt files, skipping the output file
for %%F in ("%INPUTDIR%\*.svg" "%INPUTDIR%\*.txt") do (
    if /I not "%%~nxF"=="vt_results.txt" (
        echo. >> "%OUTPUT%"
        echo REM File: %%~nxF >> "%OUTPUT%"
        type "%%F" >> "%OUTPUT%"
    )
)

echo Done. Results written to %OUTPUT%