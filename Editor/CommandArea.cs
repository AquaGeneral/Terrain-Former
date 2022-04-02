namespace JesseStiller.TerrainFormerExtension {
    public class CommandArea {
        // These are offsets which refer to the point at which the command area begins 
        public int x, y;
        // Clipped left/bottom refers to how many units have been clipped on a given side
        public int clippedLeft, clippedBottom;
        // clipped width/height are the spans taking into account clipping from the brush hanging off edge(s) of the terrain.
        public int width, height;
        private CommandArea commandArea;

        public CommandArea() { }

        public CommandArea(CommandArea commandArea) {
            x = commandArea.x;
            y = commandArea.y;
            clippedLeft = commandArea.clippedLeft;
            clippedBottom = commandArea.clippedBottom;
            width = commandArea.width;
            height = commandArea.height;
        }

        public CommandArea(int x, int y, int clippedLeft, int clippedBottom, int width, int height) {
            this.x = x;
            this.y = y;
            this.clippedLeft = clippedLeft;
            this.clippedBottom = clippedBottom;
            this.width = width;
            this.height = height;
        }

        public override string ToString() {
            return string.Format("X: {0}, Y: {1}, width: {2}, height: {3}))", x, y,
                width, height);
        }
    }
}