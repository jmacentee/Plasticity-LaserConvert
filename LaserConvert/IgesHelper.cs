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
        /// Resolve Face.Loops by matching stored loop pointers to Loop entities.
        /// Resolve Loop.Curves by matching stored edge pointers to Curve entities.
        /// This properly resolves entity pointers that were read from the IGES file.
        /// </summary>
        public static void ResolveFaceLoops(IgesFile iges)
        {
            var allFaces = iges.Entities.OfType<IgesFace>().ToList();
            var allLoops = iges.Entities.OfType<IgesLoop>().ToList();
            var allCurves = iges.Entities.OfType<IgesEntity>().Where(e => 
                e is IgesLine || e is IgesCircularArc || e is IgesRationalBSplineCurve || e is IgesCompositeCurve).ToList();

            // Build maps from directory line number to entity
            var lineToEntity = iges.EntityLineNumberMap;
            var entityToLine = new Dictionary<IgesEntity, int>();
            foreach (var kvp in lineToEntity)
            {
                entityToLine[kvp.Value] = kvp.Key;
            }

            // Step 1: Resolve Face loop pointers to actual Loop entities
            foreach (var face in allFaces)
            {
                if (face.Loops == null)
                    face.Loops = new List<IgesLoop>();

                // Face stores loop pointers - resolve them using the -2 offset (IGES pointer format)
                foreach (var loopPointer in face._loopPointers ?? new List<int>())
                {
                    if (loopPointer <= 0)
                        continue;

                    int directoryLine = loopPointer - 2;
                    if (lineToEntity.TryGetValue(directoryLine, out var entity) && entity is IgesLoop loop)
                    {
                        if (!face.Loops.Contains(loop))
                            face.Loops.Add(loop);
                    }
                }
            }

            // Step 2: Resolve Loop edge pointers to actual Curve entities
            foreach (var loop in allLoops)
            {
                if (loop.Curves == null)
                    loop.Curves = new List<IgesEntity>();

                // Loop stores edge pointers - resolve them using the -2 offset (IGES pointer format)
                foreach (var edgePointer in loop.EdgePointers)
                {
                    if (edgePointer <= 0)
                        continue;

                    int directoryLine = edgePointer - 2;
                    if (lineToEntity.TryGetValue(directoryLine, out var entity))
                    {
                        if ((entity is IgesLine || entity is IgesCircularArc || entity is IgesRationalBSplineCurve || entity is IgesCompositeCurve) &&
                            !loop.Curves.Contains(entity))
                        {
                            loop.Curves.Add(entity);
                        }
                    }
                }
            }
        }
    }
}
