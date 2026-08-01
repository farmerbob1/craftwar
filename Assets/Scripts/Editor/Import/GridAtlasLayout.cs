using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// Uniform-cell atlas layout math shared by every bake phase that packs a
    /// fixed-size frame (a unit-sprite box, a 32x32 terrain tile, an icon) into
    /// a square-ish grid — the same "ceil(sqrt(count)) columns" arrangement
    /// <c>UnitSpriteBank.BuildSprites</c> and <c>RuntimeTileCatalog.Build</c>
    /// each computed inline at runtime, now shared so the importer and any
    /// future baker agree on one packing rule.
    /// </summary>
    public readonly struct GridAtlasLayout
    {
        public readonly int Columns;
        public readonly int Rows;
        public readonly int CellWidth;
        public readonly int CellHeight;
        public readonly int AtlasWidth;
        public readonly int AtlasHeight;

        public GridAtlasLayout(int count, int cellWidth, int cellHeight)
        {
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            Columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, count))));
            Rows = Mathf.Max(1, (count + Columns - 1) / Columns);
            AtlasWidth = Columns * cellWidth;
            AtlasHeight = Rows * cellHeight;
        }

        public RectInt CellRect(int index) =>
            new RectInt((index % Columns) * CellWidth, (index / Columns) * CellHeight, CellWidth, CellHeight);
    }
}
