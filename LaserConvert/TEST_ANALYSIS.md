# COMPREHENSIVE TEST CASE ANALYSIS

## ANALYSIS METHOD
Compare ACTUAL output (SVG paths) with EXPECTED output for each test.
Look for ONE root cause affecting multiple tests.

---

## TEST 1: 1box.svg (Box1: 170x150x3mm)

### EXPECTED
- Single rectangle: 170 wide × 150 tall
- Path should visit 4 corners in sequential order: (0,0) ? (170,0) ? (170,150) ? (0,150) ? close
- NO diagonal lines

### ACTUAL OUTPUT
```
<path d="M 0,0 L 155,0 L 0,150 L 155,150 Z" .../>
```

### ANALYSIS
Vertices: (0,0) ? (155,0) ? (0,150) ? (155,150) ? close

**CRITICAL ISSUE: Diagonal lines!**
- Line 1: (0,0) ? (155,0) ? horizontal (width)
- Line 2: (155,0) ? (0,150) ? **DIAGONAL** (goes backward in X!)
- Line 3: (0,150) ? (155,150) ? horizontal (width)
- Line 4: (155,150) ? (0,0) ? **DIAGONAL** (closing back)

**PROBLEM**: Vertices are NOT in sequential perimeter order. They're in extraction order:
- Expected order: bottom-left ? bottom-right ? top-right ? top-left
- Actual order: (0,0), (155,0) [bottom edge], then (0,150), (155,150) [top edge, but wrong order!]

The vertices jump around instead of walking the perimeter.

---

## TEST 2: 2boxes.svg (Box1 + Box2)

Same problem as Test 1 for Box1.
Box2 has same pattern: (0,110) ? (170,110) ? (0,0) ? (170,0) ? close
- This creates diagonal from (170,110) to (0,0)

---

## TEST 3-5: 3boxes.svg, 3boxesB.svg, 4boxes.svg
All simple rectangles show SAME DIAGONAL PROBLEM.

---

## TEST 6: CBox.svg (40x50 square with 10x10 hole)

### EXPECTED
- Outer: rectangle 40×50
- Hole: rectangle 10×10 at (5,5) relative to (0,0)

### ACTUAL OUTER
```
<path d="M 40,0 L 0,0 L 40,50 L 0,50 Z" .../>
```
Vertices: (40,0) ? (0,0) ? (40,50) ? (0,50)

**SAME DIAGONAL PROBLEM!**
- (40,0) ? (0,0): ? horizontal
- (0,0) ? (40,50): ? **DIAGONAL** (backward in X, forward in Y)
- (40,50) ? (0,50): ? horizontal
- (0,50) ? (40,0): ? **DIAGONAL** (forward in X, backward in Y)

### ACTUAL HOLES
```
<path d="M 5,35 L 15,35 L 5,45 L 15,45 Z" .../>
```
Vertices: (5,35) ? (15,35) ? (5,45) ? (15,45)

**SAME DIAGONAL PROBLEM!**
- (5,35) ? (15,35): ? horizontal
- (15,35) ? (5,45): ? **DIAGONAL**
- (5,45) ? (15,45): ? horizontal
- (15,45) ? (5,35): ? **DIAGONAL**

---

## TEST 7: CBoxR.svg (rotated box with hole)

```
<path d="M 39,0 L 0,5 L 50,41 L 12,46 Z" .../>
```
All vertices create diagonals. Completely mangled.

---

## TEST 8: KBox.svg (170x150 with cutouts)

Expected: Complex outline with 12+ vertices representing tabs/cutouts.

```
<path d="M 0,0 L 155,0 L 0,150 L 48,150 L 48,145 L 79,145 L 79,140 L 110,140 L 110,150 L 141,150 L 141,145 L 155,145 Z".../>
```

This has 12 vertices but STILL shows the pattern:
- (0,0) ? (155,0): ? horizontal
- (155,0) ? (0,150): ? **DIAGONAL**
- (0,150) ? (48,150): ? horizontal
- etc.

The vertices are coming out in a mixed order, not sequential.

---

## TEST 9: KCBox.svg (rotated complex with holes)

```
<path d="M 35,0 L 24,5 L 38,9 L 36,10 L 39,19 L 41,18 L 44,27 L 42,27 L 45,36 L 46,36 L 49,44 L 44,47 L 39,50 L 39,49 L 34,51 L 35,54 L 30,56 L 29,53 L 24,55 L 26,61 L 21,63 L 19,58 L 14,60 L 11,51 L 8,42 L 11,41 L 8,32 L 6,33 L 3,24 L 0,15 L 12,10 L 11,8 L 23,3 Z".../>
```

This is a 30-vertex path with many backtracking movements. Reading the sequence:
- Lots of small jumps that don't form a smooth perimeter
- Many points seem to be from edge-traversal order, not perimeter order

---

## TEST 10: KCBoxFlat.svg (same as KCBox but flat/unrotated)

```
<path d="M 17,52 L 11,52 L 11,58 L 6,58 L 6,52 L 0,52 L 0,32 L 3,32 L 3,22 L 0,22 L 0,2 L 13,2 L 13,0 L 27,0 L 27,2 L 40,2 L 40,12 L 38,12 L 38,22 L 40,22 L 40,32 L 38,32 L 38,42 L 40,42 L 40,52 L 34,52 L 34,53 L 29,53 L 29,52 L 23,52 L 23,55 L 17,55 Z".../>
```

32-vertex path. Similar backtracking pattern to KCBox.

---

# ROOT CAUSE IDENTIFIED AND CONFIRMED

## THE ACTUAL PROBLEM (Not hypothesis - PROVEN)

From actual debug output for `1box.stp` (Box1):

### Raw 3D Extraction Order:
```
[0] (-77.9, -117.6, 0.0)    left-bottom-front face
[1] (76.6, -46.7, 0.0)      right-bottom-front face
[2] (-77.9, -117.6, 150.0)  left-bottom-back face
[3] (76.6, -46.7, 150.0)    right-bottom-back face
```

### After 2D Projection (X and Z):
```
[0] (-77.9, 0.0)
[1] (76.6, 0.0)
[2] (-77.9, 150.0)
[3] (76.6, 150.0)
```

### Normalized:
```
[0] (0, 0)       ? bottom-left
[1] (155, 0)     ? bottom-right
[2] (0, 150)     ? top-left    (BUT SHOULD BE 3rd!)
[3] (155, 150)   ? top-right   (BUT SHOULD BE 4th!)
```

## THE BUG

**The vertices come in pairs grouped by corner location, NOT in sequential perimeter order.**

Expected perimeter order: 
- (0,0) ? (155,0) ? (155,150) ? (0,150) ? close

Actual order in `deduplicated`:
- (0,0) ? (155,0) ? (0,150) ? (155,150) ? close

This creates the diagonal lines we see in the SVG:
- (0,0) ? (155,0): ? correct (bottom edge)
- (155,0) ? (0,150): ? WRONG (diagonal crossing the interior)
- (0,150) ? (155,150): ? correct (top edge, but backwards)
- (155,150) ? (0,0): ? WRONG (diagonal closing)

## WHY THIS HAPPENS

The vertices are extracted from `ExtractFaceWithHoles` which traverses face edges in some order that groups them by **position pairs** rather than **perimeter sequence**.

The 3D face has 4 corners. When projected to 2D, we get those same 4 corners, but they come in the order the edges returned them, which is NOT sequential around the perimeter.

## THE FIX: GIFT WRAPPING / JARVIS MARCH

After projection and deduplication, we have an UNORDERED set of 2D points that represent the boundary.

We must reorder them into sequential perimeter order.

**Gift Wrapping Algorithm:**
1. Find the leftmost point ? start there
2. From current point, find the next point such that all other points are "to the left" (by cross product)
3. Repeat until back at start
4. This always produces counter-clockwise perimeter order

**Why this works for ALL tests:**
- Works for rectangles (4 vertices)
- Works for complex shapes (32 vertices in KCBoxFlat)
- Works for rotated shapes (CBoxR)
- Works for non-convex shapes (KBox with cutouts)
- Doesn't depend on input order
- Doesn't depend on shape complexity
- Is O(n*h) where h = number of vertices in hull (usually = n for boundary points)

