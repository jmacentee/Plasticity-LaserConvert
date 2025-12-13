# StepProcess Architecture Documentation

## Module Organization

The refactored StepProcess implementation is organized into focused modules:

### Core Algorithm
- **`StepProcess.cs`** - Main entry point and algorithm orchestration (80 lines)
  - Loads STEP file
  - Filters solids by thin dimension
  - Generates SVG for each solid
  - Clean, readable flow of the 8-step algorithm

### Data Structures
- **`StepStructures.cs`** - Fundamental types and records
  - `Dimensions` record - Tracks X/Y/Z dimensions with thin dimension detection
  - `Vec2` struct - 2D vectors for screen/SVG coordinates
  - `Vec3` struct - 3D vectors for geometry with operator overloads
  - `Vec3Comparer` - Tolerance-based equality comparison

### Geometric Operations
- **`StepHelpers.cs`** - Math and transformations (280 lines)
  - **Matrix Operations:** `ApplyMatrix()`
  - **Vector Operations:** `Dot()`, `Cross()`, `Normalize()`
  - **3D Projections:** `BuildProjectionFrame()`, `Project()`
  - **2D Projection:** `ProjectTo2D()` - Selects 2 largest axes
  - **Normalization:** `NormalizeAndRound()`, `NormalizeAndRoundRelative()`
  - **Cleanup:** `RemoveConsecutiveDuplicates()`
  - **Utilities:** `GetEdgeLoopFromBound()`, `ComputeDimensions()`

### SVG Generation
- **`SVGHelpers.cs`** - SVG building and path generation (150 lines)
  - **`SvgBuilder` class:**
    - `BeginGroup()` / `EndGroup()` - Group management
    - `Path()` - Add path elements
    - `Build()` - Generate final SVG string
  - **`SvgPathBuilder` static class:**
    - `BuildPath()` - SVG path from ordered 2D points
    - `BuildAxisAlignedPathFromOrderedLoop()` - Snap to axes
    - `BuildPerimeterPath()` - Alternative path building

### Legacy & Reference Code
- **`StepUnusedHelpers.cs`** - Preserved algorithms from earlier attempts (400 lines)
  - Various ordering algorithms (Graham scan, Gift wrapping, orthogonal nearest-neighbor)
  - Alternative projection frames and approaches
  - Marked as NOT USED with explanations
  - Preserved for:
    - Understanding previous attempts
    - Potential future optimizations
    - Reference for algorithm research

## Key Design Decisions

### 1. Dynamic Axis Selection for 2D Projection
```csharp
// Instead of hard-coded X-Y projection:
var ranges = new[] { ("X", rangeX), ("Y", rangeY), ("Z", rangeZ) }
    .OrderByDescending(r => r.Item2)
    .ToArray();
// Use the two axes with largest ranges
```
**Why:** Handles rotation and orientation automatically. Works for:
- Axis-aligned shapes (largest axes are X and Y)
- Rotated shapes (largest axes may be any pair)
- Flat shapes (largest axes may be X-Z or Y-Z)

### 2. Boundary Preservation, Not Convex Hull
```csharp
// Remove only consecutive duplicates (rounding artifacts)
// DON'T apply Graham scan or Gift wrapping
var deduplicated = RemoveConsecutiveDuplicates(normalized);
```
**Why:** Extraction order from face bounds already follows edge connectivity. This preserves:
- Non-convex features (tabs, cutouts)
- Hole geometry
- Complex boundary details

### 3. One Algorithm for All Shapes
```csharp
// No special cases like:
// if (faces.Count > 20) { /* different code */ }
// or: if (isSimpleShape) { /* different code */ }

// Just one clean flow that works for all:
var bestFace = faces.WithMostBoundaryVertices();
var projected = ProjectTo2D(bestFace.Vertices);
var path = BuildPath(projected);
```
**Why:** Fewer bugs, easier maintenance, works uniformly for all complexity levels.

## Algorithm Walkthrough (8-Step Plan)

Steps 1-5 are handled by `StepTopologyResolver` (existing code):
1. Find shortest line segment between vertices on different faces
2. Calculate 3D rotation based on that segment's angle
3. Apply rotation to align thin dimension with Z axis
4. Select topmost face along Z axis
5. Apply rotation to align face edge with X axis

Steps 6-8 are in `StepProcess.Main()`:
6. **Project to 2D:** Use `StepHelpers.ProjectTo2D()` to dynamically select 2 largest axes
7. **Reconstruct perimeter:** Extract order from `ExtractFaceWithHoles()` preserves boundary
8. **Output to SVG:** Use `SvgBuilder` to create valid SVG with groups and paths

## File Size Comparison

| Component | Lines | Purpose |
|-----------|-------|---------|
| StepProcess.cs | 80 | Main algorithm flow |
| StepStructures.cs | 65 | Data types |
| StepHelpers.cs | 280 | Geometry & transforms |
| SVGHelpers.cs | 150 | SVG generation |
| StepUnusedHelpers.cs | 400 | Legacy reference code |
| **Total** | **975** | Well-organized, maintainable |

## Testing Coverage

All 10 test cases pass with correct output:

**Simple Shapes (4-vertex rectangles):**
- ? 1box.stp
- ? 2boxes.stp
- ? 3boxes.stp
- ? 3boxesB.stp
- ? 4boxes.stp

**Complex Shapes with Cutouts:**
- ? KBox.stp (12+ vertices)

**Shapes with Holes:**
- ? CBox.stp (4 vertices + hole)
- ? CBoxR.stp (4 vertices + hole, rotated)

**Complex Shapes with Holes:**
- ? KCBox.stp (33 vertices + 2 holes, rotated)
- ? KCBoxFlat.stp (32 vertices + 2 holes, flat)

## Extension Points

### Adding New Output Formats
```csharp
// In a new DxfOutput.cs:
using LaserConvert;
public static class DxfOutput
{
    public static string GenerateDxf(List<(long X, long Y)> outline, ...)
    {
        // Can reuse:
        var dims = StepHelpers.ComputeDimensions(vertices);
        var projected = StepHelpers.ProjectTo2D(vertices);
        // ...
    }
}
```

### Alternative Geometry Processing
```csharp
// In HelixProcess.cs or future IgesProcess.cs:
using LaserConvert;
// All helpers immediately available
```

### Unit Testing
```csharp
// Tests can import helper classes independently:
[TestClass]
public class StepHelpersTests
{
    [TestMethod]
    public void TestProjectTo2D_SelectsLargestAxes()
    {
        var points = new[] { (0.0, 0.0, 0.0), (100.0, 100.0, 3.0) };
        var result = StepHelpers.ProjectTo2D(points);
        // X and Y are selected, Z (only 3 units) is dropped
        Assert.IsTrue(result[0] == (0.0, 0.0));
    }
}
```

## Maintenance Guide

### Adding a Feature
1. Determine if it's geometry-related ? Add to `StepHelpers.cs`
2. Determine if it's SVG-related ? Add to `SVGHelpers.cs`
3. Determine if it's a new data type ? Add to `StepStructures.cs`
4. Wire it up in `StepProcess.Main()`

### Debugging
1. Check `StepProcess.cs` for algorithm flow issues
2. Check `StepHelpers.cs` for math/geometry issues
3. Check `SVGHelpers.cs` for output formatting issues
4. Check `StepStructures.cs` for data definition issues

### Performance Optimization
- `ProjectTo2D()` can be optimized without touching StepProcess
- `RemoveConsecutiveDuplicates()` can use faster deduplication without affecting output
- SVG generation can be parallelized or buffered
- All without touching main algorithm flow

## Summary

This refactored architecture provides:
- ? **Clarity** - Easy to understand each module's responsibility
- ? **Maintainability** - Changes isolated to appropriate files
- ? **Testability** - Each module can be tested independently
- ? **Extensibility** - Easy to add new formats or algorithms
- ? **Reusability** - Helpers available to all modules
- ? **Documentation** - Each file clearly explains its purpose

The 80-line `StepProcess.cs` is now the source of truth for the algorithm, while supporting modules handle specialized concerns.
