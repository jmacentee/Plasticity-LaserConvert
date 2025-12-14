# ROOT CAUSE ANALYSIS - VERTEX ORDERING PROBLEM

## THE ACTUAL PROBLEM (Not Gift Wrapping)

After extensive analysis, the issue is NOT convex vs concave - it's simpler than that.

### What We're Seeing

**Box1 (4 vertices):**
- Raw 3D: `(0,0), (155,0), (0,150), (155,150)`
- Normalized 2D: `(0,0), (155,0), (0,150), (155,150)` 
- After dedup: Same order
- Problem: Creates path `M 0,0 L 155,0 L 0,150 L 155,150 Z`
- This has diagonal from (155,0)?(0,150) and (155,150)?(0,0)
- **Why?** Vertices 2 and 3 are swapped - should be (155,150) then (0,150)

**The Core Issue:** 
The vertices from `ExtractFaceWithHoles` for a rectangle come as:
```
bottom-left, bottom-right, top-left, top-right
```
But proper perimeter order should be:
```
bottom-left, bottom-right, top-right, top-left
```

This is NOT a complex ordering problem. This is a **systematic vertex swap** in the extraction itself.

## Why Gift Wrapping Was Wrong

Gift Wrapping (Jarvis March) creates a **convex hull**, which is correct for identifying boundaries but:
1. It reorders vertices based on angles/distances
2. For concave shapes, it REMOVES interior boundary vertices
3. Example: KBox with 12 boundary vertices ? 5 convex hull vertices

Removing Gift Wrapping was correct, but now we need to fix the REAL problem: the systematic vertex ordering from extraction.

## The Real Root Cause

Looking at ALL test cases, there's a pattern:

**For simple geometries (boxes, rotated boxes):**
- Vertices come in pairs: (point A at Z1, point A at Z2), (point B at Z1, point B at Z2)
- After dedup and projection, they're in wrong perimeter order
- But there are only 4 of them, and Gift Wrapping "fixed" it by reordering

**For complex geometries (KBox, KCBox, KCBoxFlat):**
- Vertices come from edge topology traversal
- They should form a connected perimeter but come in a different order
- Gift Wrapping couldn't help because it removed too many vertices

## The ACTUAL Solution

The vertices ARE correct - they just need to be CONNECTED properly in the SVG path.

The issue is that `ExtractFaceWithHoles` returns vertices from **edge loop traversal**, which doesn't guarantee perimeter order.

**We need to:**
1. Keep ALL vertices (don't use convex hull)
2. Reorder them into ACTUAL perimeter sequence
3. Use an algorithm that preserves all boundary vertices (not Gift Wrapping)

## Correct Algorithm: Monotone Chain Without Convex Hull

Use the "monotone chain" concept but KEEP all vertices:
1. Sort vertices by X (then Y as tiebreaker)
2. Build lower chain: left-to-right, preserving all points
3. Build upper chain: right-to-left, preserving all points
4. Concatenate to form complete perimeter

This preserves ALL boundary vertices (including concave ones) while ordering them correctly.

Alternative: **Nearest-Neighbor with backtracking detection**
1. Start at leftmost point
2. Walk to nearest unvisited point
3. Mark visited
4. Repeat until all points visited
5. The path naturally traces the perimeter

Actually, even simpler: The vertices ARE mostly in order already. The issue is just a few swaps.

## What We Actually Need

Look at Box1 output:
```
After dedup: (0,0), (155,0), (0,150), (155,150)
Expected:   (0,0), (155,0), (155,150), (0,150)
```

The problem is: vertices 2 and 3 are swapped.

For a simple fix: **detect when consecutive vertices aren't adjacent on the perimeter and swap/reorder them**.

But this becomes complex for non-axis-aligned shapes.

## The REAL Answer: Use Correct Face Selection

Looking deeper at the extraction logs:

For KBox, we're getting 34 vertices with 3 bounds. The LARGEST bound (area=6338) has all 34 vertices and is the actual perimeter.

For KCBox and KCBoxFlat same thing - largest bound has all the outline vertices.

The issue isn't vertex ordering - it's that **we're using the vertices in the EXACT order they come from the edge loop**, which traverses edges, not a perimeter walk.

## The SIMPLEST FIX

Don't reorder at all. The vertices from bounds extraction are ALREADY in the correct perimeter order if you follow them as-is.

The problem with Box1 showing diagonals is NOT a reordering problem - it's that the 4 vertices aren't connected in the right sequence.

After dedup we get: `(0,0), (155,0), (0,150), (155,150)`

If we just connect them in that order, we get the diagonals. But if they came from edge loops in that order, it means the edge loops were traversed in that order.

**The real issue**: Are we extracting from the CORRECT face? Are we getting vertices from the TOP face or some side face?

Looking at KCBoxFlat - it has 32 boundary vertices and 2 holes. If Gift Wrapping reduced 32?8, that means it's treating the polygon as convex with 8 corners.

The 32 vertices represent all the interior points of the complex outline (the tabs, cutouts, etc.). Gift Wrapping correctly identified that if you draw a convex hull, you only need 8 points.

But we NEED all 32 to show the tabs and cutouts.

So the solution is: **DON'T use Gift Wrapping** (which was only half-right for simple boxes and completely wrong for complex shapes).

Instead, **keep the dedup order as-is, because it's the correct perimeter traversal order from the edge loops.**

But then why do we see diagonals in Box1?

Let me look again at the Box1 case in the debug output... 

The path is `M 0,0 L 155,0 L 0,150 L 155,150 Z`

If this is the order from edge loops, then the edges must be traversed as:
- Edge1: (0,0) to (155,0) - bottom horizontal
- Edge2: (155,0) to (0,150) - DIAGONAL?
- Edge3: (0,150) to (155,150) - top horizontal
- Edge4: (155,150) to (0,0) - DIAGONAL?

That doesn't make sense for a rectangle. A rectangle should have:
- Bottom: (0,0) to (155,0)
- Right: (155,0) to (155,150)  
- Top: (155,150) to (0,150)
- Left: (0,150) to (0,0)

But we're getting the wrong vertices!

The real issue: **The vertices being extracted are NOT in the right sequence from the edges.**

Or more likely: **We're extracting from the wrong face or misinterpreting which vertices belong where.**

## ACTUAL ROOT CAUSE

The bug is in HOW we're reading vertices from edges.

For a rectangular face with 4 edges, we should get 4 vertices (the corners). But we're getting them in an order that doesn't match the edge traversal.

This suggests the edges might be:
- Edge to corner 1: (0,0)
- Edge to corner 2: (155,0)
- Edge to corner 3: (0,150) [should be corner 4]
- Edge to corner 4: (155,150) [should be corner 3]

The issue is in `ExtractFaceWithHoles` or how it processes edge loops.

Without seeing the actual STEP file topology, the easiest fix is to **ensure vertices are ordered correctly after extraction**.

The correct approach: **Don't change the extraction - instead, fix the ordering with a proper perimeter algorithm THAT PRESERVES ALL VERTICES.**

Use Monotone Chain (without convex hull reduction).
