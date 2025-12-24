# Plasticity-LaserConvert
LaserConvert is a .NET tool that reads STP CAD files, detects the thin (≈ 3 mm) extrusion axis, rotates each solid into its footprint plane, and outputs clean 2D SVG outlines for laser cutting.

---

### Installation and Use
Clone this repository
Install .NET 10 SDK (if not already installed)
Build the project using `dotnet build` (or use Visual Studio)
In Plasticity, build your model out of solids all approximately 3 mm thick, then export as STEP.
Run the tool with `LaserConvert.exe <input.stp> <output.svg>`

### TO-DO
- [ ] Add command-line options for customizing output (e.g., material thickness)
- [ ] convert from a CLI to a .NET library and then add a Blazor wrapper so it can be used by anyone in a static website


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