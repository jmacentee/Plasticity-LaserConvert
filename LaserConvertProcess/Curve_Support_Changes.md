# LaserConvertProcess Changes - SVG Curve Support

## Summary

Refactored the SVG output to use proper SVG curve commands (arcs) instead of polylines for circular edges. This produces cleaner, more accurate SVG output that maintains precision at any zoom level.

## Changes Made

### 1. `CurveSegment.cs` (New File)

A new model for representing curve segments that preserve geometric information:

- **`CurveSegment`** - Abstract base class for 3D curve segments
  - `LineSegment` - Straight line between two points
  - `ArcSegment` - Circular arc with center, radius, normal, and angle information

- **`CurveSegment2D`** - Abstract base class for 2D curve segments (after projection)
  - `LineSegment2D` - 2D straight line
  - `ArcSegment2D` - 2D circular arc with SVG-compatible parameters

Each segment type can:
- Project from 3D to 2D using a coordinate frame
- Rotate around a center point
- Translate by an offset
- Generate SVG path commands

### 2. `CurveEvaluator.cs` Updates

Added `ExtractCurveSegment` method that:
- Detects if an edge is a circle (arc) and extracts full arc parameters
- Computes arc center, radius, start/end angles from STEP circle data
- Returns proper `ArcSegment` for circles, `LineSegment` for straight edges

### 3. `StepGeometryExtractor.cs` Updates

Added new extraction methods:
- `ExtractCurveSegmentsInEdgeOrder` - Extracts curve segments from a face bound
- `ExtractFaceWithHolesAsSegments` - Extracts outer and hole segments as curves

### 4. `SVGHelpers.cs` Updates

Added `BuildPathFromSegments` method that:
- Takes a list of `CurveSegment2D` objects
- Generates SVG path with proper arc (`a`) and line (`l`) commands
- Uses relative coordinates for cleaner output

### 5. `StepProcess.cs` Updates

`ProcessSolid` now:
- Extracts curve segments instead of sampled points
- Projects segments to 2D preserving arc geometry
- Outputs SVG paths with arc commands

## SVG Arc Command Format

The output uses SVG elliptical arc commands:
```
a rx,ry x-axis-rotation large-arc-flag sweep-flag dx,dy
```

For example:
```svg
<path d="M 0.000,0.000 a 25.721,25.721 0 0 0 51.443,0.000 l 0.000,7.476 a 25.721,25.721 0 1 1 -51.443,0.000" />
```

This represents:
- Move to (0,0)
- Arc with radius 25.721, ending at relative (51.443, 0)
- Line to relative (0, 7.476)
- Arc with radius 25.721, large-arc flag 1, ending at relative (-51.443, 0)

## Benefits

1. **Precision** - Curves maintain mathematical precision at any zoom level
2. **Smaller files** - Arc commands are more compact than polylines
3. **Better compatibility** - Standard SVG arc format works with all SVG tools
4. **Cleaner output** - Matches the format expected by professional CAD/laser software

## Testing

After these changes:
- Circular arcs in STEP files output as SVG arc commands
- Straight edges output as line commands
- All existing rectangular test cases continue to work
- The Cam Pipe Clamp test file now produces proper arc output

## Known Limitations

- B-spline curves still use line segments (conversion to cubic Bezier is complex)
- Ellipse arcs are treated as lines (proper ellipse arc handling would need additional work)
