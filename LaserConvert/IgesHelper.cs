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
        /// Also resolve Loop.Curves by matching curve pointers to Curve entities.
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

            // Step 1: Match Faces to Loops
            foreach (var face in allFaces)
            {
                if (face.Loops == null)
                    face.Loops = new List<IgesLoop>();

                if (entityToLineNum.TryGetValue(face, out int faceLineNum))
                {
                    // Look for loops that come right after this face
                    foreach (var loop in allLoops)
                    {
                        if (entityToLineNum.TryGetValue(loop, out int loopLineNum))
                        {
                            // Loops typically follow faces - check if this loop is close to the face
                            if (loopLineNum > faceLineNum && loopLineNum <= faceLineNum + 10)
                            {
                                if (!face.Loops.Contains(loop))
                                {
                                    face.Loops.Add(loop);
                                }
                            }
                        }
                    }
                }
            }

            // Step 2: Resolve Loop curves using directory proximity
            foreach (var loop in allLoops)
            {
                if (loop.Curves == null)
                    loop.Curves = new List<IgesEntity>();

                if (entityToLineNum.TryGetValue(loop, out int loopLineNum))
                {
                    // Look for curves that come shortly after this loop
                    foreach (var curve in allCurves)
                    {
                        if (entityToLineNum.TryGetValue(curve, out int curveLineNum))
                        {
                            // Curves typically follow loops in IGES structure
                            if (curveLineNum > loopLineNum && curveLineNum <= loopLineNum + 50)
                            {
                                if (!loop.Curves.Contains(curve))
                                {
                                    loop.Curves.Add(curve);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
