# FINAL ALGORITHM DECISION

## PROVEN FAILURES

1. **Monotone Chain with vertex removal** - Removes vertices, loses geometry
2. **Polar angle from corner** - Wrong order for non-convex shapes
3. **Polar angle from centroid** - Creates backtracking jumps

## THE CORE PROBLEM

The vertices come from STEP face bounds, which traverse edges in topological order, NOT perimeter order.

For a box with 4 corners, STEP might store them as:
- Edge loop 1: A ? B ? C ? D ? A  (clockwise around perimeter)
- OR as: A ? C ? B ? D ? A (some other order based on how the file was created)

We can't assume any particular order.

## THE ONLY GUARANTEED SOLUTION

**Use edge connectivity information from the STEP file itself.**

But we don't currently have direct edge-to-vertex mapping in our extraction.

## PRACTICAL ALTERNATIVE FOR CURRENT CODEBASE

Since we're extracting from bounds (edge loops), and the bounds represent the actual perimeter, we should:

1. **Build a connectivity graph** from the extracted vertices
2. Find which vertices are actually adjacent (connected by edges)
3. **Start at a known corner** and walk the edges sequentially
4. **Preserve all vertices** automatically by following edges

## IMPLEMENTATION APPROACH

Add this to StepHelpers:

```csharp
public static List<(long, long)> OrderPolygonPerimeterByEdgeWalking(
    List<(long, long)> vertices, 
    double tolerance = 1.0)
{
    if (vertices.Count <= 3)
        return new List<(long, long)>(vertices);

    // Build proximity graph: which vertices are adjacent?
    var adjacency = new Dictionary<int, List<int>>();
    for (int i = 0; i < vertices.Count; i++)
    {
        adjacency[i] = new List<int>();
        for (int j = i + 1; j < vertices.Count; j++)
        {
            double dist = Math.Sqrt(
                Math.Pow(vertices[i].Item1 - vertices[j].Item1, 2) +
                Math.Pow(vertices[i].Item2 - vertices[j].Item2, 2));
            
            if (dist < tolerance)
            {
                // Vertices are close enough to be adjacent
                adjacency[i].Add(j);
                if (!adjacency.ContainsKey(j))
                    adjacency[j] = new List<int>();
                adjacency[j].Add(i);
            }
        }
    }

    // Find starting vertex (leftmost-lowest)
    int start = 0;
    for (int i = 1; i < vertices.Count; i++)
    {
        if (vertices[i].Item1 < vertices[start].Item1 ||
            (vertices[i].Item1 == vertices[start].Item1 && 
             vertices[i].Item2 < vertices[start].Item2))
        {
            start = i;
        }
    }

    // Walk the perimeter following edges
    var result = new List<(long, long)>();
    var visited = new HashSet<int>();
    int current = start;
    
    while (visited.Count < vertices.Count)
    {
        result.Add(vertices[current]);
        visited.Add(current);
        
        // Find next unvisited adjacent vertex
        int next = -1;
        foreach (int neighbor in adjacency[current])
        {
            if (!visited.Contains(neighbor))
            {
                next = neighbor;
                break;
            }
        }
        
        if (next == -1)
            break;  // No unvisited neighbors (shouldn't happen for valid polygon)
        
        current = next;
    }
    
    return result;
}
```

However, this approach has issues:
- Tolerance picking is arbitrary
- Doesn't work well for slightly non-perpendicular edges
- Requires correct proximity to identify edges

## ACTUAL BEST SOLUTION

The vertices ARE in the right order in the dedup list - they just need a **specific reordering** that respects their actual connectivity.

Looking at Box1 again:
- Dedup: (0,0) ? (155,0) ? (0,150) ? (155,150)
- Expected: (0,0) ? (155,0) ? (155,150) ? (0,150)

The issue is that index 2 and 3 are swapped. Why? 

In 3D:
- [2] = (-77.9, -117.6, 150.0) ? top-left Z=150
- [3] = (76.6, -46.7, 150.0) ? top-right Z=150

After normalization and projection, they become:
- [2] = (0, 150) 
- [3] = (155, 150)

But the edge loop traversed them in the wrong order for a perimeter walk.

**The issue is that edge loops don't guarantee perimeter order.**

If we could access the actual edge connectivity from the STEP file (e.g., "vertex A connects to vertex B"), we could order them correctly.

Without that, we're stuck with geometric algorithms, all of which have limitations.

## COMPROMISE SOLUTION

The best we can do without STEP edge information is **angle-based sorting with tie-breaking**:

1. Sort by angle from centroid
2. For vertices with similar angles (on same ray), sort by distance from centroid
3. This ensures vertices on the same edge are visited together

