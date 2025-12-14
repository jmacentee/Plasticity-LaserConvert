# ALGORITHM VERIFICATION: Nearest-Neighbor Walk with Angle-based Sorting

## HYPOTHESIS
Use a combination approach:
1. Find a guaranteed corner point (e.g., leftmost-lowest = guaranteed convex corner)
2. Sort all other vertices by polar angle from that corner
3. Walk the perimeter in angle order
4. Preserve ALL vertices (no removal, no simplification)

## TEST CASE ANALYSIS

### Case 1-5: Simple Rectangles (Box1, Box2, Box4, Box5, CBox outer)

**Input (Box1 after dedup):**
```
(0, 0)      ? leftmost-lowest corner
(155, 0)    ? rightmost
(0, 150)    ? top-left
(155, 150)  ? top-right
```

**Leftmost-lowest point:** (0, 0)

**Angle from (0,0):**
- (155, 0):     angle = 0°
- (155, 150):   angle = atan2(150, 155) ? 44°
- (0, 150):     angle = 90°

**Sorted by angle:** (0,0) ? (155,0) ? (155,150) ? (0,150) ? close

**Expected path:** M 0,0 L 155,0 L 155,150 L 0,150 Z ?

**Verdict:** ? WORKS - produces correct rectangular perimeter

---

### Case 6: CBoxR (Rotated Rectangle)

**Input (after dedup):**
```
(39, 0)     ? appears to be left-ish, bottom-ish  
(0, 5)      ? leftmost
(50, 41)    ? rightmost
(12, 46)    ? topmost
```

**Leftmost-lowest point:** (0, 5)

**Angle from (0, 5):**
- (39, 0):     angle = atan2(-5, 39) ? -7.3° or 353° (below)
- (50, 41):    angle = atan2(36, 50) ? 35.5°
- (12, 46):    angle = atan2(41, 12) ? 73.6°

**Sorted by angle (0°-360°):** (0,5) ? (39,0) [353°, wraps] ? (50,41) ? (12,46)

Wait, we need to handle wrap-around. Better to sort as going clockwise from bottom:
- (39, 0): angle ? -7.3° (or 353°)  
- (50, 41): angle ? 35.5°
- (12, 46): angle ? 73.6°

If we normalize to 0-360°:
- (39, 0): 353°
- (50, 41): 35.5° 
- (12, 46): 73.6°

Sorted: 35.5°, 73.6°, 353° = (50,41) ? (12,46) ? (39,0)

**Result path:** (0,5) ? (50,41) ? (12,46) ? (39,0) ? close

**Expected path:** M 0,5 L 39,0 L 50,41 L 12,46 Z

**Actual output:** M 0,5 L 39,0 L 50,41 L 12,46 Z ?

**Problem:** The angle order gives us (50,41) first, but expected has (39,0) first!

**This is WRONG for CBoxR!** The angle-sorting approach produces a different perimeter order than expected.

---

### Case 7: KBox (12 vertices with tabs)

**After dedup (12 verts):**
```
[0] (0, 0)      ? leftmost-lowest
[1] (155, 0)
[2] (0, 150)
[3] (48, 150)
[4] (48, 145)
[5] (79, 145)
[6] (79, 140)
[7] (110, 140)
[8] (110, 150)
[9] (141, 150)
[10] (141, 145)
[11] (155, 145)
```

**Leftmost-lowest point:** (0, 0)

**Angles from (0, 0):**
- (155, 0):     0°
- (155, 145):   atan2(145, 155) ? 43°
- (141, 150):   atan2(150, 141) ? 47°
- (110, 150):   atan2(150, 110) ? 54°
- (48, 150):    atan2(150, 48) ? 72°
- (0, 150):     90°
- (141, 145):   atan2(145, 141) ? 46°
- (110, 140):   atan2(140, 110) ? 52°
- (79, 145):    atan2(145, 79) ? 61°
- (79, 140):    atan2(140, 79) ? 60°
- (48, 145):    atan2(145, 48) ? 71°

**Sorted by angle:**
0° ? 43° ? 46° ? 47° ? 52° ? 54° ? 60° ? 61° ? 71° ? 72° ? 90°

**(155,0) ? (155,145) ? (141,145) ? (141,150) ? (110,140) ? (110,150) ? (79,140) ? (79,145) ? (48,145) ? (48,150) ? (0,150) ? (0,0)**

**Expected path:** (0,0) ? (155,0) ? (155,145) ? (141,150) ? (141,145) ? (110,140) ? (110,150) ? (79,145) ? (79,140) ? (48,145) ? (48,150) ? (0,150) ? (0,0)

Let me compare with angle-sorted:
- Angle-sorted: (155,0) ? (155,145) ? (141,145) ? (141,150) ? (110,140) ? (110,150) ? (79,140) ? (79,145) ? (48,145) ? (48,150) ? (0,150)
- Expected:     (155,0) ? (155,145) ? (141,150) ? (141,145) ? (110,140) ? (110,150) ? (79,145) ? (79,140) ? (48,145) ? (48,150) ? (0,150)

**DIFFERENCE:** 
- Angle-sorted has: (141,145) **before** (141,150)
- Expected has:      (141,150) **before** (141,145)

Also:
- Angle-sorted has: (79,140) **before** (79,145)  
- Expected has:      (79,145) **before** (79,140)

**This is WRONG!** Angle sorting doesn't work for KBox either.

---

## ROOT CAUSE ANALYSIS

The problem with angle-based sorting from a corner:

**Angle sorting visits points in polar angle order from the corner.** But the perimeter is NOT necessarily in polar angle order!

**Counter-example:** The tab vertices
```
(141, 150) - top of tab
(141, 145) - return point of tab

From (0,0):
- (141,145): angle ? 46.1°
- (141,150): angle ? 47.1°

Angle sort puts (141,145) first.
But walking the RIGHT EDGE, we go UP to (141,150), THEN back down to (141,145).
```

The actual perimeter path requires going **UP then DOWN** at the tab, not by angle order.

---

## THE REAL PATTERN

Looking at the expected outputs:

**For rectangles:** Vertices are ordered left-to-right-bottom, then right-top, then left-top, then back.
- Bottom edge: increasing X
- Right edge: increasing Y  
- Top edge: decreasing X (going backwards)
- Left edge: decreasing Y (going backwards)

**For complex shapes with tabs:** Same principle - walk the actual boundary edges, not by angle.

The problem is: **the vertices from ExtractFaceWithHoles are NOT in perimeter order.**

---

## WHAT WE ACTUALLY NEED

The vertices come from edge-loop traversal. The question is: **are they in the order they appear on the boundary?**

Looking at KBox dedup order again:
```
[0] (0, 0)      ? bottom-left corner
[1] (155, 0)    ? bottom-right corner
[2] (0, 150)    ? top-left corner (JUMP!)
[3] (48, 150)   ? tab point
[4] (48, 145)   ? tab return
[5] (79, 145)   ? another tab
[6] (79, 140)   ? tab depth
[7] (110, 140)  ? more tab
[8] (110, 150)  ? tab top
[9] (141, 150)  ? tab point
[10] (141, 145) ? tab return
[11] (155, 145) ? right side before tab
```

**The dedup order is NOT a perimeter walk!**

It goes:
- (0,0) ? (155,0) [along bottom]
- (155,0) ? (0,150) [GIANT JUMP to top-left!]
- Then walks some tabs
- [missing connections between vertices]

This is the edge-loop extraction order, which doesn't follow the perimeter.

---

## CORRECT SOLUTION: Use Edge Connectivity

Instead of geometric sorting, we need to **follow the edges**.

The vertices ARE connected in the STEP topology - they're endpoints of edges that form the perimeter loop.

**Algorithm:**
1. Start at a known corner (e.g., leftmost-lowest)
2. Find the edge starting from this corner
3. Follow edges sequentially around the perimeter
4. Build the path by walking edges, not sorting by geometry

**Why this works:**
- Edges define the topology of the perimeter
- Following edges guarantees a valid perimeter walk
- Works for convex, concave, and any shape
- Preserves all vertices automatically

**Problem:** We don't have direct edge-to-vertex mapping in our current extraction.

---

## ALTERNATIVE: Reconstruct from Dedup Order

Actually, looking more carefully at the dedup order for KBox:

The 12 deduplicated vertices ARE all the perimeter vertices. The question is just their sequence.

What if we:
1. Find leftmost-lowest point: (0, 0)
2. Build a **perimeter walk graph** by connecting nearby points
3. Start from (0, 0) and walk to nearest unvisited point
4. Continue until all visited

**Nearest-Neighbor actually might work IF we're careful:**

From (0,0), nearest unvisited: (155,0) at distance 155 ?
From (155,0), nearest unvisited: (155,145) at distance 145 ?
From (155,145), nearest unvisited: (141,150) at distance ~17 ?
From (141,150), nearest unvisited: (141,145) at distance 5 ?
From (141,145), nearest unvisited: (110,150) at distance ~35? or (110,140)?

Wait, from (141,145):
- (110,150): distance = sqrt((141-110)² + (145-150)²) = sqrt(961 + 25) ? 31.4
- (110,140): distance = sqrt((141-110)² + (145-140)²) = sqrt(961 + 25) ? 31.4
- (79,145): distance = sqrt((141-79)² + (145-145)²) = 62

Nearest is (110,150) or (110,140) - they're equidistant!

**Nearest-neighbor has ambiguity problems.**

---

## CONCLUSION

**Nearest-neighbor angle-based sorting from a corner is NOT the correct general solution.**

It fails for:
- CBoxR: produces wrong vertex order
- KBox: produces wrong vertex order (angle doesn't match perimeter)

The real issue is that **we need to follow the actual edge topology, not geometric proximity or angles.**

**The correct approach:** Extract and use edge connectivity information to walk the perimeter in topological order, not geometric order.

OR: If we must use geometry, we need **monotone chain that preserves ALL vertices** (which we tried and failed), OR a different algorithm entirely.

---

## WHAT SHOULD ACTUALLY WORK

**Insight from the passing tests:**

For simple boxes with 4 vertices, monotone chain "happens to work" because they can be split into two monotone chains (lower and upper) that don't lose vertices.

For KBox with 12 vertices, monotone chain fails because vertices are removed during the left-turn test.

**The issue with monotone chain:** it's designed for convex hulls, which removes vertices. We need a version that **keeps all vertices even if they're collinear or non-monotone**.

Modified algorithm:
1. Sort by X (left to right)
2. For each point, **don't remove on right turns**, instead:
   - Mark the turn direction
   - Include point with metadata about whether it's concave/convex
3. Walk the result respecting the actual perimeter, not filtering

Actually, that just means: **Don't remove ANY vertices from monotone chain. Just reorder them.**

This is equivalent to: **Sort by X, then walk left-to-right building lower chain (keeping ALL), then walk right-to-left building upper chain (keeping ALL).**

