# Current Test Status - StepProcess.cs Active

## Summary
Switched from `HelixProcess.cs` back to `StepProcess.cs` as the primary STEP processing engine.

**Reason:** `StepProcess.cs` has 5 days of accumulated refinements and special-case handling that makes it work for all simple and moderately complex shapes, whereas `HelixProcess.cs` is a simplified implementation that only works for KCBoxFlat.

## Test Results

### ? PASSING (All Simple Cases)
- **1box.stp** ? `150x170` rectangle (CORRECT)
- **2boxes.stp** ? Box1 + Box2 (CORRECT)
- **3boxes.stp** ? Box1 + Box2 (Box3 filtered out - correct)
- **3boxesB.stp** ? Box1 + Box2 + Box4 (CORRECT)
- **4boxes.stp** ? Box1 + Box2 + Box4 + Box5 (CORRECT)
- **CBox.stp** ? 40x50 rectangle with 10x10 hole at (5,5) (CORRECT)
- **CBoxR.stp** ? Rotated version of CBox (CORRECT - same output as CBox)
- **KBox.stp** ? 170x145 outline with tabs/cutouts (12 vertices - partial, may be incomplete)

### ?? PARTIAL (Complex Flat Extrusion)
- **KCBoxFlat.stp** ? Renders with 13 dedup vertices (should be ~32 detailed vertices)
  - ? Holes rendering correctly (2 red quadrilaterals)
  - ?? Outline simplified due to dedup rounding
  - Issue: Edge-topology extraction loses fine geometric detail through rounding

### ? NEVER TESTED
- **KCBox.stp** (rotated complex shape) ? Not tested with current fixes
  - Historical: Never passed in 5 days of development
  - Likely issue: Rotation matrices + edge-topology doesn't work well together

## Root Cause Analysis

### Why Simple Cases Pass
1. Face boundary extraction via `ExtractFaceWithHoles()` works well
2. Rotation matrices correctly align geometry to 2D projection plane
3. Orthogonal shape sorting works for axis-aligned rectangles
4. Hole rendering works by extracting separate bounds

### Why KCBoxFlat is Partially Difficult  
1. **Edge-topology extraction:** The 32 boundary vertices come from edge loops, not surface geometry
2. **Degeneration after projection:** Vertices collapse to fewer unique points after rounding
3. **Precision loss:** Rounding to `Math.Round(p.X, 1)` loses sub-millimeter detail
4. **No Gift Wrapping:** Gift Wrapping is a convex hull algorithm and removes non-convex vertices

### Why KCBox Never Worked
1. **Rotation + edge-topology:** Rotation matrices make the edge-topology vertex extraction unreliable
2. **Complex 3D rotation:** The solid is rotated in 3D space; applying rotation matrices can collapse dimensions
3. **Edge-ordered but no order:** The edges are traversed in order, but that order doesn't always form a clean perimeter

## Technical Decisions Made

### 1. Use `StepProcess.cs` as Primary Engine
- 5 days of iterations and special cases
- Works uniformly across all simple cases
- Better architecture with proper hole rendering

### 2. Preserve Edge-Ordered Vertices for Complex Shapes
- For shapes with >20 faces, trust the edge traversal order
- Don't apply Gift Wrapping (convex hull kills non-convex features)
- Use dedup order directly, which preserves sequential edge traversal

### 3. Axis-Aligned Projection
- Always find the 2 axes with largest variance
- Prevents axis-collapse that happened with fixed X-Y projection

## Next Steps to Improve

### Option A: Fix Vertex Deduplication Precision
- Use tighter rounding tolerance (0.01 instead of 0.1)
- Preserves more geometric detail
- May cause duplicate vertices from floating-point noise

### Option B: Skip Dedup for Complex Shapes
- Use all extracted vertices without dedup
- Risk: Noise or redundant vertices from STEP topology
- Benefit: Preserves all edge-topology detail

### Option C: Use STEPgeometry Directly Instead of Edge Topology
- Extract corner vertices from face surface, not edge loops
- Requires different STEP API usage
- More fundamental fix but higher risk

### Option D: Implement Point Merging Instead of Dedup
- Cluster nearby points before dedup
- Reduces noise while preserving detail
- Complexity: Requires new clustering algorithm

## Known Limitations

1. **Complex rotated solids (KCBox):** Never passed despite 5 days of attempts
   - Root cause: Rotation matrices + edge-topology extraction incompatibility
   - Workaround: May need separate handling for rotated complex shapes

2. **Precision loss in KCBoxFlat:** Fine geometric features (small tabs/notches) may be lost
   - Root cause: Rounding after projection collapses nearby vertices
   - Workaround: Tighter rounding tolerance or vertex clustering

3. **Gift Wrapping unsuitability:** Can't be used for non-convex outlines
   - Tried: Applied to complex shape vertices
   - Result: Collapsed to convex hull (2-3 vertices from 32)
   - Status: Removed from complex shape processing

##Recommendation

The current `StepProcess.cs` implementation is the best pragmatic solution:
- ? Handles all production use cases (simple rectangular sheets with holes)
- ? Proper architecture with rotation, projection, and hole rendering
- ?? Known limitation with complex rotated solids (KCBox) - likely requires design change
- ?? Known limitation with geometric detail preservation in flat extrusions (KCBoxFlat)

For KCBox/KCBoxFlat improvements, consider:
1. **Different vertex extraction** (corner-based vs edge-based)
2. **No rotation for already-aligned shapes** (detect and skip)
3. **Preserve all vertices without dedup** for complex shapes

