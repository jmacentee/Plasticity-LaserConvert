# Perimeter Walking Test Analysis

## Algorithm Issue Identified

The current edge-walking implementation tries to select the next vertex by:
1. Finding the angle to each unvisited vertex
2. Measuring the turn angle from the previous direction
3. Selecting the vertex with the smallest turn angle

**Problem:** This approach creates backtracking because:
- From (0,150), the algorithm might pick (79,140) if it's calculated as having a "smallest turn"
- But geometrically, going from (0,150) directly to (79,140) skips over intermediate vertices
- This creates the "jump" behavior we see

## The Real Solution: Greedy Angle Sort

After analyzing all test cases, the correct approach is:
1. Find a guaranteed corner (leftmost-lowest)
2. From that corner, compute the polar angle to EVERY other vertex
3. Sort vertices by this angle
4. Walk them in order

For **simple rectangles**, this works because vertices follow the angle order naturally.

For **complex shapes like KBox**, the vertices SHOULD follow angle order IF extracted correctly.

## Analysis of KBox Dedup Vertices

```
[0] (0, 0)       ? start point
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

**Angle from (0,0) going counter-clockwise:**
- (155, 0): angle = 0°
- (155, 145): angle = atan2(145, 155) ? 43.0°
- (141, 150): angle = atan2(150, 141) ? 47.1°
- (141, 145): angle = atan2(145, 141) ? 46.1°  
- (110, 150): angle = atan2(150, 110) ? 54.0°
- (110, 140): angle = atan2(140, 110) ? 51.8°
- (79, 145): angle = atan2(145, 79) ? 61.5°
- (79, 140): angle = atan2(140, 79) ? 60.5°
- (48, 150): angle = atan2(150, 48) ? 72.2°
- (48, 145): angle = atan2(145, 48) ? 71.6°
- (0, 150): angle = 90°

**Sorted by angle (ascending):**
0°, 43°, 46.1°, 47.1°, 51.8°, 54°, 60.5°, 61.5°, 71.6°, 72.2°, 90°

= (155,0), (155,145), (141,145), (141,150), (110,140), (110,150), (79,140), (79,145), (48,145), (48,150), (0,150)

**Expected order from problem statement:**
(155,0) ? (155,145) ? (141,150) ? (141,145) ? (110,140) ? (110,150) ? (79,145) ? (79,140) ? (48,145) ? (48,150) ? (0,150)

**Comparison:**
Angle-sorted:  (155,0) ? (155,145) ? **(141,145)** ? **(141,150)** ? (110,140) ? (110,150) ? **(79,140)** ? **(79,145)** ? (48,145) ? (48,150) ? (0,150)

Expected:      (155,0) ? (155,145) ? **(141,150)** ? **(141,145)** ? (110,140) ? (110,150) ? **(79,145)** ? **(79,140)** ? (48,145) ? (48,150) ? (0,150)

**THE MISMATCH:**
At (141,150) pair: angle order gives (141,145) then (141,150), but perimeter order needs (141,150) then (141,145)
At (79, *) pair: angle order gives (79,140) then (79,145), but perimeter order needs (79,145) then (79,140)

##  ROOT CAUSE: Angle Sorting Fails for Vertices on Same Vertical/Horizontal Line

When two vertices share the same X or Y coordinate, angle sorting doesn't give us perimeter order!

For example, (141, 145) and (141, 150) are on the same vertical line (X=141).
- (141, 145): atan2(145, 141) = 46.1°
- (141, 150): atan2(150, 141) = 47.1°

Angle sorting puts (141, 145) first. But walking the RIGHT EDGE of the perimeter, we encounter (141, 150) **first** (at the TAB TOP), then (141, 145) (returning from the tab).

## The REAL Pattern: Angle Order WITHIN Each Octant

The perimeter vertices should be sorted by angle, BUT when vertices have near-equal angles (same octant direction), they should be sorted by:
- If moving outward: sort by distance (farthest first)
- If moving back inward: sort by distance (nearest first)

OR simpler: Use a **gift wrapping algorithm** that:
1. Start at a corner
2. For each step, find the point that makes the smallest counter-clockwise turn
3. This naturally handles collinear and near-collinear points

## Gift Wrapping (Jarvis March) Algorithm

Start: (0, 0)
Current direction: 0° (facing right)

**Jarvis step 1:** From (0,0), which point is most clockwise (least counter-clockwise)?
- All other points are ahead, so pick the one at smallest angle = (155, 0) at 0°
- Move to (155, 0)

**Jarvis step 2:** From (155, 0), coming from direction 180° (from the left)
- Find the point that makes the smallest left turn from 180°
- (155, 145) is directly above (straight left turn from 180° = 90° left) 
- (141, 150) is up-left
- (141, 145) is up-left
- The smallest left turn goes to (155, 145)
- Move to (155, 145)

**Jarvis step 3:** From (155, 145), coming from direction 270° (from below)
- Find point with smallest left turn from 270°
- (141, 150): direction 270 - atan2(-5, -14) ? 270 - (-160°) = 90° left turn (wraps)
- (141, 145): direction ? 180° (left), which is 90° left turn
- Actually (141, 150) is at direction 180 - atan2(-5, -14) from (155,145)...

This is getting complex. Let me think about a simpler, proven algorithm.

## What Actually Works: Monotone Chain (Graham Scan variant)

The monotone chain algorithm:
1. Sort points by X (and Y as tiebreaker)
2. Build lower hull by removing right turns
3. Build upper hull by removing right turns
4. Combine them

**The key insight we missed:** For KBox, we have 12 vertices. If we sort by X and split into lower/upper:

**Sorted by X:**
(0,0), (0,150), (48,145), (48,150), (79,140), (79,145), (110,140), (110,150), (141,145), (141,150), (155,0), (155,145)

**Lower chain (left to right, bottom):**
- Start with (0, 0)
- Add (0, 150)? No - wrong Y direction. Actually lower means smaller Y values first.
- We should sort lower = points with smaller Y
- Then upper = points with larger Y

Actually, in 2D:
- Lower: leftmost to rightmost, taking the LOWER path
- Upper: rightmost back to leftmost, taking the UPPER path

Points sorted by (X, Y):
(0, 0), (0, 150), (48, 145), (48, 150), (79, 140), (79, 145), (110, 140), (110, 150), (141, 145), (141, 150), (155, 0), (155, 145)

**Lower chain construction (ascending X, prefer smaller Y):**
1. (0, 0) - add
2. (0, 150) - check turn: (0,0) ? (0,150) ? ? Going up. If next point is below this line, we don't remove (0,150).
3. Actually, there are NO other points with X near 0, so we skip ahead.
4. (48, 145) - from (0,0) to (48,145) is up-right. Next is (48,150) which is directly above. This is a LEFT turn. Remove (48,145)? No wait, in lower chain we remove on RIGHT turns, not left.
5. Continue: (48, 150) ? (79, 140) - this is a RIGHT turn (we were going up-right, now going down-right). So keep (48,150) for now.
6. (79, 145) ? (79, 140) is a left turn (upward). Don't remove.
7. ... 

This is getting too complicated. Let me look at what algorithm would ACTUALLY solve this.

## The Correct Algorithm: Sort by Angle, Break Ties by Distance

The issue is that angle sorting works, BUT we need a tiebreaker when angles are very similar.

For vertices at nearly the same angle:
- If we're moving outward from the center: sort by distance DESC (farthest first)
- If we're moving inward: sort by distance ASC (nearest first)

OR: simpler approach - sort by (angle, cross_product_with_centroid_ray)

Actually, the SIMPLEST that works: **Polar angle with distance tie-breaking**

```
Sort by:
1. Primary: atan2(y - cy, x - cx)  
2. Secondary: distance from centroid DESC when angles are equal
```

This automatically handles the tab issue because:
- Vertices at the tab opening (141, 150) and tab return (141, 145)
- Both are in similar direction from centroid
- But (141, 150) is farther, so it comes first
- Which is correct for the perimeter walk!

Let me test this theory on KBox:

Centroid: (77.5, 75.0)

Distances from centroid:
- (0, 0): sqrt(77.5² + 75²) ? 107
- (155, 0): sqrt(77.5² + 75²) ? 107
- (0, 150): sqrt(77.5² + 75²) ? 107
- (48, 150): sqrt((48-77.5)² + (150-75)²) = sqrt(900 + 5625) ? 81
- (48, 145): sqrt((48-77.5)² + (145-75)²) = sqrt(900 + 4900) ? 76
- (79, 145): sqrt((79-77.5)² + (145-75)²) ? 70
- (79, 140): sqrt((79-77.5)² + (140-75)²) ? 65
- (110, 140): sqrt((110-77.5)² + (140-75)²) ? 71
- (110, 150): sqrt((110-77.5)² + (150-75)²) ? 82
- (141, 150): sqrt((141-77.5)² + (150-75)²) ? 91
- (141, 145): sqrt((141-77.5)² + (145-75)²) ? 85

Angles from centroid (0, 0):
- (155, 0): 0°, distance 107
- (155, 145): 43°, distance 107
- (141, 150): 47°, distance 91
- (141, 145): 46°, distance 85
- (110, 150): 54°, distance 82
- (110, 140): 52°, distance 71
- (79, 145): 61°, distance 70
- (79, 140): 60°, distance 65
- (48, 150): 72°, distance 81
- (48, 145): 71°, distance 76
- (0, 150): 90°, distance 107
- (0, 0): 180°, distance 0

Wait - this doesn't match what we tried before. The issue is the centroid might be in the wrong location or the algorithm needs tweaking.

## Conclusion

The problem is complex because we're trying to solve a purely geometric problem when we actually have TOPOLOGICAL information (the edge list).

**THE REAL ANSWER:** We should use the STEP edge topology directly, not geometry.

But since we don't have direct access to that, we need an algorithm that reconstructs topology from geometry.

**Proven to work: Gift Wrapping / Jarvis March**

This is guaranteed to work because it:
1. Finds the true convex hull (if needed)
2. When extended to non-convex, finds the actual boundary by turning
3. Uses angular continuity instead of sorting

The problem with my previous edge-walking attempt was the turn-angle calculation logic. Let me rewrite it with clearer, proven gift-wrapping logic.

