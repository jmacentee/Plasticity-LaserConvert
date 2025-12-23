REM the correct way to run these tests is to execute this batch file from the command line.
REM the correct way to read the test results is to read the contents of all_results.txt after the test run is complete.


REM in our test cases below, all STP files have been validated as correct with the geometry described below. never assume there is a problem in an STP file and that is why test is failing.
REM when successful, the groups in the SVG files will have the ids which match the solids in the STP file (Box1, Box2, etc)
REM the box ordering in the SVG file is not important
REM the solids are allowed to be placed and rotated anywhere in 3d space in the step file.  Our job is to find the faces which are perpendicular to the 3mm wide face and then make a 2d projection of that face into the svg file.  the outer edge of the solids may not be regular and there may be cutouts in the solids which should be included in the svg output as indepdent red (#960000) paths.

REM Every time you try something and it doesn't work, you should leave a comment on what you tried, why you tried it and what the result was to prevent future duplicate effort.
REM Do not write any custom code when code from a 3rd party library can be used instead.

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
REM In these tests, there should not be any diagonal lines. (This is not a general rule, only true for these examples).  
REM The holes in the solids in a STP file are clearly part of the standard. When I load KCBox.stp into https://3dviewer.net/ I see the solid with two holes in it, the same as I saw in Plasticity.  If any tests fail, try to look for the root problem and the general solution instead of any hacks.

REM we shouldn't have any special cases, we should just handle everything generally.  why do we have any "thresholds" at all?  We need to understand the real geometory generaly (what is an outside edge, what is the edge of a hole) and deal with it the same way from our most simple example to our most complex

REM Preserve all boundary vertices for complex shapes instead of deduplicating them, which was collapsing the geometry to a simplified convex hull approximation.

REM TEST CASES START HERE

REM This file contains a single object named Box1 which is 170x150x3mm and should result in a single square in the SVG of 170x150mm.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\1box.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\1box.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\1box_output.txt"

REM This file contains both the previous Box1 and a second box named Box2 which is 110.215x170x3mm and should result in both the 1st square a second square in the SVG of 110x170mm.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\2boxes.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\2boxes.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\2boxes_output.txt"

REM This file contains the previous two boxes as well as a third box named Box3 which is 167x170x150mm which should be filtered out as too large. The output should be identical to the previous test.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxes.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxes.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxes_output.txt"

REM This file contains the previous Box1 and Box2 as well as a third box named Box4 which is 67.488x74.819x3mm. The output should be the same as 2boxes.svg with the addition of Box4 which is 67x75mm.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxesB.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxesB.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\3boxesB_output.txt"

REM This file contains the previous Box1 and Box2 and Box4 fourth box named Box5 which is 44.445x57.582x3mm. The output should be the same as 3boxesB.svg with the addition of Box5 which is 44x58mm.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\4boxes.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\4boxes.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\4boxes_output.txt"

REM This file contains a single solid named KBox.  The Overall dimension is 150x170x3mm.  There are tabs cut out of the topmost face which should appear as cutouts in the SVG output. Two tabs are 5mm deep, one tab is 10mm deep.
REM one 5mm cutout is on the edge and is 14.656 wide
REM the other two cutouts are both 34mm wide.
REM since these cutouts are on the outside edge of the face, they should not be red (#960000).  The dark purple (#9600c8) outline of the face is more complex than a rectangle.
REM the correct svg path for KBox is d="m 0,0 h 170 v 145 h -15 v 5 H 121 V 140 H 87 v 5 H 53 v 5 H 0 Z"
REM this path should have no diagonal lines.
REM it is not necessary to copy the format of the correct path with "h" and "v".  Only to have the same points in the same order.
REM there may be some slight rounding errors in the correct path. it is acceptable if our output is the same within +/- 1 unit.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KBox.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KBox.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KBox_output.txt"

REM This file contains a single solid named CBox. The Overall dimension is 40x50x3mm. It should be represented in the SVG as a single dark purple (#9600c8) square 40x50mm.
REM There is a hole in CBox.  It is positioned 5mm over and 5mm down from one corner. It is 10x10mm wide.
REM The hole should be represented in the SVG as a new path in the same group as CBox which is red (#960000).
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBox.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBox.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBox_output.txt"


REM This file is the same as CBox.stp but repositioned rotated in 3d space.  The output SVG should be identical to the previous test.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBoxR.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBoxR.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\CBoxR_output.txt"


REM This file contains a solid which is rotated in 3d space, has cutouts and tabs along the outer edge, and has two holes cut into it.
REM The approximate correct output for this file can be found in KCBox_corrected.svg.  This KCBox_corrected.svg sample file may have rounding errors which do not need to be duplicated exactly.
REM the overall width is 40mm.  the overall height is 58mm including the tab at the bottom and the tall tab at the top.  the left side is only 50mm without those tabs.  the top hole is 10x10 and is 5mm from the top and left sides (not counting the tabs).
REM We have discussed before -- if you think that the holes are in the other faces, not the main face, that means we have selected the mainface incorrectly.
REM The outer edge of KCBox has 32 vertexes.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KCBox.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KCBox.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KCBox_output.txt"


REM KCBoxFlat is the result of importing KCBox_corrected.svg into Plasticity, extruding it exactly 3mm tall and then exporting as a step file. 
REM when al tests pass, the output from this test should be identical to KCBox.svg except for the different group name and possiblty it's rotation in the file
REM There may be slight rounding errors in the scaling of KCBoxFlat.  The bottom edge is 40.084mm total.
REM the left side is 56.087mm total.
REM the big hole near the top left is 10.189mm x 10.165mm
REM the small hole near the bottom right is 10.132mm x 5.175 mm
REM KCBoxFlat is approximatly the same solid as KCBox except it is not rotated in 3d space.  It sits flat on the x-y plane. It's upper left corner is at 0,0.
"C:\Users\jdm\source\repos\Plasticity-LaserConvert\LaserConvert\bin\Debug\net10.0\LaserConvert.exe" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KCBoxFlat.stp" "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KCBoxFlat.svg" > "C:\Users\jdm\source\repos\Plasticity-LaserConvert\sample files\KCBoxFlat_output.txt"




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