REM all 3 STP files have been validated as correct with the geometry described below. never assume there is a problem in an STP file and that is why test is failing.
REM when successful, the groups in the SVG files will have the ids which match the solids in the STP file (Box1, Box2, etc)
REM the box ordering in the SVG file is not important
REM the solids are allowed to be placed and rotated anywhere in 3d space in the step file.  Our job is to find the faces which are perpendicular to the 3mm wide face and then make a 2d projection of that face into the svg file.  the outer edge of the solids may not be regular and there may be cutouts in the solids which should be included in the svg output as indepdent red paths.

REM 1.	discover the shortest line segment between to verticies on different faces
REM 2.	discover the 3d rotation of the solid based on the angle between those two vertex
REM 3.	apply a transform to the entire 3d solid to rotate it in memory so that the short segment is now along the z axis
REM 4.	pick the topmost face along the z axis
REM 5.  apply a transform to the entire 3d solid to rotate it so that 1 edge is aligned with the x axis
REM 6.	use the newly rotated x/y vertexes of that topmost face as the coordinates of the path in the svg file.

REM Normalize the projection so the rectangles appear axis-aligned in the SVG.  
REM start each object at 0,0 in the svg so we can read easily it's width and height in the SVG.  
REM round the coordinate output in the SGV to the nearest whole number.



REM This file contains a single object named Box1 which is 170x150x3mm and should result in a single square in the SVG of 170x150mm.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\1box.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\1box.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\1box_output.txt"

REM This file contains both the previous Box1 and a second box named Box2 which is 110.215x170x3mm and should result in both the 1st square a second square in the SVG of 110x170mm.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\2boxes.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\2boxes.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\2boxes_output.txt"

REM This file contains the previous two boxes as well as a third box named Box3 which is 167x170x150mm which should be filtered out as too large. The output should be identical to the previous test.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxes.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxes.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxes_output.txt"

REM This file contains the previous Box1 and Box2 as well as a third box named Box4 which is 67.488x74.819x3mm. The output should be the same as 2boxes.svg with the addition of Box4 which is 64x74mm.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxesB.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxesB.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxesB_output.txt"

REM This file contains the previous Box1 and Box2 and Box4 fourth box named Box5 which is 44.445x57.582x3mm. The output should be the same as 3boxesB.svg with the addition of Box5 which is 44x58mm.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\4boxes.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\4boxes.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\4boxes_output.txt"

REM This file contains a single solid named KBox.  The Overall dimension is 150x170x3mm.  There are tabs cut out of the topmost face which should appear as cutouts in the SVG output. Two tabs are 5mm deep, one tab is 10mm deep.
REM one 5mm cutout is on the edge and is 14.656 wide
REM the other two cutouts are both 34mm wide.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KBox.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KBox.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KBox_output.txt"

REM This file contains a single solid named CBox. The Overall dimension is 40x50x3mm. It should be represented in the SVG as a single black square 40x50mm.
REM There is a hole in CBox.  It is positioned 5mm over and 5mm down from one corner. It is 10x10mm wide.
REM The hole should be represented in the SVG as a new path in the same group as CBox which is red.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBox.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBox.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBox_output.txt"


REM This file is the same as CBox.stp but repositioned rotated in 3d space.  The output SVG should be identical to the previous test.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBoxR.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBoxR.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBoxR_output.txt"



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