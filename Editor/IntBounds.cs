namespace JesseStiller.TerrainFormerExtension { 
    public struct IntBounds {
        internal int xMin, xMax, yMin, yMax;

        public IntBounds(int xMin, int yMin, int length) {
            this.xMin = xMin;
            this.xMax = xMin + length;
            this.yMin = yMin;
            this.yMax = yMin + length;
        }
    }
}
