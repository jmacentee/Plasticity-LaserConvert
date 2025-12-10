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
    }
}
