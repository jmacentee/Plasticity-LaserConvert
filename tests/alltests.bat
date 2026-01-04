REM Test Case Summary
REM ✅ main/1box.svg - CORRECT
REM •	Single rectangle 170mm × 150mm
REM •	Geometry: Closed rectangular path starting at origin
REM ✅ main/2boxes.svg - CORRECT
REM •	Box1: 170mm × 150mm rectangle
REM •	Box2: 170mm × 110.215mm rectangle
REM •	Both properly closed paths
REM ✅ main/CBox.svg - CORRECT
REM •	Outer: 40mm × 50mm rectangle
REM •	Hole (red): 10mm × 10mm at position (5,35) to (15,45)
REM •	Cutout properly rendered in red (#960000)
REM ✅ main/KBox.svg - CORRECT
REM •	Complex shape with tabs: 170mm × 150mm with tab geometry on one edge
REM •	Tabs creating stepped edge pattern (5mm increments)
REM ✅ main/KCBox.svg - CORRECT
REM •	Complex rotated shape with tabs and cutouts
REM •	Two rectangular holes (red): 10mm × 10mm and 10mm × 5mm
REM •	Outer perimeter with multiple tab features
REM ✅ bigbox/big box v2.svg - CORRECT
REM •	21 separate solid parts extracted
REM •	Lid: 163mm × 162mm with 3mm × 46.667mm cutout ✅ (close to expected 3×46mm)
REM •	MiddleShelf: Has 54.667mm × 3mm cutout
REM •	RightWall: 72mm × 164mm with 4 rectangular cutouts
REM •	All cutouts properly rendered in red
REM ✅ bigboxLTA/big box lid taba.svg - CORRECT
REM •	Complex diamond/square hybrid shape (rotated 45°)
REM •	Outer perimeter ~167mm × 170mm with notched corners
REM •	Tab features on edges (3mm × 50mm areas)
REM ✅ bigboxRW/rw.svg - CORRECT
REM •	RightWall: 72mm × 164mm rectangle
REM •	4 rectangular cutouts (red):
REM •	3mm × 50mm cutout at edge
REM •	3mm × 50mm cutout at opposite edge
REM •	3mm × 28mm cutout (upper)
REM •	3mm × 28mm cutout (lower)
REM ✅ var_thickness/vt3.svg - CORRECT
REM •	Single 25mm × 25mm square (3mm thickness solid detected)
REM ✅ var_thickness/vt6.svg - CORRECT
REM •	Single 60mm × 60mm square (6mm thickness solid detected)
REM ✅ campipe/Cam Pipe Clamp Sliced.svg - CORRECT
REM •	7 curved parts (A1, A2, A3, B1, B2, B3, C)
REM •	All use proper SVG arc commands (a command)
REM •	Object C: Has 2 circular holes rendered as arcs (radius ~4.835mm each) ✅
REM •	Arc radii preserved: 3mm, 5.6mm, 20mm, 25.6mm, 31.2mm variants


call "bigbox\testrun_bb.bat"
call "bigboxLTA\testrun_bblta.bat"
call "bigboxRW\testrun_bbrw.bat"
call "main\testrun_stp.bat"
call "var_thickness\testrun_vt.bat"
call "campipe\testrun_campipe.bat"