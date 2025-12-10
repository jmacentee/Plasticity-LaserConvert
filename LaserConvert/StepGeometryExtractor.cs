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
        /// <summary>
        /// Find all B-rep solid entities in the file by searching all items for solid types.
        /// </summary>
        public static List<(string Name, List<StepAdvancedFace> Faces)> GetAllSolids(StepFile stepFile)
        {
            var solids = new List<(string, List<StepAdvancedFace>)>();
            
            Console.WriteLine($"[SOLID] Searching {stepFile.Items.Count} items for MANIFOLD_SOLID_BREP...");
            
            // First, try to find actual MANIFOLD_SOLID_BREP entities
            var allItems = stepFile.Items.ToList();
            var foundManifoldSolids = false;
            
            foreach (var item in allItems)
            {
                var typeName = item.GetType().Name;
                Console.WriteLine($"[SOLID] Checking type: {typeName}");
                
                // Look for any type that contains "ManifoldSolid" or "Brep"
                if (typeName.IndexOf("ManifoldSolid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (typeName.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0 && 
                     typeName.IndexOf("Brep", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var name = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : $"Solid_{solids.Count}";
                    
                    // Try to extract the shell/faces from this solid
                    var faces = ExtractFacesFromSolid(item, stepFile);
                    
                    if (faces.Count > 0)
                    {
                        solids.Add((name, faces));
                        Console.WriteLine($"[SOLID] ? Found {typeName} '{name}' with {faces.Count} faces");
                        foundManifoldSolids = true;
                    }
                }
            }
            
            // If we found MANIFOLD_SOLID_BREP entities, use them
            if (foundManifoldSolids)
            {
                Console.WriteLine($"[SOLID] Found {solids.Count} explicit MANIFOLD_SOLID_BREP entities");
                return solids;
            }
            
            // Fallback: create pseudo-solids by grouping faces
            var faces_list = stepFile.Items.OfType<StepAdvancedFace>().ToList();
            if (faces_list.Count > 0)
            {
                Console.WriteLine($"[SOLID] No MANIFOLD_SOLID_BREP found. Creating pseudo-solids from {faces_list.Count} faces.");
                
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
                    Console.WriteLine($"[SOLID] Created pseudo-solid '{solidName}' with {solidFaces.Count} faces");
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
        /// Extract vertices from a list of faces, finding the outline of the thin solid.
        /// For a thin box, find the pair of parallel faces separated by ~3mm and use their outline.
        /// </summary>
        public static List<(double X, double Y, double Z)> ExtractVerticesFromFaces(
            List<StepAdvancedFace> faces,
            StepFile stepFile)
        {
            var vertices = new List<(double, double, double)>();
            
            Console.WriteLine($"[TOPO] Processing {faces.Count} faces");
            
            // For each face, extract its vertices and compute its bounding box
            var faceData = new List<(StepAdvancedFace face, List<(double X, double Y, double Z)> vertices, double minZ, double maxZ)>();
            
            foreach (var face in faces)
            {
                var faceVertices = ExtractVerticesFromFace(face, stepFile);
                if (faceVertices.Count > 0)
                {
                    var minZ = faceVertices.Min(v => v.Item3);
                    var maxZ = faceVertices.Max(v => v.Item3);
                    faceData.Add((face, faceVertices, minZ, maxZ));
                }
            }
            
            // Find faces with minimal Z extent (planar faces perpendicular to Z axis)
            var planarFaces = faceData.Where(f => Math.Abs(f.maxZ - f.minZ) < 0.1).ToList();
            
            if (planarFaces.Count >= 2)
            {
                // Sort by Z coordinate
                planarFaces = planarFaces.OrderBy(f => f.minZ).ToList();
                
                // The thickness is the difference between the two planar faces
                var thickness = planarFaces[1].minZ - planarFaces[0].minZ;
                Console.WriteLine($"[TOPO] Found pair of parallel faces with Z separation: {thickness:F1}mm");
                
                // For dimensions: use all vertices from both parallel faces
                // This gives us the correct X, Y dimensions and the thickness in Z
                vertices.AddRange(planarFaces[0].vertices);
                vertices.AddRange(planarFaces[1].vertices);
            }
            else if (planarFaces.Count == 1)
            {
                // Only one planar face found, but we need to include all vertices for dimension calculation
                Console.WriteLine($"[TOPO] Found only one planar face, using all face vertices for dimensions");
                foreach (var (face, faceVerts, _, _) in faceData)
                {
                    vertices.AddRange(faceVerts);
                }
            }
            else
            {
                // No clear planar faces, use all vertices
                Console.WriteLine($"[TOPO] No clear parallel faces found, using all vertices");
                foreach (var (face, faceVerts, _, _) in faceData)
                {
                    vertices.AddRange(faceVerts);
                }
            }
            
            // Deduplicate
            var uniqueVertices = vertices
                .GroupBy(v => (Math.Round(v.Item1, 3), Math.Round(v.Item2, 3), Math.Round(v.Item3, 3)))
                .Select(g => g.First())
                .ToList();
            
            Console.WriteLine($"[TOPO] Extracted {uniqueVertices.Count} unique vertices");
            return uniqueVertices;
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

        private static List<(double X, double Y, double Z)> ExtractVerticesFromFace(
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
            // StepFaceBound should have reference to an edge loop
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
    }
}
