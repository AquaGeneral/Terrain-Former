namespace JesseStiller.TerrainFormerExtension { 
    internal enum Tool : short {
        Ignore = -2,
        None   = -1,
        RaiseOrLower,
        Smooth,
        SetHeight,
        Flatten,
        Mould,
        PaintTexture,
        Heightmap,
        FirstNonMouse = Heightmap,
        Generate,
        Settings
    }
}