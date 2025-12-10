using System;
using System.Collections.Generic;
using System.Linq;
using IxMilia.Iges;
using IxMilia.Iges.Entities;

namespace LaserConvert
{
    /// <summary>
    /// Helper class for IGES-specific post-processing logic.
    /// Handles manifest-to-shell bindings after IxMilia has loaded all entities.
    /// </summary>
    internal static class IgesHelper
    {
        /// <summary>
        /// Resolve manifest-to-shell bindings after IxMilia has loaded all entities.
        /// </summary>
        public static void ResolveManifestShellBindings(IgesFile iges)
        {
            var manifests = iges.Entities.OfType<IgesManifestSolidBRepObject>().ToList();
            var shells = iges.Entities.OfType<IgesShell>().ToList();

            // Resolve shell face pointers using the entity line number map
            foreach (var shell in shells)
            {
                shell.ResolveFacePointers(iges.EntityLineNumberMap);
            }

            // Bind manifests to shells using sequential assignment based on entity load order
            for (int i = 0; i < manifests.Count && i < shells.Count; i++)
            {
                manifests[i].Shell = shells[i];
            }
        }

        /// <summary>
        /// Resolve Face.Loops by matching loop pointers to Loop entities.
        /// Also resolve Loop.Curves by matching curves by directory proximity.
        /// This post-processes the deferred bindings that were registered during entity.ReadParameters().
        /// </summary>
        public static void ResolveFaceLoops(IgesFile iges)
        {
            var allFaces = iges.Entities.OfType<IgesFace>().ToList();
            var allLoops = iges.Entities.OfType<IgesLoop>().ToList();
            var allCurves = iges.Entities.OfType<IgesEntity>().Where(e => 
                e is IgesLine || e is IgesCircularArc || e is IgesRationalBSplineCurve || e is IgesCompositeCurve).ToList();

            // Build a reverse map from entity to directory line number
            var entityToLineNum = new Dictionary<IgesEntity, int>();
            foreach (var kvp in iges.EntityLineNumberMap)
            {
                entityToLineNum[kvp.Value] = kvp.Key;
            }

            // Step 1: Match Faces to Loops by directory proximity
            foreach (var face in allFaces)
            {
                if (face.Loops == null)
                    face.Loops = new List<IgesLoop>();

                if (entityToLineNum.TryGetValue(face, out int faceLineNum))
                {
                    // Look for loops that come right after this face (within 10 lines)
                    foreach (var loop in allLoops)
                    {
                        if (entityToLineNum.TryGetValue(loop, out int loopLineNum))
                        {
                            if (loopLineNum > faceLineNum && loopLineNum <= faceLineNum + 10)
                            {
                                if (!face.Loops.Contains(loop))
                                    face.Loops.Add(loop);
                            }
                        }
                    }
                }
            }

            // Step 2: Match Loops to Curves by directory proximity
            // Curves come after loops in the IGES structure
            foreach (var loop in allLoops)
            {
                if (loop.Curves == null)
                    loop.Curves = new List<IgesEntity>();

                if (entityToLineNum.TryGetValue(loop, out int loopLineNum))
                {
                    // Look for curves that come shortly after this loop
                    // Expand range significantly since supporting entities can appear between
                    int searchStart = loopLineNum + 1;
                    int searchEnd = Math.Min(loopLineNum + 500, iges.EntityLineNumberMap.Keys.Max() + 1);
                    
                    foreach (var curve in allCurves)
                    {
                        if (entityToLineNum.TryGetValue(curve, out int curveLineNum))
                        {
                            // Curves should come after the loop (or rarely, before if reordered)
                            if ((curveLineNum > loopLineNum && curveLineNum <= searchEnd) ||
                                (curveLineNum < loopLineNum && loopLineNum - curveLineNum <= 50))
                            {
                                if (!loop.Curves.Contains(curve))
                                    loop.Curves.Add(curve);
                            }
                        }
                    }
                }
            }
        }
    }
}
