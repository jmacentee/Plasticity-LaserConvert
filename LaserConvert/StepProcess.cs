using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IxMilia.Step;
using IxMilia.Step.Items;

namespace LaserConvert
{
    /// <summary>
    /// StepProcess handles STEP file parsing and SVG generation for thin solids.
    /// 
    /// Algorithm (8-step plan):
    /// 1. Discover shortest line segment between vertices on different faces
    /// 2. Discover 3D rotation based on angle between those vertices
    /// 3. Apply transform to rotate so thin segment is along Z axis
    /// 4. Pick topmost face along Z axis
    /// 5. Apply transform to rotate so 1 edge aligns with X axis
    /// 6. Project to 2D (X, Y only after rotation/normalization)
    /// 7. Reconstruct perimeter order in 2D using computational geometry
    /// 8. Output to SVG
    /// </summary>
    internal static class StepProcess
    {
        public static int Main(string inputPath, string outputPath)
        {
            try
            {
                Console.WriteLine($"Loading STEP file: {inputPath}");
                var stepFile = StepFile.Load(inputPath);
                Console.WriteLine($"File loaded. Total items: {stepFile.Items.Count}");

                var solids = StepTopologyResolver.GetAllSolids(stepFile);
                Console.WriteLine($"Found {solids.Count} solids");
                if (solids.Count == 0)
                {
                    Console.WriteLine("No solids found in STEP file.");
                    return 0;
                }

                const double minThickness = 2.5;
                const double maxThickness = 10.0;

                var thinSolids = new List<(string Name, List<StepAdvancedFace> Faces)>();
                foreach (var (name, faces) in solids)
                {
                    var (vertices, _, _, dimX, dimY, dimZ) = StepTopologyResolver.ExtractVerticesAndFaceIndices(faces, stepFile);
                    var dimensions = new Dimensions(dimX, dimY, dimZ);
                    if (dimensions.HasThinDimension(minThickness, maxThickness))
                    {
                        thinSolids.Add((name, faces));
                        Console.WriteLine($"[FILTER] {name}: dimensions {dimensions} - PASS");
                    }
                    else
                    {
                        Console.WriteLine($"[FILTER] {name}: dimensions {dimensions} - FAIL");
                    }
                }

                if (thinSolids.Count == 0)
                {
                    Console.WriteLine("No thin solids found.");
                    return 0;
                }

                var svg = new SvgBuilder();
                foreach (var (name, faces) in thinSolids)
                {
                    svg.BeginGroup(name);

                    // Find the face with most boundary vertices - this is the main surface
                    StepAdvancedFace bestFace = null;
                    int maxBoundaryVerts = 0;
                    
                    foreach (var face in faces)
                    {
                        var (outerVerts, _) = StepTopologyResolver.ExtractFaceWithHoles(face, stepFile);
                        if (outerVerts.Count > maxBoundaryVerts)
                        {
                            maxBoundaryVerts = outerVerts.Count;
                            bestFace = face;
                        }
                    }
                    
                    if (bestFace != null && maxBoundaryVerts >= 3)
                    {
                        var (outerPerimeter, holePerimeters) = StepTopologyResolver.ExtractFaceWithHoles(bestFace, stepFile);
                        
                        // STEP 6: Project to 2D
                        var projected = StepHelpers.ProjectTo2D(outerPerimeter);
                        var normalized = StepHelpers.NormalizeAndRound(projected);
                        
                        // STEP 7: Remove only consecutive duplicates (preserves all boundary vertices)
                        var deduplicated = StepHelpers.RemoveConsecutiveDuplicates(normalized);
                        
                        if (deduplicated.Count >= 3)
                        {
                            // STEP 8: Build SVG path
                            var outerPath = SvgPathBuilder.BuildPath(deduplicated);
                            svg.Path(outerPath, 0.2, "none", "#000");
                            Console.WriteLine($"[SVG] {name}: Generated outline from {deduplicated.Count} vertices");
                            
                            // Handle holes
                            var outerMinX = projected.Min(p => p.X);
                            var outerMinY = projected.Min(p => p.Y);
                            
                            foreach (var holePeri in holePerimeters)
                            {
                                if (holePeri.Count >= 3)
                                {
                                    var projHole = StepHelpers.ProjectTo2D(holePeri);
                                    var normHole = StepHelpers.NormalizeAndRoundRelative(projHole, outerMinX, outerMinY);
                                    var dedupHole = StepHelpers.RemoveConsecutiveDuplicates(normHole);
                                    
                                    if (dedupHole.Count >= 3)
                                    {
                                        var holePath = SvgPathBuilder.BuildPath(dedupHole);
                                        svg.Path(holePath, 0.2, "none", "#f00");
                                    }
                                }
                            }
                        }
                    }
                    
                    svg.EndGroup();
                }

                File.WriteAllText(outputPath, svg.Build());
                Console.WriteLine($"Wrote SVG: {outputPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 2;
            }
        }
    }
}
