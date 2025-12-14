### Warning
This does not work correctly yet.  It was close before I started adding "groups" support, but now it produces incorrect output.  I need to fix it.
This is an early prototype. 



# Plasticity-LaserConvert
LaserConvert is a .NET tool that reads STP CAD files, detects the thin (≈ 3 mm) extrusion axis, rotates each solid into its footprint plane, and outputs clean 2D SVG outlines for laser cutting.

---

### Installation and Use
Clone this repository
Install .NET 10 SDK (if not already installed)
Build the project using `dotnet build` (or use Visual Studio)
In Plasticity, build your model out of solids all approximately 3 mm thick, then export as IGES.
Run the tool with `LaserConvert.exe <input.iges> <output.svg>`

