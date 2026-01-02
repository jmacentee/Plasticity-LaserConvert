# Plasticity-LaserConvert
LaserConvert is a .NET tool that reads STP CAD files, detects thin extrusion solids (configurable thickness), rotates each solid into its footprint plane, and outputs clean 2D SVG outlines for laser cutting.

## 🌐 Web App

**Try it online:** [https://jmacentee.github.io/Plasticity-LaserConvert/](https://jmacentee.github.io/Plasticity-LaserConvert/)

The web app runs entirely in your browser using WebAssembly - no files are uploaded to any server!

---

## Command-Line Tool

### Installation and Use
1. Clone this repository
2. Install .NET 10 SDK (if not already installed)
3. Build the project using `dotnet build` (or use Visual Studio)
4. In Plasticity, build your model out of thin solids, then export as STEP.
5. Run the tool with `LaserConvert.exe <input.stp> <output.svg> [options]`

### Command-Line Options

| Option | Description | Default |
|--------|-------------|---------|
| `thickness=<mm>` | Target material thickness in millimeters. Only solids matching this thickness (within tolerance) will be processed. | 3.0 |
| `tolerance=<mm>` | Thickness matching tolerance in millimeters. Solids with thickness within `thickness ± tolerance` will be included. | 0.5 |
| `debugMode=true\|false` | Enable verbose debug output for troubleshooting. | false |

### Examples

```bash
# Process 3mm thick solids (default)
LaserConvert.exe input.stp output.svg

# Process 6mm thick solids with debug output
LaserConvert.exe input.stp output.svg thickness=6 debugMode=true

# Process 3mm thick solids with tighter tolerance (±0.25mm)
LaserConvert.exe input.stp output.svg thickness=3 tolerance=0.25

# Process 9mm thick solids
LaserConvert.exe input.stp output.svg thickness=9
```

### Processing Multiple Thicknesses

If your STEP file contains solids of different thicknesses, run the tool multiple times with different `thickness` parameters to generate separate SVG files for each material:

```bash
LaserConvert.exe model.stp 3mm_parts.svg thickness=3
LaserConvert.exe model.stp 6mm_parts.svg thickness=6
LaserConvert.exe model.stp 9mm_parts.svg thickness=9
```

---

## Screenshots

### Click for larger images
<p align="center">
  <a href="images/boxoverview.png" target="_blank">
    <img src="images/boxoverview_thumb.png" alt="boxoverview" width="342">
  </a>
</p>
<p align="center">
  <a href="images/boxopen.png" target="_blank">
    <img src="images/boxopen_thumb.png" alt="boxopen" width="412">
  </a>
</p>
<p align="center">
  <a href="images/stepexport.png" target="_blank">
    <img src="images/stepexport.png" alt="stepexport" width="301">
  </a>
</p>
<p align="center">
  <a href="images/svg.png" target="_blank">
    <img src="images/svg_thumb.png" alt="svg" width="564">
  </a>
  <br>(Layout and some decoration lines added in Inkscape)
</p>
<p align="center">
  <a href="images/big_box_photo_closed.jpg" target="_blank">
    <img src="images/big_box_photo_closed_thumb.jpg" alt="big_box_photo_closed" width="408">
  </a>
</p>
<p align="center">
  <a href="images/big_box_photo_open.jpg" target="_blank">
    <img src="images/big_box_photo_open_thumb.jpg" alt="big_box_photo_open" width="564">
  </a>
</p>