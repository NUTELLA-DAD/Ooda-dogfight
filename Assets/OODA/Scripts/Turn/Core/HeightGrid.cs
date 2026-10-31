using System;

namespace Ooda.Turn.Core
{
    /// <summary>
    /// The ground as a grid of heights (feet), read with bilinear interpolation. A plain copy of the terrain so the
    /// turn engine and the AI can ask for the ground from any thread (Unity's own terrain can only be read from
    /// the main thread). Below the grid's edges the ground is seaLevelFt.
    /// </summary>
    public class HeightGrid
    {
        public readonly float[] heightsFt;   // row-major, rows along y (north), columns along x (east)
        public readonly int nx, ny;
        public readonly double x0, y0, cellX, cellY;
        public double seaLevelFt = 0;

        public HeightGrid(float[] heightsFt, int nx, int ny, double x0Ft, double y0Ft, double cellXFt, double cellYFt)
        {
            this.heightsFt = heightsFt; this.nx = nx; this.ny = ny; x0 = x0Ft; y0 = y0Ft; cellX = cellXFt; cellY = cellYFt;
        }

        public double At(double x, double y)
        {
            double fx = (x - x0) / cellX, fy = (y - y0) / cellY;
            if (fx < 0 || fy < 0 || fx > nx - 1 || fy > ny - 1) return seaLevelFt;
            int i = Math.Min(nx - 2, (int)fx), j = Math.Min(ny - 2, (int)fy);
            double u = fx - i, v = fy - j;
            int k = j * nx + i;
            double a = heightsFt[k], b = heightsFt[k + 1], c = heightsFt[k + nx], d = heightsFt[k + nx + 1];
            return Math.Max(seaLevelFt, (a * (1 - u) + b * u) * (1 - v) + (c * (1 - u) + d * u) * v);
        }
    }
}
