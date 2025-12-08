# Plasticity-LaserConvert
LaserConvert is a .NET tool that reads IGES CAD files, detects the thin (≈ 3 mm) extrusion axis, rotates each solid into its footprint plane, and outputs clean 2D SVG outlines for laser cutting. Built on IxMilia.Iges (MIT licensed). Vibe Coded with MS Copilot.

---

## ✨ Features
- Parse IGES files using [IxMilia.Iges](https://github.com/ixmilia/iges) (MIT licensed).
- Automatically detect sheet thickness (~3 mm) via PCA analysis.
- Rotate solids into their footprint plane for consistent XY projection.
- Output precise 2D SVG outlines (lines and arcs preserved).
- Designed for workflows with Glowforge and other laser cutters.

### Installation and Use
Clone this repository
Install .NET 10 SDK (if not already installed)
Build the project using `dotnet build` (or use Visual Studio)
In Plasticity, build your model out of solids all approximately 3 mm thick, then export as IGES.
Run the tool with `LaserConvert.exe <input.iges> <output.svg>`

### Warning
This is an early prototype. 

### Contributing
Pull requests are welcome! If you’d like to add features or improve the code, please fork the repo and submit a PR.

## 🛠️ Development Journey

LaserConvert was developed iteratively to solve a specific problem: converting 3D CAD models into accurate 2D outlines for laser cutting. The journey unfolded as follows:

1. **Initial Approach**  
   - We began by attempting to directly flatten IGES geometry onto the XY plane.  
   - This produced incorrect results because it ignored the thin (~3 mm) extrusion axis of solids.  
   - The first parser treated IGES files like CSV, which failed since IGES uses fixed‑width DE/PD sections.  
   - Outcome: no usable geometry was detected.

2. **Pivot to IxMilia.Iges**  
   - To avoid re‑implementing the IGES spec, we adopted the open‑source [IxMilia.Iges](https://github.com/ixmilia/iges) library.  
   - This provided robust parsing of IGES entities such as `IgesLine` and `IgesCircularArc`.  
   - With this, we could focus on geometry logic instead of file parsing.

3. **Entity Handling**  
   - Lines were read via `Line.P1` and `Line.P2`.  
   - Arcs were read via `Arc.Center`, `Arc.StartPoint`, and `Arc.EndPoint`.  
   - For arcs, radius and angles are derived after projection into 2D.

4. **PCA and Thickness Detection**  
   - We integrated Principal Component Analysis (PCA) to find the three orthogonal axes of each solid.  
   - The axis with ~3 mm extent was identified as the thickness axis.  
   - Geometry was rotated so the footprint plane aligned with XY, collapsing the thickness dimension.

5. **Projection and SVG Output**  
   - All points were projected into the footprint plane.  
   - Lines became `<line>` elements in SVG.  
   - Arcs became `<path>` elements with computed radius and sweep flags.  
   - The result: clean, accurate 2D outlines suitable for Glowforge and other laser cutters.

6. **Repo Integration**  
   - IxMilia.Iges was included via Git submodule (with sparse‑checkout to keep only the needed folder).  
   - Attribution was preserved via LICENSE and README.  
   - The namespace was set to `LaserConvert` for clarity.

---

**Summary:**  
We started with a naive flattening approach, discovered IGES parsing complexity, pivoted to IxMilia.Iges for robust entity handling, added PCA‑based thickness detection, and now have a working tool that produces accurate SVG outlines for fabrication.