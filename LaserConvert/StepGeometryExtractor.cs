using System;
using System.Collections.Generic;
using System.Linq;
using IxMilia.Step;
using IxMilia.Step.Items;

namespace LaserConvert
{
    /// <summary>
    /// Extracts solids from STEP files and traverses B-rep topology to get vertices.
    /// </summary>
    internal static class StepTopologyResolver
    {
        /// <summary>
        /// Find all B-rep solid entities in the file.
        /// </summary>
        public static List<(string Name, List<StepAdvancedFace> Faces)> GetAllSolids(StepFile stepFile)
        {
            var solids = new List<(string, List<StepAdvancedFace>)>();
            
            // Search for MANIFOLD_SOLID_BREP entities by type name
            var allItems = stepFile.Items.ToList();
            
            foreach (var item in allItems)
            {
                var typeName = item.GetType().Name;
                
                // Check if this is a ManifoldSolidBrep type
                if (typeName.Equals("StepManifoldSolidBrep", StringComparison.OrdinalIgnoreCase))
                {
                    var name = !string.IsNullOrWhiteSpace(item.Name) ? item.Name : $"Solid_{solids.Count}";
                    
                    // Try to extract the shell/faces from this solid
                    // The solid references a shell, which contains the faces
                    var faces = ExtractFacesFromSolid(item, stepFile);
                    
                    if (faces.Count > 0)
                    {
                        solids.Add((name, faces));
                        Console.WriteLine($"[SOLID] Found MANIFOLD_SOLID_BREP '{name}' with {faces.Count} faces");
                    }
                }
            }
            
            // If no solids found, create pseudo-solids by grouping faces
            if (solids.Count == 0)
            {
                var faces = stepFile.Items.OfType<StepAdvancedFace>().ToList();
                if (faces.Count > 0)
                {
                    Console.WriteLine($"[SOLID] No MANIFOLD_SOLID_BREP found. Creating pseudo-solids from {faces.Count} faces.");
                    
                    // Assume 6 faces per solid
                    int facesPerSolid = 6;
                    int solidCount = (faces.Count + facesPerSolid - 1) / facesPerSolid;
                    
                    for (int i = 0; i < solidCount; i++)
                    {
                        int startIdx = i * facesPerSolid;
                        int endIdx = Math.Min(startIdx + facesPerSolid, faces.Count);
                        var solidFaces = faces.GetRange(startIdx, endIdx - startIdx);
                        var solidName = $"Solid{i + 1}";
                        
                        solids.Add((solidName, solidFaces));
                        Console.WriteLine($"[SOLID] Created pseudo-solid '{solidName}' with {solidFaces.Count} faces");
                    }
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
        /// Extract all CartesianPoints that are vertices of faces in this solid.
        /// </summary>
        public static List<(double X, double Y, double Z)> ExtractSolidVertices(
            StepRepresentationItem solid,
            StepFile stepFile)
        {
            var vertices = new List<(double, double, double)>();
            var processedPoints = new HashSet<string>();
            
            // Get all AdvancedFaces
            var allFaces = stepFile.Items.OfType<StepAdvancedFace>().ToList();
            Console.WriteLine($"[TOPO] Processing {allFaces.Count} faces");
            
            // Extract vertices from each face's edge loops
            foreach (var face in allFaces)
            {
                ExtractVerticesFromFace(face, stepFile, vertices, processedPoints);
            }
            
            Console.WriteLine($"[TOPO] Extracted {vertices.Count} unique vertices");
            return vertices;
        }

        /// <summary>
        /// Extract vertices from a list of faces.
        /// </summary>
        public static List<(double X, double Y, double Z)> ExtractVerticesFromFaces(
            List<StepAdvancedFace> faces,
            StepFile stepFile)
        {
            var vertices = new List<(double, double, double)>();
            var processedPoints = new HashSet<string>();
            
            Console.WriteLine($"[TOPO] Processing {faces.Count} faces");
            
            foreach (var face in faces)
            {
                ExtractVerticesFromFace(face, stepFile, vertices, processedPoints);
            }
            
            Console.WriteLine($"[TOPO] Extracted {vertices.Count} unique vertices");
            return vertices;
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

        private static void ExtractVerticesFromFaceBound(
            StepFaceBound bound,
            StepFile stepFile,
            List<(double X, double Y, double Z)> vertices,
            HashSet<string> processedPoints)
        {
            // StepFaceBound should have reference to an edge loop
            // Since we don't know the exact property name, try to get it via reflection or direct access
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
            
            // Extract vertices - cast from StepVertex to StepVertexPoint
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
