using System;
using System.Collections.Generic;
using System.Linq;
using IxMilia.Iges;
using IxMilia.Iges.Entities;

namespace LaserConvert
{
    /// <summary>
    /// Helper class for IGES-specific post-processing logic.
    /// Handles manifest-to-shell binding after IxMilia has loaded all entities.
    /// </summary>
    internal static class IgesHelper
    {
        /// <summary>
        /// Resolve manifest-to-shell bindings after IxMilia has loaded all entities.
        /// Shell face binding is now handled by IxMilia's binder mechanism.
        /// </summary>
        public static void ResolveManifestShellBindings(IgesFile iges)
        {
            var manifests = iges.Entities.OfType<IgesManifestSolidBRepObject>().ToList();
            var shells = iges.Entities.OfType<IgesShell>().ToList();
            var allFaces = iges.Entities.OfType<IgesFace>().ToList();

            Console.WriteLine($"[IGES] Resolving bindings: {manifests.Count} manifests, {shells.Count} shells");

            // Step 1: Manually bind shell face pointers to actual faces
            ResolvShellFacePointers(iges, shells, allFaces);

            // Step 2: Bind manifests to shells
            BindManifestsToShells(manifests, shells);
        }

        private static void ResolvShellFacePointers(IgesFile iges, List<IgesShell> shells, List<IgesFace> allFaces)
        {
            // Build a map from directory entry index (0-based) to entity
            var entityByDirectoryIndex = new Dictionary<int, IgesEntity>();
            var allEntities = iges.Entities;
            
            foreach (var entity in allEntities)
            {
                if (entity.DirectoryEntryIndex >= 0)
                {
                    entityByDirectoryIndex[entity.DirectoryEntryIndex] = entity;
                }
            }

            Console.WriteLine($"[IGES] Built entity map with {entityByDirectoryIndex.Count} directory entries");
            Console.WriteLine($"[IGES] Directory entry indices range: {entityByDirectoryIndex.Keys.Min()}-{entityByDirectoryIndex.Keys.Max()}");
            
            // Show distribution of entity types
            var typeCount = entityByDirectoryIndex.Values.GroupBy(e => e.EntityType).ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine($"[IGES] Entity type distribution:");
            foreach (var kvp in typeCount.OrderBy(kvp => kvp.Key.ToString()))
            {
                Console.WriteLine($"[IGES]   {kvp.Key}: {kvp.Value}");
            }
            
            // Show all Face entities and their directory indices, highlighting ones with edges
            Console.WriteLine($"[IGES] All Face entities (showing edge counts):");
            foreach (var kvp in entityByDirectoryIndex.Where(kvp => kvp.Value is IgesFace).OrderBy(kvp => kvp.Key))
            {
                var face = kvp.Value as IgesFace;
                int edgeCount = face?.Edges?.Count ?? 0;
                int loopCount = face?.Loops?.Count ?? 0;
                Console.WriteLine($"[IGES]   DirectoryIndex {kvp.Key} ? Pointer {kvp.Key + 1} (edges={edgeCount}, loops={loopCount})");
            }

            foreach (var shell in shells)
            {
                if (shell.FacePointers == null || shell.FacePointers.Count == 0)
                {
                    Console.WriteLine($"[IGES] Shell has no FacePointers");
                    continue;
                }

                Console.WriteLine($"[IGES] Shell has {shell.FacePointers.Count} face pointers: {string.Join(", ", shell.FacePointers)}");
                
                shell.Faces = new List<IgesFace>();

                foreach (int pointer in shell.FacePointers)
                {
                    // IGES pointers in Shell parameters are directory line numbers (1-based, 2 lines per entry)
                    // The pointers seem to point one entry PAST the actual Face in the IGES files
                    // Convert to 0-based directory entry index: (pointer - 1) / 2 - 1
                    int directoryIndex = (pointer - 1) / 2 - 1;
                    
                    if (entityByDirectoryIndex.TryGetValue(directoryIndex, out var entity))
                    {
                        if (entity is IgesFace face)
                        {
                            shell.Faces.Add(face);
                            Console.WriteLine($"[IGES]   Pointer {pointer} ? DirectoryIndex {directoryIndex} ? Face found");
                        }
                        else
                        {
                            Console.WriteLine($"[IGES]   Pointer {pointer} ? DirectoryIndex {directoryIndex} ? Entity found but not a Face (type: {entity.EntityType})");
                        }
                    }
                    else
                    {
                        var nearbyIndices = entityByDirectoryIndex.Keys
                            .Where(idx => Math.Abs(idx - directoryIndex) <= 3)
                            .OrderBy(idx => Math.Abs(idx - directoryIndex))
                            .Take(3);
                        string nearbyStr = nearbyIndices.Any() ? string.Join(", ", nearbyIndices) : "none";
                        Console.WriteLine($"[IGES]   Pointer {pointer} ? DirectoryIndex {directoryIndex} ? NOT FOUND (nearby indices: {nearbyStr})");
                    }
                }

                Console.WriteLine($"[IGES] Shell resolved to {shell.Faces.Count} faces");
            }
        }

        /// <summary>
        /// Bind each manifest to a shell using sequential assignment based on entity load order.
        /// This is reliable because manifests and shells appear in the same order in IGES files.
        /// </summary>
        private static void BindManifestsToShells(List<IgesManifestSolidBRepObject> manifests, List<IgesShell> shells)
        {
            Console.WriteLine($"[IGES] Binding {manifests.Count} manifests to shells");

            for (int i = 0; i < manifests.Count && i < shells.Count; i++)
            {
                var manifest = manifests[i];
                var shell = shells[i];
                
                manifest.Shell = shell;
                
                Console.WriteLine($"[IGES]   Manifest[{i}] '{GetEntityName(manifest)}' ? Shell[{i}] ({shell.Faces?.Count ?? 0} faces)");
                if (shell.Faces != null && shell.Faces.Count > 0)
                {
                    Console.WriteLine($"[IGES]     Shell has {shell.Faces.Count} faces with edges:");
                    int edgeTotal = 0;
                    foreach (var face in shell.Faces)
                    {
                        int faceEdges = face.Edges?.Count ?? 0;
                        edgeTotal += faceEdges;
                        Console.WriteLine($"[IGES]       Face: {faceEdges} edges");
                    }
                    Console.WriteLine($"[IGES]     Total edges in shell: {edgeTotal}");
                }
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
