using System;
using System.Collections.Generic;
using System.Linq;
using IxMilia.Iges;
using IxMilia.Iges.Entities;

namespace LaserConvert
{
    /// <summary>
    /// Helper class for IGES-specific post-processing logic.
    /// Handles manifest-to-shell binding and face pointer resolution
    /// after IxMilia has loaded all entities.
    /// </summary>
    internal static class IgesHelper
    {
        /// <summary>
        /// Resolve manifest-to-shell bindings and populate shell faces.
        /// This must be called after IgesFile.Load() but before processing solids.
        /// </summary>
        public static void ResolveManifestShellBindings(IgesFile iges)
        {
            var manifests = iges.Entities.OfType<IgesManifestSolidBRepObject>().ToList();
            var shells = iges.Entities.OfType<IgesShell>().ToList();
            var allFaces = iges.Entities.OfType<IgesFace>().ToList();

            Console.WriteLine($"[IGES] Resolving bindings: {manifests.Count} manifests, {shells.Count} shells, {allFaces.Count} faces");
            
            // DEBUG: Show manifest order and names
            Console.WriteLine($"[IGES] Manifests in load order:");
            for (int i = 0; i < manifests.Count; i++)
            {
                Console.WriteLine($"[IGES]   [{i}] '{GetEntityName(manifests[i])}'");
            }
            
            // DEBUG: Show shell order
            Console.WriteLine($"[IGES] Shells in load order:");
            for (int i = 0; i < shells.Count; i++)
            {
                Console.WriteLine($"[IGES]   [{i}] Shell (will have {shells[i].FacePointers?.Count ?? 0} faces)");
            }

            // Step 1: Populate shell faces from FacePointers
            ResolvShellFacePointers(iges, shells, allFaces);

            // Step 2: Bind manifests to shells
            BindManifestsToShells(manifests, shells);
            
            // DEBUG: Final state
            Console.WriteLine($"[IGES] Final binding state:");
            for (int i = 0; i < manifests.Count; i++)
            {
                var shellIndex = shells.IndexOf(manifests[i].Shell);
                Console.WriteLine($"[IGES]   Manifest[{i}] '{GetEntityName(manifests[i])}' ? Shell[{shellIndex}] ({manifests[i].Shell?.Faces?.Count ?? 0} faces)");
            }
        }

        /// <summary>
        /// Resolve face pointers in each shell by matching IGES directory entry numbers
        /// (stored in FacePointers) to actual loaded IgesFace entities via DirectoryEntryIndex.
        /// </summary>
        private static void ResolvShellFacePointers(IgesFile iges, List<IgesShell> shells, List<IgesFace> allFaces)
        {
            Console.WriteLine($"[IGES] Populating {shells.Count} shells with {allFaces.Count} total faces");

            // Build a map from directory entry index to face for quick lookup
            var faceByDirectoryIndex = new Dictionary<int, IgesFace>();
            foreach (var face in allFaces)
            {
                if (face.DirectoryEntryIndex >= 0)
                {
                    faceByDirectoryIndex[face.DirectoryEntryIndex] = face;
                }
            }

            foreach (var shell in shells)
            {
                if (shell.FacePointers == null || shell.FacePointers.Count == 0)
                {
                    Console.WriteLine($"[IGES]   Shell has no FacePointers");
                    continue;
                }

                int expectedCount = shell.FacePointers.Count;
                Console.WriteLine($"[IGES]   Shell expects {expectedCount} faces, FacePointers: {string.Join(", ", shell.FacePointers)}");

                shell.Faces = new List<IgesFace>();

                // Try to resolve each face pointer to an actual face
                foreach (int facePointer in shell.FacePointers)
                {
                    // FacePointers are 1-based directory entry numbers
                    // DirectoryEntryIndex is 0-based, so subtract 1
                    int directoryIndex = facePointer - 1;

                    if (faceByDirectoryIndex.TryGetValue(directoryIndex, out var face))
                    {
                        shell.Faces.Add(face);
                        Console.WriteLine($"[IGES]     Pointer {facePointer} ? DirectoryIndex {directoryIndex} ? Face found");
                    }
                    else
                    {
                        Console.WriteLine($"[IGES]     Pointer {facePointer} ? DirectoryIndex {directoryIndex} ? Face NOT FOUND (available indices: {string.Join(", ", faceByDirectoryIndex.Keys.OrderBy(x => x))})");
                    }
                }

                Console.WriteLine($"[IGES]   Shell resolved to {shell.Faces.Count} faces (expected {expectedCount})");
            }
        }

        /// <summary>
        /// Bind each manifest to a shell using sequential assignment based on entity load order.
        /// This is reliable because manifests and shells appear in the same order in IGES files.
        /// We do NOT trust IxMilia's manifest.Shell binding because pointer resolution can fail.
        /// </summary>
        private static void BindManifestsToShells(List<IgesManifestSolidBRepObject> manifests, List<IgesShell> shells)
        {
            Console.WriteLine($"[IGES] Binding {manifests.Count} manifests to shells");

            // Use sequential assignment: manifest[i] ? shell[i]
            // This is safe because:
            // 1. Manifests and shells are loaded in file order
            // 2. Each manifest's shell pointer references the shell that comes next in the file
            // 3. IxMilia's binder often fails to resolve these pointers correctly,
            //    so we use entity load order as the source of truth instead
            
            for (int i = 0; i < manifests.Count && i < shells.Count; i++)
            {
                var manifest = manifests[i];
                var shell = shells[i];
                
                manifest.Shell = shell;
                Console.WriteLine($"[IGES]   Manifest[{i}] '{GetEntityName(manifest)}' ? Shell[{i}] ({shell.Faces?.Count ?? 0} faces)");
            }

            if (manifests.Count > shells.Count)
            {
                Console.WriteLine($"[IGES]   WARNING: {manifests.Count - shells.Count} manifests have no shells!");
            }
        }

        private static string GetEntityName(IgesEntity e)
        {
            var name = e.EntityLabel;
            if (string.IsNullOrWhiteSpace(name))
                name = e.EntityType.ToString();
            return name;
        }
    }
}
