# GIFT WRAPPING IMPLEMENTATION REQUIREMENTS

## CRITICAL CONSTRAINT: Must Not Break KBoxFlat

KBoxFlat is currently rendering with reasonable visual correctness for the outline (though holes are wrong).
This means its 32 vertices, despite being in the WRONG ORDER per Gift Wrapping logic, are somehow producing a recognizable shape.

## Root Cause Analysis

**Box1 (simple rectangle):**
- Current: (0,0) ? (155,0) ? (0,150) ? (155,150)
- Distances: 155, 215.7 (diagonal), 155, 215.7 (diagonal)
- PROBLEM: Vertices 0,1,2,3 are in PAIRED order, not PERIMETER order
- NEEDED: (0,0) ? (155,0) ? (155,150) ? (0,150)
- Distances would be: 155, 150, 155, 150 (all axis-aligned edges)

**KBoxFlat (complex 32-vertex shape):**
- Current order: (17,52) ? (11,52) ? (11,58) ? (6,58) ? (6,52) ? ...
- This IS following some edge topology order
- Despite wrong order, it produces a RECOGNIZABLE shape because:
  - The backtracking may still close loops
  - The visual error is in WHICH direction the diagonals go, not whether they exist

## The Gift Wrapping Algorithm

```
1. Find leftmost point (lowest X, break ties by lowest Y)
2. Set current = leftmost, ordered = [], next = undefined
3. Loop:
   4. Add current to ordered
   5. next = any other point
   6. For each unvisited point p:
      7. cross = CrossProduct((p - current), (next - current))
      8. If cross > 0: next = p  (p is more clockwise)
      9. Else if cross == 0 and dist(current, p) > dist(current, next): next = p (collinear, take farther)
   10. current = next
   11. If current == leftmost: break
12. Return ordered
```

## Why Gift Wrapping Works

For ANY set of 2D boundary points (even in random order), Gift Wrapping produces a sequential perimeter walk.

Cross product determines "rightmost turn": if we're at point A looking toward point B, and point C has a positive cross product, then C is to the RIGHT of the line AB, meaning C comes "after" B in clockwise order.

## Risk: Breaking KBoxFlat

If KBoxFlat's current visual correctness relies on a specific order that creates the right overall shape by accident, Gift Wrapping will REORDER it.

**Hypothesis:** KBoxFlat works because even though vertices are in edge-order, they still visit most corners. Gift Wrapping will find the TRUE perimeter order.

**Expected Result:** Gift Wrapping should IMPROVE KBoxFlat, not break it, because it will:
1. Fix the backtracking diagonals
2. Produce a proper CCW perimeter walk
3. Still hit all 32 vertices

## Implementation Strategy

**No special cases:**
- Don't detect "simple" vs "complex" shapes
- Don't have different algorithms for different vertex counts
- Apply Gift Wrapping uniformly to ALL polygons after deduplication

**Single algorithm for all:**
1. After dedup, have N vertices (4, 12, 32, 34, etc.)
2. Apply Gift Wrapping to get perimeter order
3. Build SVG path from reordered vertices
4. Process holes separately with Gift Wrapping

**Hole handling:**
- Holes should be ordered OPPOSITE direction from outer boundary (CW vs CCW)
- OR: Apply Gift Wrapping but then reverse the order for holes to make them CW

## Validation Plan

After implementation:

1. **Box1:** Should go from (0,0)?(155,0)?(0,150)?(155,150) to (0,0)?(155,0)?(155,150)?(0,150)
   - Distances change from [155, 215.7, 155, 215.7] to [155, 150, 155, 150]
   - Path visually becomes a proper rectangle

2. **KBoxFlat:** Should improve (diagonals eliminated, proper perimeter)
   - Distances between consecutive vertices should decrease (no more 215+ unit jumps for a ~40×58 shape)
   - Outline becomes cleaner

3. **KCBox:** Should improve from current jumbled order
   - 33 vertices now walk the perimeter, not backtrack

4. **Holes:** May need order reversal for proper SVG rendering

## NO SPECIAL CASES allowed

This is critical. The implementation must:
- Use the SAME Gift Wrapping for all shapes (4 verts to 34 verts)
- Use the SAME Gift Wrapping for holes
- Only difference: hole direction (reversed or negated)
- No detection of "axis-aligned" vs "rotated" vs "complex"
- No threshold-based logic
- No fallback algorithms
