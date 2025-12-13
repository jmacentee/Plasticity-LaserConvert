# Focused Debug Plan: Step-by-Step Analysis of StepProcess for KCBox

## Decision: Using StepProcess

**Why StepProcess?**
- ? 8 of 10 tests pass (80% success rate)  
- ? Only 2 tests fail (KCBox and KCBoxFlat) - **isolated problem**
- The algorithm is proven to work for simple and moderate complexity shapes
- We have a known-good baseline

**Why NOT HelixProcess?**
- Implementation swap mid-analysis was a mistake
- We don't know which tests it actually passes/fails
- Starting over with a new implementation is the wrong approach

## The 8-Step Plan (from testrun_stp.bat)

```
1. Discover shortest line segment between vertices on different faces
2. Discover 3D rotation based on angle between those vertices
3. Apply transform to rotate so thin segment is along Z axis
4. Pick topmost face along Z axis
5. Apply transform to rotate so 1 edge aligns with X axis
6. PROJECT TO 2D (X, Y only after rotation/normalization) ? KEY STEP
7. RECONSTRUCT PERIMETER ORDER IN 2D
8. Output to SVG
```

## Known Issues from Previous Logs

### From the all_results.txt (StepProcess output for KCBox):

**Step 6 Output:**
```
[STEP 6] 3D ranges - X:[-5.8,43.5] Y:[-55.1,7.9] Z:[8.1,36.9]
[STEP 6] 2D ranges - X:[-5.8,43.5] (extent=49.3) Y:[-55.1,7.9] (extent=62.9)
```

**Problem:** The Z dimension has large extent [8.1, 36.9] = 28.8mm

This means **Step 5 failed** - the rotation did NOT properly align the geometry with the XY plane. The Z extent should be ~3mm (the thin dimension), not 28.8mm.

## What We Need to Debug

### Immediate Focus: Step 5 Rotation

The code in StepProcess has:
```csharp
// For complex shapes, skip the edge-alignment normalization (rotMatrix2)
// since the thin-face vertex set may not have enough top vertices for good alignment
if (faces.Count > 20)
{
    return rot1;  // Skip rotMatrix2!
}
var rot2 = ApplyMatrix(rot1, rotMatrix2);
return rot2;
```

**This is the bug.** For KCBox (44 faces):
- Step 3 applies `rotMatrix1` (aligns thin dimension to Z)
- Step 5 SKIPS `rotMatrix2` (would align edge to X)
- **Result:** Geometry is NOT properly in the XY plane after Step 5

### What We Need to Check

1. **Is rotMatrix1 working?** Does it put the thin dimension along Z?
2. **Is rotMatrix2 being computed correctly?**  Is it even needed for complex shapes?
3. **Should we apply rotMatrix2 even for complex shapes?** (Current code skips it)

## Test Strategy

1. Run KCBox test with StepProcess
2. Examine the logged ranges to see if Z extent is ~3mm or something large
3. If Z extent is large, Step 5 failed
4. Debug which rotation matrix is the problem
5. Add logging to see intermediate matrices and coordinates

## Success Criteria

When Step 6 works correctly for KCBox, we should see:
```
[STEP 6] 3D ranges - X:[-5.8,43.5] Y:[-55.1,7.9] Z:[~3.0,~3.0] or Z:[0,~3.0]
```

The Z extent should be close to the thin dimension (~3mm), not 28.8mm.
