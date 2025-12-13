# CRITICAL FINDING: Face Selection Problem (Step 4)

## The Real Bug is NOT in Step 5 (Rotation)

Step 5 is working correctly. The rotMatrix1 and rotMatrix2 are properly computed.

## The Real Problem: Step 4 - Face Selection

**Current Strategy (WRONG):**
- Pick the face with the MOST BOUNDARY VERTICES
- For KCBox, this returns a face with 34 vertices
- Those 34 vertices are extracted from edge-loop topology
- Edge-loop vertices are 1D (they're on the edges of the solid, not the surface)
- When collapsed to 2D, they have degenerate ranges: X=[-19.3, -19.3] (SINGLE VALUE!)

**Evidence from logging:**
```
[SVG] KCBox: After rotation - X:[-5.8,44.6] Y:[-55.1,8.6]   ? All 84 vertices - Good X and Y ranges
[SVG] KCBox: Outline vertices after rotation - X:[-19.3,-19.3] Y:[-52.3,-12.3]   ? 34 face vertices - X is DEGENERATE!
```

All X values in the outline are identical (- 19.3)! This means:
1. The 34 vertices aren't spread across the outline
2. They're all in a single vertical line  
3. They represent edge topology, not surface geometry

## Why This Happens

`ExtractFaceWithHoles` returns vertices from `StepFaceBound` -> `StepEdgeLoop` chains.

For a thin, complex shape like KCBox:
- The shape has thin walls (3mm)
- A "face" with complex topology is actually a face with many small rectangular faces representing the tabs/cutouts
- These faces are arranged in a specific pattern around the perimeter
- But when you extract the bound vertices from such a face, you get vertices from the EDGES of that small face
- If the face is oriented in a certain way, all those vertices fall on a single line in 3D

## The Real Solution

We need to select a face that has its main surface spread across the outline geometry, not a face that's a small tab or edge. The current heuristic of "most boundary vertices" is backwards - it selects faces with many edges/tabs, not the main flat surface.

### Better Face Selection Strategy

For complex shapes:
1. Try to find a face that when rotated has good 2D extent (not degenerate)
2. Check if extracted vertices cover a good 2D area after rotation
3. If none work, fall back to using a synthetic outline from ALL vertices

### Immediate Fix

Instead of selecting the face with most vertices, select the face whose 34 extracted vertices, when rotated, have the LARGEST 2D area (not 1D degenerate).
