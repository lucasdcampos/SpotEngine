namespace Voxelcraft;

/// <summary>The block types, stored as one byte per cell in a <see cref="Chunk"/>.</summary>
public enum BlockId : byte
{
    Air,
    Grass,
    Dirt,
    Stone,
    Cobblestone,
    Sand,
    Gravel,
    OakLog,
    OakLeaves,
    Planks,
    Water,
    SnowyGrass,
    Bedrock,
    CoalOre,
    IronOre,
    TallGrass,
    Poppy,
    Dandelion,
    Glass,
    Bricks,
    Glowstone,
    Sandstone,
    Cactus,
    SpruceLog,
    SpruceLeaves,
    BirchLog,
    BirchLeaves,
    Snow,
    DiamondOre,
    GoldOre,
    DeadBush,
    Count,
}

/// <summary>How a block is drawn and whether it hides its neighbours' faces.</summary>
public enum BlockShape : byte
{
    /// <summary>Nothing is drawn.</summary>
    None,

    /// <summary>A full cube that hides the faces touching it.</summary>
    Opaque,

    /// <summary>A full cube with see-through texels (leaves, glass): the faces behind it still draw.</summary>
    Cutout,

    /// <summary>Two crossed quads (grass, flowers), with no collision.</summary>
    Plant,

    /// <summary>Water: its own translucent pass.</summary>
    Liquid,
}

/// <summary>Everything the game knows about one block type.</summary>
public sealed class BlockInfo
{
    public required string Name { get; init; }

    public BlockShape Shape { get; init; } = BlockShape.Opaque;

    /// <summary>Gets the atlas tiles of the top, the sides and the bottom.</summary>
    public int Top { get; init; }

    public int Side { get; init; }

    public int Bottom { get; init; }

    /// <summary>Gets whether the biome's foliage color tints the texels its tint mask marks.</summary>
    public bool Tinted { get; init; }

    /// <summary>Gets whether the block shines on its own (it blooms at night).</summary>
    public bool Emissive { get; init; }

    /// <summary>
    /// Gets whether it is foliage: its faces are seen from both sides through the gaps between the leaves, and the
    /// faces between two foliage blocks are left out (the canopy is a shell).
    /// </summary>
    public bool Leafy { get; init; }

    /// <summary>Gets whether a plant sways in the wind.</summary>
    public bool Sways { get; init; }

    /// <summary>Gets whether the player collides with it.</summary>
    public bool Solid => Shape is BlockShape.Opaque or BlockShape.Cutout;

    public int Tile(int face) => face switch
    {
        Faces.Up => Top,
        Faces.Down => Bottom,
        _ => Side,
    };
}

/// <summary>The cube faces, in the order the mesher and the shaders share.</summary>
public static class Faces
{
    public const int East = 0;   // +X
    public const int West = 1;   // -X
    public const int Up = 2;     // +Y
    public const int Down = 3;   // -Y
    public const int South = 4;  // +Z
    public const int North = 5;  // -Z
    public const int Plant = 6;  // a crossed quad: shaded like a lit side

    public static readonly int[] DX = { 1, -1, 0, 0, 0, 0 };
    public static readonly int[] DY = { 0, 0, 1, -1, 0, 0 };
    public static readonly int[] DZ = { 0, 0, 0, 0, 1, -1 };
}

/// <summary>The block table, indexed by <see cref="BlockId"/>.</summary>
public static class Blocks
{
    private static readonly BlockInfo[] Table = Build();

    // Plain lookups for the hot loops: the mesher and the physics test millions of cells.
    private static readonly BlockShape[] Shapes = Table.Select(b => b.Shape).ToArray();

    public static int Count => Table.Length;

    public static BlockInfo Get(BlockId id) => (int)id < Table.Length ? Table[(int)id] : Table[0];

    public static BlockShape Shape(BlockId id) => Shapes[(int)id];

    /// <summary>Gets whether the block hides the faces of whatever touches it.</summary>
    public static bool IsOpaque(BlockId id) => Shapes[(int)id] == BlockShape.Opaque;

    /// <summary>Gets whether the player collides with it.</summary>
    public static bool IsSolid(BlockId id) => Shapes[(int)id] is BlockShape.Opaque or BlockShape.Cutout;

    /// <summary>Gets whether the crosshair can select it (everything but air and water).</summary>
    public static bool IsSelectable(BlockId id) => Shapes[(int)id] is not (BlockShape.None or BlockShape.Liquid);

    /// <summary>Gets whether sky light is dimmed by it — what the column's height map tracks.</summary>
    public static bool BlocksSky(BlockId id) => Shapes[(int)id] is BlockShape.Opaque or BlockShape.Cutout or BlockShape.Liquid;

    /// <summary>Gets whether something can stand on it (plants need a solid block below them).</summary>
    public static bool Supports(BlockId id) => IsSolid(id);

    /// <summary>The blocks the inventory offers, in display order.</summary>
    public static readonly BlockId[] Placeable =
    {
        BlockId.Grass, BlockId.Dirt, BlockId.Stone, BlockId.Cobblestone, BlockId.Bricks, BlockId.Planks,
        BlockId.OakLog, BlockId.BirchLog, BlockId.SpruceLog, BlockId.OakLeaves, BlockId.BirchLeaves, BlockId.SpruceLeaves,
        BlockId.Glass, BlockId.Glowstone, BlockId.Sand, BlockId.Sandstone, BlockId.Gravel, BlockId.Snow,
        BlockId.SnowyGrass, BlockId.Cactus, BlockId.CoalOre, BlockId.IronOre, BlockId.GoldOre, BlockId.DiamondOre,
        BlockId.TallGrass, BlockId.Poppy, BlockId.Dandelion, BlockId.DeadBush, BlockId.Bedrock,
    };

    private static BlockInfo[] Build()
    {
        var table = new BlockInfo[(int)BlockId.Count];
        void Cube(BlockId id, string name, int tile) => table[(int)id] = new BlockInfo { Name = name, Top = tile, Side = tile, Bottom = tile };

        table[(int)BlockId.Air] = new BlockInfo { Name = "Air", Shape = BlockShape.None };
        table[(int)BlockId.Grass] = new BlockInfo { Name = "Grass Block", Top = Tiles.GrassTop, Side = Tiles.GrassSide, Bottom = Tiles.Dirt, Tinted = true };
        Cube(BlockId.Dirt, "Dirt", Tiles.Dirt);
        Cube(BlockId.Stone, "Stone", Tiles.Stone);
        Cube(BlockId.Cobblestone, "Cobblestone", Tiles.Cobblestone);
        Cube(BlockId.Sand, "Sand", Tiles.Sand);
        Cube(BlockId.Gravel, "Gravel", Tiles.Gravel);
        table[(int)BlockId.OakLog] = new BlockInfo { Name = "Oak Log", Top = Tiles.OakLogTop, Side = Tiles.OakLog, Bottom = Tiles.OakLogTop };
        table[(int)BlockId.OakLeaves] = new BlockInfo
        {
            Name = "Oak Leaves", Shape = BlockShape.Cutout, Top = Tiles.OakLeaves, Side = Tiles.OakLeaves, Bottom = Tiles.OakLeaves,
            Tinted = true, Leafy = true,
        };
        Cube(BlockId.Planks, "Oak Planks", Tiles.Planks);
        table[(int)BlockId.Water] = new BlockInfo { Name = "Water", Shape = BlockShape.Liquid, Top = Tiles.Water, Side = Tiles.Water, Bottom = Tiles.Water };
        table[(int)BlockId.SnowyGrass] = new BlockInfo { Name = "Snowy Grass", Top = Tiles.Snow, Side = Tiles.SnowySide, Bottom = Tiles.Dirt };
        Cube(BlockId.Bedrock, "Bedrock", Tiles.Bedrock);
        Cube(BlockId.CoalOre, "Coal Ore", Tiles.CoalOre);
        Cube(BlockId.IronOre, "Iron Ore", Tiles.IronOre);
        table[(int)BlockId.TallGrass] = new BlockInfo
        {
            Name = "Tall Grass", Shape = BlockShape.Plant, Top = Tiles.TallGrass, Side = Tiles.TallGrass, Bottom = Tiles.TallGrass,
            Tinted = true, Sways = true,
        };
        table[(int)BlockId.Poppy] = new BlockInfo
        {
            Name = "Poppy", Shape = BlockShape.Plant, Top = Tiles.Poppy, Side = Tiles.Poppy, Bottom = Tiles.Poppy, Tinted = true, Sways = true,
        };
        table[(int)BlockId.Dandelion] = new BlockInfo
        {
            Name = "Dandelion", Shape = BlockShape.Plant, Top = Tiles.Dandelion, Side = Tiles.Dandelion, Bottom = Tiles.Dandelion,
            Tinted = true, Sways = true,
        };
        table[(int)BlockId.Glass] = new BlockInfo { Name = "Glass", Shape = BlockShape.Cutout, Top = Tiles.Glass, Side = Tiles.Glass, Bottom = Tiles.Glass };
        Cube(BlockId.Bricks, "Bricks", Tiles.Bricks);
        table[(int)BlockId.Glowstone] = new BlockInfo { Name = "Glowstone", Top = Tiles.Glowstone, Side = Tiles.Glowstone, Bottom = Tiles.Glowstone, Emissive = true };
        table[(int)BlockId.Sandstone] = new BlockInfo { Name = "Sandstone", Top = Tiles.SandstoneTop, Side = Tiles.Sandstone, Bottom = Tiles.SandstoneTop };
        table[(int)BlockId.Cactus] = new BlockInfo { Name = "Cactus", Top = Tiles.CactusTop, Side = Tiles.Cactus, Bottom = Tiles.CactusTop };
        table[(int)BlockId.SpruceLog] = new BlockInfo { Name = "Spruce Log", Top = Tiles.SpruceLogTop, Side = Tiles.SpruceLog, Bottom = Tiles.SpruceLogTop };
        table[(int)BlockId.SpruceLeaves] = new BlockInfo
        {
            Name = "Spruce Leaves", Shape = BlockShape.Cutout, Top = Tiles.SpruceLeaves, Side = Tiles.SpruceLeaves, Bottom = Tiles.SpruceLeaves,
            Leafy = true,
        };
        table[(int)BlockId.BirchLog] = new BlockInfo { Name = "Birch Log", Top = Tiles.BirchLogTop, Side = Tiles.BirchLog, Bottom = Tiles.BirchLogTop };
        table[(int)BlockId.BirchLeaves] = new BlockInfo
        {
            Name = "Birch Leaves", Shape = BlockShape.Cutout, Top = Tiles.BirchLeaves, Side = Tiles.BirchLeaves, Bottom = Tiles.BirchLeaves,
            Leafy = true,
        };
        Cube(BlockId.Snow, "Snow", Tiles.Snow);
        Cube(BlockId.DiamondOre, "Diamond Ore", Tiles.DiamondOre);
        Cube(BlockId.GoldOre, "Gold Ore", Tiles.GoldOre);
        table[(int)BlockId.DeadBush] = new BlockInfo
        {
            Name = "Dead Bush", Shape = BlockShape.Plant, Top = Tiles.DeadBush, Side = Tiles.DeadBush, Bottom = Tiles.DeadBush, Sways = true,
        };
        return table;
    }
}
