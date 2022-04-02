using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    // The only way to win with these long strings is to use word wrapping in your IDE! Concatenating instead to me is much worse.
    internal static class GUIContents {
        internal static readonly string[] brushSelectionDisplayTypeLabels = { "Image Only", "Image with Type Icon", "Tabbed" };
        internal static readonly GUIContent[] previewSizesContent = { new GUIContent("32px"), new GUIContent("48px"), new GUIContent("64px") };
        internal static readonly GUIContent smoothAllTerrain = new GUIContent("Smooth All", "Smooths the entirety of the terrain based on the smoothing settings.");
        internal static readonly GUIContent boxFilterSize = new GUIContent("Smooth Size", "Sets the number of adjacent terrain segments that are taken into account when smoothing each segment. A higher value will more quickly smooth the area to become almost flat, but it may slow down performance while smoothing.");
        internal static readonly GUIContent smoothAllIterations = new GUIContent("Smooth Iterations", "Sets how many times the entire terrain will be smoothed. (This setting only applies to the Smooth All button.)");
        internal static readonly GUIContent flattenMode = new GUIContent("Flatten Mode", "Sets the mode of flattening that will be used.\n- Flatten: Terrain higher than the current click-location height will be set to the click-location height.\n- Bridge: The entire terrain will be set to the click-location height.\n- Extend: Terrain lower than the current click-location height wil be set to the click-location height.");
        internal static readonly GUIContent showSculptingGridPlane = new GUIContent("Show Sculpting Plane Grid", "Sets whether or not a grid plane will be visible while sculpting.");
        internal static readonly GUIContent raycastModeLabel = new GUIContent("Sculpt Onto", "Sets the way terrain will be sculpted.\n- Plane: Sculpting will be projected onto a plane that's located where you initially left-clicked at.\n- Terrain: Sculpting will be projected onto the terrain.");
        internal static readonly GUIContent alwaysUpdateTerrainLODs = new GUIContent("Update Terrain LODs", "Sets when the terrain(s) being modified will have their LODs updated. This" +
            "option affects sculpting performance depending on how detailed the terrain is, how close it is, and your computer.\n" +
            "Always: Only updates the terrain LODs every time they are modified, which can be up to 100 times per second.\nOn mouse up: Only updates the LODs when the mouse has been released (" +
            "which is when modifications have stopped).");
        internal static readonly GUIContent alwaysShowBrushSelection = new GUIContent("Always Show Brush Selection", "Sets whether or not the brush selection control will be expanded in the general brush settings area.");
        internal static readonly GUIContent[] heightmapSources = new GUIContent[] { new GUIContent("Greyscale"), new GUIContent("Alpha") };
        internal static readonly GUIContent collectDetailPatches = new GUIContent("Collect Detail Patches", "If enabled the detail patches in the Terrain will be removed from memory when not visible. If the property is set to false, the patches are kept in memory until the Terrain object is destroyed or the collectDetailPatches property is set to true.\n\nBy setting the property to false all the detail patches for a given density will be initialized and kept in memory. Changing the density will recreate the patches.");
        internal static readonly GUIContent mouldHeightOffset = new GUIContent("Offset", "Sets the number of units to offset the raycast position. This option is useful for making sure the moulded terrain doesn't stick out above objects.");
        internal static readonly GUIContent mouldToolRaycastTopDownContent = new GUIContent("Raycast Mode", "Sets the origin and direction of the raycast. For moulding around roads, use Top-down. For moulding around buildings, use Bottom-up.");
        internal static readonly GUIContent mouldAllIterations = new GUIContent("Smooth Iterations", "Sets how many times the mould tool will smooth surrounding terrain per frame.\"Mould All\".");
        internal static readonly GUIContent mouldAllTerrain = new GUIContent("Mould All", "Applies the moulding feature to the entire terrain.");
        internal static readonly GUIContent[] mouldToolRaycastDirectionContents = new GUIContent[] { new GUIContent("Top-down"), new GUIContent("Bottom-up") };
        internal static readonly GUIContent[] generateRampCurveOptions = new GUIContent[] { new GUIContent("X-axis"), new GUIContent("Z-axis") };
        internal static readonly GUIContent[] alwaysUpdateTerrain = new GUIContent[] { new GUIContent("Always"), new GUIContent("On mouse up") };
        internal static readonly GUIContent[] raycastModes = { new GUIContent("Plane"), new GUIContent("Terrain") };
    }
}