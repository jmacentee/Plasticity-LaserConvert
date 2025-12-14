# Gift Wrapping Debug for KBox

## Start Point: (0, 0)

From (0,0), we need to find which vertex makes the smallest left turn (most clockwise/rightmost turn).

First step: All points are "ahead" (coming from negative/left), so we compare using (1, 0) as reference direction.

For each point, cross product with (1, 0):
- (1, 0) × (dx, dy) = 1*dy - 0*dx = dy

So: 
- (155, 0): cross = 0
- (155, 145): cross = 145  
- (141, 145): cross = 145
- (141, 150): cross = 150
- (110, 145): cross = 145
- (110, 140): cross = 140
- (79, 145): cross = 145
- (79, 140): cross = 140
- (48, 150): cross = 150
- (48, 145): cross = 145
- (0, 150): cross = 150

**Minimum cross = 0 at (155, 0)** ? This is correct!

## Step 2: From (155, 0), previous point was (0, 0)

Previous direction: (155, 0) - (0, 0) = (155, 0)

For each unvisited point, compute cross product:
- prevDx * dy - prevDy * dx = 155 * dy - 0 * dx = 155 * dy

Points:
- (155, 145): cross = 155 * 145 = 22475 (positive = left turn)
- (141, 150): cross = 155 * 150 = 23250 (larger left turn)
- (141, 145): cross = 155 * 145 = 22475
- (0, 150): cross = 155 * 150 = 23250
- etc. - all positive (all are left turns)

**We want the SMALLEST positive cross = 22475**

Which points give 22475?
- (155, 145): 155 * 145 = 22475 ?
- (141, 145): 155 * 145 = 22475 ?
- (79, 145): 155 * 145 = 22475 ?
- (48, 145): 155 * 145 = 22475 ?

**Four vertices tie!** The tiebreaker is distance:
- (155, 145): distance = 145 (closest in dy)
- (141, 145): distance = sqrt((141-155)² + (145-0)²) = sqrt(196 + 21025) ? 145.6
- (79, 145): distance = sqrt((79-155)² + (145-0)²) ? 160
- (48, 145): distance = sqrt((48-155)² + (145-0)²) ? 179

**So (155, 145) is picked** ? This is correct!

## Step 3: From (155, 145), previous was (155, 0)

Previous direction: (155, 145) - (155, 0) = (0, 145)

For each unvisited:
- prevDx * dy - prevDy * dx = 0 * dy - 145 * dx = -145 * dx

Points (calculating cross):
- (141, 150): -145 * (141 - 155) = -145 * (-14) = 2030 (positive = left turn)
- (141, 145): -145 * (-14) = 2030
- (110, 150): -145 * (110 - 155) = -145 * (-45) = 6525
- (110, 140): -145 * (-45) = 6525
- (79, 145): -145 * (79 - 155) = -145 * (-76) = 11020
- (79, 140): -145 * (-76) = 11020
- (48, 150): -145 * (48 - 155) = -145 * (-107) = 15515
- (48, 145): -145 * (-107) = 15515
- (0, 150): -145 * (0 - 155) = -145 * (-155) = 22475

**Minimum positive = 2030 at (141, 150) and (141, 145)**

Tiebreaker by distance from (155, 145):
- (141, 150): sqrt((-14)² + 5²) = sqrt(196 + 25) = sqrt(221) ? 14.9
- (141, 145): sqrt((-14)² + 0²) = 14

**Distance tiebreaker: (141, 145) is closer!**

But we WANT to pick (141, 150) first!

## THE BUG IN GIFT WRAPPING

The gift wrapping algorithm as implemented picks (141, 145) because it's closer. But for the correct perimeter walk, we need (141, 150) first.

The issue: distance tie-breaking doesn't work for perimeter walking.

## THE FIX

When cross products are equal (collinear or near-collinear), we should NOT use distance as tiebreaker.

Instead:
- If we're moving AWAY from the start: pick the FARTHEST
- If we're moving BACK: pick the NEAREST

OR simpler: don't tie-break by distance. Instead, prefer the point that is FARTHEST in the cross-product direction.

Actually, the REAL fix: use a smarter cross product that distinguishes between:
- (141, 150): points "up" from our current direction
- (141, 145): points "down" from our current direction

The issue is both calculate the same cross product because they differ in Y, and we're moving in the Y direction.

## CORRECT SOLUTION

For tie-breaking, use:
1. Primary: cross product (turn direction)
2. Secondary (if tied): compare the SECONDARY cross product using the perpendicular direction
3. OR: use angle instead of just cross product

Actually, the fix is: **don't break ties by distance**. Instead, pick the one that is "more clockwise" in case of ties.

For (141, 145) vs (141, 150):
- Both make the same primary turn from direction (0, 145)
- But (141, 150) is FURTHER UP, which is "more to the right" from our current vector
- So use a secondary sort: angle

Let me compute angles:
From (155, 145), direction was (0, 145) pointing straight up (angle 90°)

To (141, 145): direction is (-14, 0) pointing left (angle 180°)
- Turn from 90° to 180° = 90° left turn

To (141, 150): direction is (-14, 5) pointing up-left (angle ~160°)
- Turn from 90° to 160° = 70° left turn

**70° turn < 90° turn, so (141, 150) makes the SMALLER left turn!**

The bug is that the cross product calculation doesn't capture this. We need to use ANGLE, not just cross product, or use a different cross product formula.

## THE REAL ALGORITHM

Gift wrapping should use:
1. Cross product for primary ordering (which side)
2. Angle for secondary ordering (how much)

OR: use atan2 to compute actual angles and just pick the smallest angle change.

Actually: **the standard gift wrapping uses only angles**, not cross products!

```
For each candidate point:
  angle = atan2(dy, dx)  // direction to candidate
  turn = angle - previousAngle  // turn required
  normalize turn to [-?, ?]
  pick point with smallest turn (most clockwise)
```

This naturally handles all cases without distance tiebreaking.

