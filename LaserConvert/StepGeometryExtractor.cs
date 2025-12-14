using System;
using System.Collections.Generic;
using System.Linq;
using IxMilia.Step;
using IxMilia.Step.Items;

namespace LaserConvert
{
    /// <summary>
    /// Extracts solids from STEP files and traverses B-rep topology to get geometry.
    /// </summary>
    internal static class StepTopologyResolver
    {
        private static bool _debugMode = false;
        
        private static void DebugLog(string message)
        {
            if (_debugMode)
            {
                Console.WriteLine(message);
            }
        }

        /// <summary>
        /// Find all B-rep solid entities in the file by searching all items for solid types.
        /// </summary>
        public static List<(string Name, List<StepAdvancedFace> Faces)> GetAllSolids(StepFile stepFile)
        {
            var solids = new List<(string, List<StepAdvancedFace>)>();
            
            // First, try to find actual MANIFOLD_SOLID_BREP entities
            var allItems = stepFile.Items.ToList();
            var foundManifoldSolids = false;
            
            foreach (var item in allItems)
            {
                var typeName = item.GetType().Name;
                
                // Look for ManifoldSolidBrep type
                if (typeName.Equals("StepManifoldSolidBrep", StringComparison.OrdinalIgnoreCase))
                {
                    var name = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : $"Solid_{solids.Count}";
                    
                    // Try to extract the shell/faces from this solid
                    var faces = ExtractFacesFromSolid(item, stepFile);
                    
                    if (faces.Count > 0)
                    {
                        solids.Add((name, faces));
                        foundManifoldSolids = true;
                    }
                }
            }
            
            // If we found MANIFOLD_SOLID_BREP entities, use them
            if (foundManifoldSolids)
            {
                return solids;
            }
            
            // Fallback: create pseudo-solids by grouping faces
            var faces_list = stepFile.Items.OfType<StepAdvancedFace>().ToList();
            if (faces_list.Count > 0)
            {
                DebugLog($"[SOLID] No MANIFOLD_SOLID_BREP found. Creating pseudo-solids from {faces_list.Count} faces.");
                
                // Assume 6 faces per solid
                int facesPerSolid = 6;
                int solidCount = (faces_list.Count + facesPerSolid - 1) / facesPerSolid;
                
                for (int i = 0; i < solidCount; i++)
                {
                    int startIdx = i * facesPerSolid;
                    int endIdx = Math.Min(startIdx + facesPerSolid, faces_list.Count);
                    var solidFaces = faces_list.GetRange(startIdx, endIdx - startIdx);
                    var solidName = $"Solid{i + 1}";
                    
                    solids.Add((solidName, solidFaces));
                }
            }
            
            return solids;
        }

        /// <summary>
        /// Extract faces from a MANIFOLD_SOLID_BREP by traversing its shell.
        /// </summary>
        private static List<StepAdvancedFace> ExtractFacesFromSolid(StepRepresentationItem solid, StepFile stepFile)
        {
            var faces = new List<StepAdvancedFace>();
            
            // Try to get the shell property using reflection
            var shellProp = solid.GetType().GetProperties()
                .FirstOrDefault(p => p.PropertyType.Name.Contains("Shell"));
            
            if (shellProp != null)
            {
                var shell = shellProp.GetValue(solid);
                if (shell != null)
                {
                    // Try to get the faces list from the shell
                    var facesProp = shell.GetType().GetProperties()
                        .FirstOrDefault(p => p.PropertyType.Name.Contains("List") && p.Name.Contains("Face"));
                    
                    if (facesProp != null)
                    {
                        var facesList = facesProp.GetValue(shell) as System.Collections.IList;
                        if (facesList != null)
                        {
                            foreach (var face in facesList.OfType<StepAdvancedFace>())
                            {
                                faces.Add(face);
                            }
                        }
                    }
                }
            }
            
            return faces;
        }

        /// <summary>
        /// Extract vertices from a list of faces for dimension calculation.
        /// Also returns information about the thin face pair for SVG projection and adjusted dimensions.
        /// </summary>
        public static (List<(double X, double Y, double Z)> DimensionVertices, int ThinFace1Idx, int ThinFace2Idx, double DimX, double DimY, double DimZ) ExtractVerticesAndFaceIndices(
            List<StepAdvancedFace> faces,
            StepFile stepFile,
            bool debugMode = false)
        {
            _debugMode = debugMode;
            
            var allVertices = new List<(double, double, double)>();
            
            DebugLog($"[TOPO] Processing {faces.Count} faces");
            
            // For each face, extract its vertices
            var faceData = new List<(StepAdvancedFace face, List<(double X, double Y, double Z)> vertices)>();
            
            foreach (var face in faces)
            {
                var faceVertices = ExtractVerticesFromFace(face, stepFile);
                if (faceVertices.Count > 0)
                {
                    faceData.Add((face, faceVertices));
                }
            }
            
            // Initialize tracking variables for thin face pair
            double minSeparation = double.MaxValue;
            int face1Idx = -1, face2Idx = -1;
            
            // Find the PAIR of faces with the smallest centroid-to-centroid distance
            // BUT: ensure the separation direction aligns with one of the dimension axes
            
            // Strategy: Find ALL face pairs, then pick the one that:
            // 1. Has separation close to a reasonable "thin" dimension (2.5-5mm range)
            // 2. Is well-aligned along one axis
            // If that fails, pick the closest pair overall
            
            var candidates = new List<(int i, int j, double sep, double alignment)>();
            
            for (int i = 0; i < faceData.Count; i++)
            {
                for (int j = i + 1; j < faceData.Count; j++)
                {
                    var fi = faceData[i].vertices;
                    var fj = faceData[j].vertices;
                    
                    var (sep, dx, dy, dz) = ComputeFacePairSeparationWithDirection(fi, fj);
                    var maxComponent = Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Abs(dz));
                    var alignment = maxComponent / (sep + 1e-6);
                    
                    candidates.Add((i, j, sep, alignment));
                }
            }
            
            // Prefer pairs with "thin" separation (2.5-5.0mm) and good alignment
            var thinCandidates = candidates.Where(c => c.sep >= 2.0 && c.sep <= 8.0 && c.alignment > 0.85).OrderBy(c => c.sep).ToList();
            
            if (thinCandidates.Count > 0)
            {
                // Pick the CLOSEST well-aligned pair (most likely to be the thin face)
                var best = thinCandidates.First();
                minSeparation = best.sep;
                face1Idx = best.i;
                face2Idx = best.j;
            }
            else if (candidates.Count > 0)
            {
                // Fallback: pick the closest pair with good alignment
                var alignedCandidates = candidates.Where(c => c.alignment > 0.80).OrderBy(c => c.sep).ToList();
                if (alignedCandidates.Count > 0)
                {
                    var best = alignedCandidates.First();
                    minSeparation = best.sep;
                    face1Idx = best.i;
                    face2Idx = best.j;
                }
            }
            
            // For complex shapes with many faces (>20), use all vertices
            // This ensures we get the full geometry including tabs and holes
            bool isComplexShape = faces.Count > 20;
            
            if (isComplexShape)
            {
                DebugLog($"[TOPO] Complex shape detected ({faces.Count} faces), extracting ALL vertices from all face bounds");
                // For complex shapes, extract ALL bound vertices (not just outer loops)
                // because tabs/cutouts are represented by intermediate faces with multiple bounds
                foreach (var (face, faceVerts) in faceData)
                {
                    if (face?.Bounds != null)
                    {
                        // Extract from ALL bounds (outer + holes), not just outer loop
                        foreach (var bound in face.Bounds)
                        {
                            var boundVerts = ExtractVerticesFromBound(bound, stepFile);
                            if (boundVerts.Count > 0)
                            {
                                allVertices.AddRange(boundVerts);
                            }
                        }
                    }
                }
            }
            else
            {
                if (face1Idx >= 0 && face2Idx >= 0 && minSeparation < 200.0)
                {
                    DebugLog($"[TOPO] Found pair of faces with separation: {minSeparation:F1}mm");
                    
                    // For simple shapes, use ALL vertices from the two closest faces (includes holes)
                    allVertices.AddRange(faceData[face1Idx].vertices);
                    allVertices.AddRange(faceData[face2Idx].vertices);
                }
                else
                {
                    // Fallback: use all vertices
                    DebugLog($"[TOPO] No close face pair found, using all vertices");
                    foreach (var (face, faceVerts) in faceData)
                    {
                        allVertices.AddRange(faceVerts);
                    }
                }
            }
            
            // Deduplicate - keep vertices from both faces
            var uniqueVertices = allVertices
                .GroupBy(v => (Math.Round(v.Item1, 2), Math.Round(v.Item2, 2), Math.Round(v.Item3, 2)))
                .Select(g => g.First())
                .ToList();
            
            DebugLog($"[TOPO] Extracted {uniqueVertices.Count} unique vertices");
            
            // Debug: print vertex ranges for dimension calculation
            var minX = 0.0;
            var maxX = 0.0;
            var minY = 0.0;
            var maxY = 0.0;
            var minZ = 0.0;
            var maxZ = 0.0;
            var dimX = 0.0;
            var dimY = 0.0;
            var dimZ = 0.0;
            
            if (uniqueVertices.Count > 0)
            {
                minX = uniqueVertices.Min(v => v.Item1);
                maxX = uniqueVertices.Max(v => v.Item1);
                minY = uniqueVertices.Min(v => v.Item2);
                maxY = uniqueVertices.Max(v => v.Item2);
                minZ = uniqueVertices.Min(v => v.Item3);
                maxZ = uniqueVertices.Max(v => v.Item3);
                dimX = maxX - minX;
                dimY = maxY - minY;
                dimZ = maxZ - minZ;
                DebugLog($"[TOPO] Vertex ranges: X[{minX:F1},{maxX:F1}] Y[{minY:F1},{maxY:F1}] Z[{minZ:F1},{maxZ:F1}]");
                DebugLog($"[TOPO] Computed dimensions: {dimX:F1} x {dimY:F1} x {dimZ:F1}");
            }
            
            // Sanity check: if we found a small separation but computed a large Z dimension,
            // the faces might not be what we think they are
            if (minSeparation < 10.0 && dimZ > 50.0)
            {
                DebugLog($"[TOPO] WARNING: Small face separation ({minSeparation:F1}mm) but large Z dimension ({dimZ:F1}mm) - faces may not be parallel!");
                DebugLog($"[TOPO] This suggests the face pair is not the actual thin pair. Using all vertices instead.");
                
                // Fallback: re-compute dimensions using ALL faces
                allVertices.Clear();
                foreach (var (face, faceVerts) in faceData)
                {
                    allVertices.AddRange(faceVerts);
                }
                
                uniqueVertices = allVertices
                    .GroupBy(v => (Math.Round(v.Item1, 2), Math.Round(v.Item2, 2), Math.Round(v.Item3, 2)))
                    .Select(g => g.First())
                    .ToList();
                
                // DON'T reset face indices - keep them for SVG projection!
                // face1Idx = -1;
                // face2Idx = -1;
                
                // Recompute dimensions
                if (uniqueVertices.Count > 0)
                {
                    minX = uniqueVertices.Min(v => v.Item1);
                    maxX = uniqueVertices.Max(v => v.Item1);
                    minY = uniqueVertices.Min(v => v.Item2);
                    maxY = uniqueVertices.Max(v => v.Item2);
                    minZ = uniqueVertices.Min(v => v.Item3);
                    maxZ = uniqueVertices.Max(v => v.Item3);
                    dimX = maxX - minX;
                    dimY = maxY - minY;
                    dimZ = maxZ - minZ;
                    DebugLog($"[TOPO] Recomputed with all faces: {dimX:F1} x {dimY:F1} x {dimZ:F1}");
                }
            }
            
            // CRITICAL FIX: If we found a face pair with separation in reasonable range (2.5-10.0mm),
            // consider that as the detected thin dimension regardless of vertex bounding box
            // This handles rotated geometry where the thin faces aren't axis-aligned
            if (minSeparation >= 2.0 && minSeparation <= 10.0)  // Increased to 10mm for rotated geometry tolerance
            {
                // The detected separation IS the thin dimension
                // Replace the smallest bounding dimension with it
                var dims = new[] { dimX, dimY, dimZ }.OrderBy(d => d).ToList();
                dims[0] = minSeparation;  // The thin dimension
                dimX = dims[0];
                dimY = dims[1];
                dimZ = dims[2];
                DebugLog($"[TOPO] Thin dimension detected from face separation: {minSeparation:F1}mm");
                DebugLog($"[TOPO] Adjusted dimensions: {dimX:F1} x {dimY:F1} x {dimZ:F1}");
            }
            
            return (uniqueVertices, face1Idx, face2Idx, dimX, dimY, dimZ);
        }

        /// <summary>
        /// Extract vertices from a list of faces (legacy interface for dimension calculation).
        /// </summary>
        public static List<(double X, double Y, double Z)> ExtractVerticesFromFaces(
            List<StepAdvancedFace> faces,
            StepFile stepFile)
        {
            var (vertices, _, _, _, _, _) = ExtractVerticesAndFaceIndices(faces, stepFile);
            return vertices;
        }

        /// <summary>
        /// Get the adjusted dimensions for a solid (returns the three dimensions with thin dimension adjusted).
        /// </summary>
        public static (double DimX, double DimY, double DimZ) GetAdjustedDimensions(
            List<(double X, double Y, double Z)> vertices)
        {
            // Re-compute using the same logic as ExtractVerticesAndFaceIndices
            // This is a simplified version that just returns dimensions without full face analysis
            var minX = vertices.Min(v => v.X);
            var maxX = vertices.Max(v => v.X);
            var minY = vertices.Min(v => v.Y);
            var maxY = vertices.Max(v => v.Y);
            var minZ = vertices.Min(v => v.Z);
            var maxZ = vertices.Max(v => v.Z);
            
            return (maxX - minX, maxY - minY, maxZ - minZ);
        }

        /// <summary>
        /// Compute the distance between two sets of vertices and return the separation vector.
        /// </summary>
        private static double ComputeFacePairSeparation(
            List<(double X, double Y, double Z)> face1,
            List<(double X, double Y, double Z)> face2)
        {
            if (face1.Count == 0 || face2.Count == 0)
                return double.MaxValue;
            
            // Compute centroid of each face
            var c1x = face1.Average(v => v.X);
            var c1y = face1.Average(v => v.Y);
            var c1z = face1.Average(v => v.Z);
            
            var c2x = face2.Average(v => v.X);
            var c2y = face2.Average(v => v.Y);
            var c2z = face2.Average(v => v.Z);
            
            // Distance between centroids
            var dx = c2x - c1x;
            var dy = c2y - c1y;
            var dz = c2z - c1z;
            
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Compute the distance between two sets of vertices and return the separation vector.
        /// </summary>
        private static (double Distance, double DX, double DY, double DZ) ComputeFacePairSeparationWithDirection(
            List<(double X, double Y, double Z)> face1,
            List<(double X, double Y, double Z)> face2)
        {
            if (face1.Count == 0 || face2.Count == 0)
                return (double.MaxValue, 0, 0, 0);
            
            // Compute centroid of each face
            var c1x = face1.Average(v => v.X);
            var c1y = face1.Average(v => v.Y);
            var c1z = face1.Average(v => v.Z);
            
            var c2x = face2.Average(v => v.X);
            var c2y = face2.Average(v => v.Y);
            var c2z = face2.Average(v => v.Z);
            
            // Vector from face1 centroid to face2 centroid
            var dx = c2x - c1x;
            var dy = c2y - c1y;
            var dz = c2z - c1z;
            
            var dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return (dist, dx, dy, dz);
        }

        private static void ExtractVerticesFromFace(
            StepAdvancedFace face,
            StepFile stepFile,
            List<(double X, double Y, double Z)> vertices,
            HashSet<string> processedPoints)
        {
            if (face?.Bounds == null)
                return;
            
            foreach (var bound in face.Bounds)
            {
                ExtractVerticesFromFaceBound(bound, stepFile, vertices, processedPoints);
            }
        }

        /// <summary>
        /// Extract vertices from a face (public method for external use).
        /// </summary>
        public static List<(double X, double Y, double Z)> ExtractVerticesFromFace(
            StepAdvancedFace face,
            StepFile stepFile)
        {
            var vertices = new List<(double, double, double)>();
            var processedPoints = new HashSet<string>();
            
            ExtractVerticesFromFace(face, stepFile, vertices, processedPoints);
            return vertices;
        }

        private static void ExtractVerticesFromFaceBound(
            StepFaceBound bound,
            StepFile stepFile,
            List<(double X, double Y, double Z)> vertices,
            HashSet<string> processedPoints)
        {
            var edgeLoop = GetEdgeLoopFromBound(bound);
            
            if (edgeLoop?.EdgeList != null)
            {
                foreach (var orientedEdge in edgeLoop.EdgeList)
                {
                    ExtractVerticesFromOrientedEdge(orientedEdge, stepFile, vertices, processedPoints);
                }
            }
        }

        private static StepEdgeLoop GetEdgeLoopFromBound(StepFaceBound bound)
        {
            // The property is likely called "Loop" or similar - use reflection to find it
            var props = bound.GetType().GetProperties();
            foreach (var prop in props)
            {
                if (prop.PropertyType == typeof(StepEdgeLoop) || 
                    prop.PropertyType.Name.Contains("Loop"))
                {
                    return prop.GetValue(bound) as StepEdgeLoop;
                }
            }
            return null;
        }

        private static void ExtractVerticesFromOrientedEdge(
            StepOrientedEdge orientedEdge,
            StepFile stepFile,
            List<(double X, double Y, double Z)> vertices,
            HashSet<string> processedPoints)
        {
            if (orientedEdge?.EdgeElement == null)
                return;
            
            var edgeCurve = orientedEdge.EdgeElement;
            
            // Extract vertices
            if (edgeCurve.EdgeStart is StepVertexPoint startVertex)
            {
                AddVertex(startVertex, vertices, processedPoints);
            }
            if (edgeCurve.EdgeEnd is StepVertexPoint endVertex)
            {
                AddVertex(endVertex, vertices, processedPoints);
            }
        }

        private static void AddVertex(
            StepVertexPoint vertex,
            List<(double X, double Y, double Z)> vertices,
            HashSet<string> processedPoints)
        {
            if (vertex?.Location != null)
            {
                var pt = vertex.Location;
                var key = $"{pt.X:F6},{pt.Y:F6},{pt.Z:F6}";
                if (!processedPoints.Contains(key))
                {
                    vertices.Add((pt.X, pt.Y, pt.Z));
                    processedPoints.Add(key);
                }
            }
        }

        /// <summary>
        /// Extract vertices from a single face's OUTER LOOP ONLY.
        /// This is the correct way to handle B-rep solids with holes:
        /// the first bound is the outer loop, subsequent bounds are inner loops (holes).
        /// </summary>
        public static List<(double X, double Y, double Z)> ExtractOuterLoopVerticesFromFace(
            StepAdvancedFace face,
            StepFile stepFile)
        {
            var vertices = new List<(double, double, double)>();
            
            if (face?.Bounds == null || face.Bounds.Count == 0)
                return vertices;
            
            // Only extract from the FIRST bound - the outer loop
            // Subsequent bounds are inner loops (holes) and should not be used for boundary tracing
            var outerBound = face.Bounds[0];
            var processedPoints = new HashSet<string>();
            
            ExtractVerticesFromFaceBound(outerBound, stepFile, vertices, processedPoints);
            return vertices;
        }
        
        /// <summary>
        /// Extract vertices from all bounds of a face (outer + inner loops).
        /// This includes holes. Used when we want to detect holes separately.
        /// Handles STEP files that may have bounds in any order (not always outer first).
        /// </summary>
        public static (List<(double X, double Y, double Z)> OuterVertices, List<List<(double X, double Y, double Z)>> HoleVertices) ExtractFaceWithHoles(
            StepAdvancedFace face,
            StepFile stepFile,
            bool debugMode = false)
        {
            _debugMode = debugMode;
            
            var outerVerts = new List<(double, double, double)>();
            var holeVerts = new List<List<(double, double, double)>>();
            
            if (face?.Bounds == null || face.Bounds.Count == 0)
                return (outerVerts, holeVerts);
            
            DebugLog($"[EXTRACT] ExtractFaceWithHoles: face has {face.Bounds.Count} bounds");
            
            // Extract all loops IN EDGE ORDER
            var allLoops = new List<(int Index, List<(double, double, double)> Vertices, double BoundingBoxArea)>();
            
            for (int i = 0; i < face.Bounds.Count; i++)
            {
                var bound = face.Bounds[i];
                // Use new method that preserves edge order
                var verts = ExtractVerticesInEdgeOrder(bound, stepFile);
                
                if (verts.Count > 0)
                {
                    var minX = verts.Min(v => v.Item1);
                    var maxX = verts.Max(v => v.Item1);
                    var minY = verts.Min(v => v.Item2);
                    var maxY = verts.Max(v => v.Item2);
                    var minZ = verts.Min(v => v.Item3);
                    var maxZ = verts.Max(v => v.Item3);
                    var width = maxX - minX;
                    var height = maxY - minY;
                    var depth = maxZ - minZ;
                    
                    var area = width * height + height * depth + depth * width;
                    
                    DebugLog($"[EXTRACT] Bound {i} has {verts.Count} vertices, bbox {width:F1}x{height:F1}x{depth:F1} (area={area:F1}): {string.Join(", ", verts.Take(4).Select(v => $"({v.Item1:F1},{v.Item2:F1},{v.Item3:F1})"))}...");
                    
                    allLoops.Add((i, verts, area));
                }
            }
            
            // The OUTER loop should have the LARGEST bounding box area
            if (allLoops.Count > 0)
            {
                allLoops = allLoops.OrderByDescending(l => l.BoundingBoxArea).ToList();
                
                outerVerts = allLoops[0].Vertices;
                DebugLog($"[EXTRACT] Identified bound {allLoops[0].Index} as outer loop (largest area={allLoops[0].BoundingBoxArea:F1})");
                
                for (int i = 1; i < allLoops.Count; i++)
                {
                    holeVerts.Add(allLoops[i].Vertices);
                    DebugLog($"[EXTRACT] Identified bound {allLoops[i].Index} as hole loop {i} (area={allLoops[i].BoundingBoxArea:F1})");
                }
            }
            
            DebugLog($"[EXTRACT] Returning {outerVerts.Count} outer verts and {holeVerts.Count} hole loops");
            return (outerVerts, holeVerts);
        }

        /// <summary>
        /// Extract vertices from a face bound IN EDGE ORDER.
        /// This preserves the traversal order from the STEP file, which is essential
        /// for correctly tracing the perimeter of complex shapes.
        /// </summary>
        public static List<(double X, double Y, double Z)> ExtractVerticesInEdgeOrder(
            StepFaceBound bound,
            StepFile stepFile)
        {
            var vertices = new List<(double, double, double)>();
            
            var edgeLoop = GetEdgeLoopFromBound(bound);
            if (edgeLoop?.EdgeList == null)
                return vertices;
            
            // Walk through edges in order, taking only the START vertex of each edge.
            // The end vertex of edge N should be the start vertex of edge N+1 (forming a chain).
            // This gives us exactly one vertex per edge in the correct order.
            foreach (var orientedEdge in edgeLoop.EdgeList)
            {
                if (orientedEdge?.EdgeElement == null)
                    continue;
                
                var edgeCurve = orientedEdge.EdgeElement;
                
                // Use EdgeStart for forward-oriented edges, EdgeEnd for reversed edges
                StepVertexPoint vertex;
                if (orientedEdge.Orientation)
                {
                    vertex = edgeCurve.EdgeStart as StepVertexPoint;
                }
                else
                {
                    vertex = edgeCurve.EdgeEnd as StepVertexPoint;
                }
                
                if (vertex?.Location != null)
                {
                    var pt = vertex.Location;
                    vertices.Add((pt.X, pt.Y, pt.Z));
                }
            }
            
            return vertices;
        }

        /// <summary>
        /// Extract vertices specifically from a single face bound (for extracting inner loops).
        /// Public method that creates its own HashSet.
        /// </summary>
        public static List<(double X, double Y, double Z)> ExtractVerticesFromBound(
            StepFaceBound bound,
            StepFile stepFile)
        {
            var vertices = new List<(double, double, double)>();
            var processedPoints = new HashSet<string>();
            ExtractVerticesFromFaceBound(bound, stepFile, vertices, processedPoints);
            return vertices;
        }

        /// <summary>
        /// Extract UNIQUE corner vertices from all bounds (not traversing edge order).
        /// This gives us the actual geometric corners of the face, even for degenerate flat faces.
        /// </summary>
        public static (List<(double X, double Y, double Z)> OuterVertices, List<List<(double X, double Y, double Z)>> HoleVertices) ExtractFaceCornerVertices(
            StepAdvancedFace face,
            StepFile stepFile)
        {
            var allBoundVertices = new List<(int BoundIndex, List<(double, double, double)> Vertices)>();
            
            if (face?.Bounds == null || face.Bounds.Count == 0)
                return (new List<(double, double, double)>(), new List<List<(double, double, double)>>());
            
            // Extract unique vertices from each bound (without following edge order)
            for (int boundIdx = 0; boundIdx < face.Bounds.Count; boundIdx++)
            {
                var bound = face.Bounds[boundIdx];
                var uniqueCornerVertices = new Dictionary<string, (double, double, double)>();
                
                // Traverse edge loop and collect END POINTS (corners), not intermediate edge points
                var edgeLoop = GetEdgeLoopFromBound(bound);
                if (edgeLoop?.EdgeList != null)
                {
                    foreach (var orientedEdge in edgeLoop.EdgeList)
                    {
                        if (orientedEdge?.EdgeElement?.EdgeStart is StepVertexPoint startVertex && startVertex.Location != null)
                        {
                            var pt = startVertex.Location;
                            var key = $"{pt.X:F4},{pt.Y:F4},{pt.Z:F4}";
                            if (!uniqueCornerVertices.ContainsKey(key))
                            {
                                uniqueCornerVertices[key] = (pt.X, pt.Y, pt.Z);
                            }
                        }
                    }
                }
                
                allBoundVertices.Add((boundIdx, uniqueCornerVertices.Values.ToList()));
            }
            
            // Classify bounds by size
            var boundsBySize = allBoundVertices
                .Select(b => (
                    Index: b.BoundIndex,
                    Vertices: b.Vertices,
                    Area: b.Vertices.Count > 0 ? ComputeConvexHullArea(b.Vertices) : 0.0
                ))
                .OrderByDescending(b => b.Area)
                .ToList();
            
            if (boundsBySize.Count == 0)
                return (new List<(double, double, double)>(), new List<List<(double, double, double)>>());
            
            // Largest is outer, rest are holes
            var outer = boundsBySize[0].Vertices;
            var holes = boundsBySize.Skip(1).Select(b => b.Vertices).ToList();
            
            return (outer, holes);
        }
        
        private static double ComputeConvexHullArea(List<(double X, double Y, double Z)> verts)
        {
            if (verts.Count < 3) return 0;
            
            // Project onto best-fit plane and compute area
            // For now, use simple bounding box diagonal as proxy
            var minX = verts.Min(v => v.X);
            var maxX = verts.Max(v => v.X);
            var minY = verts.Min(v => v.Y);
            var maxY = verts.Max(v => v.Y);
            var minZ = verts.Min(v => v.Z);
            var maxZ = verts.Max(v => v.Z);
            
            var dx = maxX - minX;
            var dy = maxY - minY;
            var dz = maxZ - minZ;
            
            return dx * dy + dy * dz + dz * dx;
        }
    }
}
