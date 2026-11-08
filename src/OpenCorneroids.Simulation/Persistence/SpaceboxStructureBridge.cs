using System.IO.Compression;
using System.Numerics;
using OpenCorneroids.Simulation.Persistence.Nbt;
using OpenCorneroids.Simulation.Shapes;

namespace OpenCorneroids.Simulation.Persistence;

/// <summary>
/// Bidirectional import and export bridge for Spacebox structure files (.sbs).
/// Serializes to and deserializes from GZIP-compressed binary Named Binary Tag (NBT) compounds
/// mapping palettes, chunk grids, orientations, and control consoles into OpenCorneroids snapshots.
/// </summary>
public static class SpaceboxStructureBridge
{
    private const int MaxDimension = 512;
    private const int MinDimension = 1;

    private static readonly Dictionary<string, int> KnownBlockNameToId = new(StringComparer.OrdinalIgnoreCase)
    {
        // Air / Void
        { "air", WorldSnapshot.AirBlockId },
        { "default:air", WorldSnapshot.AirBlockId },
        { "void", WorldSnapshot.AirBlockId },
        { "default:void", WorldSnapshot.AirBlockId },
        { "barrier", WorldSnapshot.AirBlockId },
        { "default:barrier", WorldSnapshot.AirBlockId },
        { "", WorldSnapshot.AirBlockId },

        // Asteroids & Rocks
        { "stone", 0 },
        { "default:stone", 0 },
        { "rock", 0 },
        { "default:rock", 0 },
        { "light_rock", 0 },
        { "default:light_rock", 0 },
        { "moritz", 0 },
        { "default:moritz", 0 },
        { "medium_rock", 1 },
        { "default:medium_rock", 1 },
        { "heavy_rock", 2 },
        { "default:heavy_rock", 2 },
        { "ice_rock", 32 },
        { "default:ice_rock", 32 },
        { "dense_ice_rock", 32 },
        { "default:dense_ice_rock", 32 },
        { "cobaltite_rock", 33 },
        { "default:cobaltite_rock", 33 },

        // Minerals & Ores
        { "uranium_ore", 3 },
        { "default:uranium_ore", 3 },
        { "iron_ore", 4 },
        { "default:iron_ore", 4 },
        { "aluminium_ore", 5 },
        { "default:aluminium_ore", 5 },
        { "titanium_ore", 16 },
        { "default:titanium_ore", 16 },
        { "thulium_ore", 17 },
        { "default:thulium_ore", 17 },
        { "obscurium_ore", 18 },
        { "default:obscurium_ore", 18 },
        { "ponium_ore", 18 },
        { "default:ponium_ore", 18 },

        // Aluminium Hulls
        { "light_aluminium", 8 },
        { "default:light_aluminium", 8 },
        { "aluminium_block_light", 8 },
        { "default:aluminium_block_light", 8 },
        { "aluminium_hull", 8 },
        { "default:aluminium_hull", 8 },
        { "medium_aluminium", 9 },
        { "default:medium_aluminium", 9 },
        { "aluminium_block_medium", 9 },
        { "default:aluminium_block_medium", 9 },
        { "heavy_aluminium", 10 },
        { "default:heavy_aluminium", 10 },
        { "aluminium_block_heavy", 10 },
        { "default:aluminium_block_heavy", 10 },

        // Iron & Steel Hulls
        { "light_iron", 7 },
        { "default:light_iron", 7 },
        { "iron_block_light", 7 },
        { "default:iron_block_light", 7 },
        { "medium_iron", 11 },
        { "default:medium_iron", 11 },
        { "iron_block_medium", 11 },
        { "default:iron_block_medium", 11 },
        { "steel_hull", 11 },
        { "default:steel_hull", 11 },
        { "steel_block_light", 11 },
        { "default:steel_block_light", 11 },
        { "steel_block_medium", 11 },
        { "default:steel_block_medium", 11 },
        { "light_steel", 11 },
        { "default:light_steel", 11 },
        { "medium_steel", 11 },
        { "default:medium_steel", 11 },
        { "hull", 11 },
        { "default:hull", 11 },
        { "heavy_iron", 12 },
        { "default:heavy_iron", 12 },
        { "iron_block_heavy", 12 },
        { "default:iron_block_heavy", 12 },
        { "heavy_steel", 12 },
        { "default:heavy_steel", 12 },
        { "steel_block_heavy", 12 },
        { "default:steel_block_heavy", 12 },

        // Titanium Hulls
        { "light_titanium", 13 },
        { "default:light_titanium", 13 },
        { "titanium_block_light", 13 },
        { "default:titanium_block_light", 13 },
        { "medium_titanium", 14 },
        { "default:medium_titanium", 14 },
        { "titanium_block_medium", 14 },
        { "default:titanium_block_medium", 14 },
        { "heavy_titanium", 15 },
        { "default:heavy_titanium", 15 },
        { "titanium_block_heavy", 15 },
        { "default:titanium_block_heavy", 15 },

        // Power & Electrical
        { "cable_block", 19 },
        { "default:cable_block", 19 },
        { "wire", 19 },
        { "default:wire", 19 },
        { "wire_block", 19 },
        { "default:wire_block", 19 },
        { "wire_angle", 19 },
        { "default:wire_angle", 19 },
        { "wire_hub", 19 },
        { "default:wire_hub", 19 },
        { "wire_top", 19 },
        { "default:wire_top", 19 },
        { "item_pipe", 19 },
        { "default:item_pipe", 19 },
        { "item_pipe_straight", 19 },
        { "default:item_pipe_straight", 19 },
        { "item_pipe_merger", 19 },
        { "default:item_pipe_merger", 19 },
        { "cobalt_power_block", 20 },
        { "default:cobalt_power_block", 20 },
        { "power_block", 20 },
        { "default:power_block", 20 },
        { "standard_cobalt_power_block", 20 },
        { "default:standard_cobalt_power_block", 20 },
        { "powerful_cobalt_power_block", 21 },
        { "default:powerful_cobalt_power_block", 21 },
        { "standard_nuclear_power_block", 39 },
        { "default:standard_nuclear_power_block", 39 },
        { "nuclear_power_block", 40 },
        { "default:nuclear_power_block", 40 },
        { "powerful_nuclear_power_block", 40 },
        { "default:powerful_nuclear_power_block", 40 },
        { "reactor", 20 },
        { "default:reactor", 20 },

        // Thrusters & Propulsion
        { "thruster_weak", 22 },
        { "default:thruster_weak", 22 },
        { "weak_thruster", 22 },
        { "default:weak_thruster", 22 },
        { "thruster_standard", 37 },
        { "default:thruster_standard", 37 },
        { "thruster", 28 },
        { "default:thruster", 28 },
        { "engine", 28 },
        { "default:engine", 28 },
        { "thruster_powerful", 38 },
        { "default:thruster_powerful", 38 },
        { "powerful_thruster", 38 },
        { "default:powerful_thruster", 38 },
        { "thruster_ion", 41 },
        { "default:thruster_ion", 41 },
        { "ion_thruster", 41 },
        { "default:ion_thruster", 41 },

        // Cockpit, Consoles & Cameras
        { "cockpit", 23 },
        { "default:cockpit", 23 },
        { "control_console", 23 },
        { "default:control_console", 23 },
        { "console", 23 },
        { "default:console", 23 },
        { "ship_console", 23 },
        { "default:ship_console", 23 },
        { "command_chair", 23 },
        { "default:command_chair", 23 },
        { "cannon_console", 27 },
        { "default:cannon_console", 27 },
        { "gun_console", 27 },
        { "default:gun_console", 27 },
        { "ship_camera", 26 },
        { "default:ship_camera", 26 },
        { "camera", 26 },
        { "default:camera", 26 },
        { "radar", 26 },
        { "default:radar", 26 },

        // Cannons & Weapons
        { "cannon_antipersonnel", 24 },
        { "default:cannon_antipersonnel", 24 },
        { "antipersonnel_cannon", 24 },
        { "default:antipersonnel_cannon", 24 },
        { "cannon_light_assault", 42 },
        { "default:cannon_light_assault", 42 },
        { "light_assault_cannon", 42 },
        { "default:light_assault_cannon", 42 },
        { "laser_cannon", 30 },
        { "default:laser_cannon", 30 },
        { "cannon", 30 },
        { "default:cannon", 30 },
        { "weapon", 30 },
        { "default:weapon", 30 },
        { "cannon_anticorvette", 43 },
        { "default:cannon_anticorvette", 43 },
        { "anticorvette_cannon", 43 },
        { "default:anticorvette_cannon", 43 },
        { "cannon_antifrigate", 44 },
        { "default:cannon_antifrigate", 44 },
        { "antifrigate_cannon", 44 },
        { "default:antifrigate_cannon", 44 },
        { "cannon_heavy_assault", 45 },
        { "default:cannon_heavy_assault", 45 },
        { "heavy_assault_cannon", 45 },
        { "default:heavy_assault_cannon", 45 },

        // Doors
        { "door", 25 },
        { "default:door", 25 },
        { "iron_door", 28 },
        { "default:iron_door", 28 },
        { "iron_blast_door", 28 },
        { "default:iron_blast_door", 28 },
        { "aluminium_door", 29 },
        { "default:aluminium_door", 29 },
        { "aluminium_blast_door", 29 },
        { "default:aluminium_blast_door", 29 },
        { "titanium_door", 31 },
        { "default:titanium_door", 31 },
        { "titanium_blast_door", 31 },
        { "default:titanium_blast_door", 31 },

        // Windows & Glass
        { "glass", 15 },
        { "default:glass", 15 },
        { "window", 47 },
        { "default:window", 47 },
        { "reinforced_window", 47 },
        { "default:reinforced_window", 47 },
        { "small_window", 47 },
        { "default:small_window", 47 },
        { "big_window", 48 },
        { "default:big_window", 48 },
        { "large_window", 48 },
        { "default:large_window", 48 },

        // Lights & Torches
        { "light", 16 },
        { "default:light", 16 },
        { "lamp", 16 },
        { "default:lamp", 16 },
        { "torch", 25 },
        { "default:torch", 25 },
        { "cobalt_wall_torch", 25 },
        { "default:cobalt_wall_torch", 25 },
        { "light_blue", 25 },
        { "default:light_blue", 25 },
        { "light_cyan", 25 },
        { "default:light_cyan", 25 },
        { "light_green", 25 },
        { "default:light_green", 25 },
        { "light_orange", 25 },
        { "default:light_orange", 25 },
        { "light_pink", 25 },
        { "default:light_pink", 25 },
        { "light_red", 25 },
        { "default:light_red", 25 },
        { "light_white", 25 },
        { "default:light_white", 25 },
        { "light_yellow", 25 },
        { "default:light_yellow", 25 },

        // Machines & Stations
        { "crafting_table", 34 },
        { "default:crafting_table", 34 },
        { "basic_machine", 34 },
        { "default:basic_machine", 34 },
        { "analyzer", 34 },
        { "default:analyzer", 34 },
        { "disassembler", 34 },
        { "default:disassembler", 34 },
        { "dna_reconstructor", 34 },
        { "default:dna_reconstructor", 34 },
        { "crusher", 35 },
        { "default:crusher", 35 },
        { "enhanced_crusher", 35 },
        { "default:enhanced_crusher", 35 },
        { "mineral_extractor", 35 },
        { "default:mineral_extractor", 35 },
        { "extractor", 35 },
        { "default:extractor", 35 },
        { "furnace", 36 },
        { "default:furnace", 36 },
        { "enhanced_furnace", 36 },
        { "default:enhanced_furnace", 36 },
        { "ore_furnace", 36 },
        { "default:ore_furnace", 36 },

        // Crates & Storage
        { "cargo", 46 },
        { "default:cargo", 46 },
        { "container", 46 },
        { "default:container", 46 },
        { "storage", 46 },
        { "default:storage", 46 },
        { "basic_aluminium_crate", 46 },
        { "default:basic_aluminium_crate", 46 },
        { "basic_iron_crate", 46 },
        { "default:basic_iron_crate", 46 },
        { "basic_titanium_crate", 46 },
        { "default:basic_titanium_crate", 46 },
        { "safe", 46 },
        { "default:safe", 46 },

        // Devices & Decor
        { "claim_beacon", 20 },
        { "default:claim_beacon", 20 },
        { "gravity_generator", 20 },
        { "default:gravity_generator", 20 },
        { "oxygen_generator", 20 },
        { "default:oxygen_generator", 20 },
        { "cobalt_bomb", 21 },
        { "default:cobalt_bomb", 21 },
        { "uranium_bomb", 21 },
        { "default:uranium_bomb", 21 },
        { "picture_frame_iron_1x1", 47 },
        { "default:picture_frame_iron_1x1", 47 },
        { "picture_frame_iron_2x1", 47 },
        { "default:picture_frame_iron_2x1", 47 },
        { "picture_frame_iron_2x2", 47 },
        { "default:picture_frame_iron_2x2", 47 },
        { "picture_frame_plastic_1x1", 47 },
        { "default:picture_frame_plastic_1x1", 47 },
        { "picture_frame_plastic_2x1", 47 },
        { "default:picture_frame_plastic_2x1", 47 },
        { "picture_frame_plastic_2x2", 47 },
        { "default:picture_frame_plastic_2x2", 47 },
        { "sign_panel_wide", 47 },
        { "default:sign_panel_wide", 47 },
        { "sign_steel", 47 },
        { "default:sign_steel", 47 },
        { "sign_steel_wide", 47 },
        { "default:sign_steel_wide", 47 },
    };

    private static readonly Dictionary<int, string> KnownIdToBlockName = new()
    {
        { WorldSnapshot.AirBlockId, "default:air" },
        { 0, "default:light_rock" },
        { 1, "default:medium_rock" },
        { 2, "default:heavy_rock" },
        { 3, "default:uranium_ore" },
        { 4, "default:iron_ore" },
        { 5, "default:aluminium_ore" },
        { 7, "default:iron_block_light" },
        { 8, "default:aluminium_block_light" },
        { 9, "default:aluminium_block_medium" },
        { 10, "default:aluminium_block_heavy" },
        { 11, "default:steel_hull" },
        { 12, "default:iron_block_heavy" },
        { 13, "default:titanium_block_light" },
        { 14, "default:titanium_block_medium" },
        { 15, "default:titanium_block_heavy" },
        { 16, "default:titanium_ore" },
        { 17, "default:thulium_ore" },
        { 18, "default:obscurium_ore" },
        { 19, "default:cable_block" },
        { 20, "default:cobalt_power_block" },
        { 21, "default:powerful_cobalt_power_block" },
        { 22, "default:thruster_weak" },
        { 23, "default:ship_console" },
        { 24, "default:cannon_antipersonnel" },
        { 25, "default:light_blue" },
        { 26, "default:ship_camera" },
        { 27, "default:cannon_console" },
        { 28, "default:iron_door" },
        { 29, "default:aluminium_door" },
        { 31, "default:titanium_door" },
        { 32, "default:ice_rock" },
        { 33, "default:cobaltite_rock" },
        { 34, "default:crafting_table" },
        { 35, "default:crusher" },
        { 36, "default:furnace" },
        { 37, "default:thruster_standard" },
        { 38, "default:thruster_powerful" },
        { 39, "default:standard_nuclear_power_block" },
        { 40, "default:nuclear_power_block" },
        { 41, "default:thruster_ion" },
        { 42, "default:cannon_light_assault" },
        { 43, "default:cannon_anticorvette" },
        { 44, "default:cannon_antifrigate" },
        { 45, "default:cannon_heavy_assault" },
        { 46, "default:basic_aluminium_crate" },
        { 47, "default:window" },
        { 48, "default:big_window" },
    };

    /// <summary>
    /// Deserializes a Spacebox .sbs structure stream into an OpenCorneroids <see cref="ShipSnapshot"/>.
    /// </summary>
    /// <param name="sbsStream">A binary stream containing GZIP-compressed (or raw) NBT data.</param>
    /// <param name="name">Default fallback name if missing in structure metadata.</param>
    public static ShipSnapshot Import(Stream sbsStream, string name = "Imported Structure")
    {
        ArgumentNullException.ThrowIfNull(sbsStream);

        NbtCompound root;
        try
        {
            root = NbtReader.ReadRoot(sbsStream);
        }
        catch (Exception ex) when (ex is not WorldSaveException)
        {
            throw new WorldSaveException($"Corrupt or invalid Spacebox structure NBT stream: {ex.Message}", ex);
        }

        int version = -1;
        if (root.TryGet<NbtInt>("format", out var formatTag))
        {
            version = formatTag.Value;
        }
        else if (root.TryGet<NbtInt>("Version", out var versionTag))
        {
            version = versionTag.Value;
        }

        if (version != 1)
        {
            throw new WorldSaveException("Invalid Spacebox structure: missing or unsupported 'Version' tag (expected Version 1).");
        }

        // Branch 1: Native Spacebox Chunked Structure (format with entity.chunks)
        if (root.GetCompound("entity") is { } entityCompound && entityCompound.GetList("chunks") is { } chunksList)
        {
            return ImportChunkedEntity(root, entityCompound, chunksList, name);
        }

        // Branch 2: Flat Structure format
        // 1. Dimensions
        var sizeList = root.GetList("Size");
        if (sizeList == null || sizeList.Count != 3)
        {
            throw new WorldSaveException("Invalid Spacebox structure: missing or invalid 'Size' tag (expected list of 3 integers).");
        }

        int sx = GetIntFromTag(sizeList[0]);
        int sy = GetIntFromTag(sizeList[1]);
        int sz = GetIntFromTag(sizeList[2]);

        if (sx < MinDimension || sy < MinDimension || sz < MinDimension)
        {
            throw new WorldSaveException($"Invalid structure dimensions [{sx}, {sy}, {sz}]: all dimensions must be positive integers.");
        }
        if (sx > MaxDimension || sy > MaxDimension || sz > MaxDimension)
        {
            throw new WorldSaveException($"Structure dimensions [{sx}, {sy}, {sz}] exceed maximum supported size of {MaxDimension}.");
        }

        int maxDim = Math.Max(sx, Math.Max(sy, sz));
        int S = 16;
        while (S < maxDim && S < 1024)
        {
            S *= 2;
        }
        if (S > MaxDimension)
        {
            throw new WorldSaveException($"Enclosing cubic size {S} exceeds maximum supported size of {MaxDimension}.");
        }

        // 2. Palette
        var paletteList = root.GetList("Palette");
        if (paletteList == null || paletteList.Count == 0)
        {
            throw new WorldSaveException("Invalid Spacebox structure: missing or empty 'Palette' tag.");
        }

        var shipPalette = new List<int> { WorldSnapshot.AirBlockId };
        var blockIdToShipPal = new Dictionary<int, ushort> { { WorldSnapshot.AirBlockId, 0 } };
        var sbsIdxToShipPal = new int[paletteList.Count];

        for (int i = 0; i < paletteList.Count; i++)
        {
            string blockName = ExtractBlockNameFromPaletteTag(paletteList[i]);
            int blockId = ResolveBlockNameToId(blockName, i);

            if (blockId == WorldSnapshot.AirBlockId)
            {
                sbsIdxToShipPal[i] = 0;
            }
            else
            {
                if (!blockIdToShipPal.TryGetValue(blockId, out ushort palIdx))
                {
                    palIdx = (ushort)shipPalette.Count;
                    shipPalette.Add(blockId);
                    blockIdToShipPal[blockId] = palIdx;
                }
                sbsIdxToShipPal[i] = palIdx;
            }
        }

        // 3. Blocks array
        int[] rawBlocks;
        if (root.TryGet<NbtIntArray>("Blocks", out var intArrayTag) && intArrayTag != null)
        {
            rawBlocks = intArrayTag.Value;
        }
        else if (root.TryGet<NbtByteArray>("Blocks", out var byteArrayTag) && byteArrayTag != null)
        {
            rawBlocks = new int[byteArrayTag.Value.Length];
            for (int i = 0; i < byteArrayTag.Value.Length; i++) rawBlocks[i] = byteArrayTag.Value[i];
        }
        else
        {
            throw new WorldSaveException("Invalid Spacebox structure: missing 'Blocks' tag.");
        }

        int expectedCellCount = sx * sy * sz;
        if (rawBlocks.Length != expectedCellCount)
        {
            throw new WorldSaveException($"Blocks array length {rawBlocks.Length} does not match dimensions {sx}x{sy}x{sz} ({expectedCellCount}).");
        }

        // 4. Orientations array
        byte[]? rawOrientations = null;
        if (root.TryGet<NbtByteArray>("Orientations", out var orientTag) && orientTag != null)
        {
            rawOrientations = orientTag.Value;
        }

        // 5. Stride accommodation
        bool useZMajor = false;
        if (sy != sz && root.Contains("Consoles"))
        {
            var consolesTag = root.GetList("Consoles");
            if (consolesTag != null)
            {
                foreach (var cTag in consolesTag.OfType<NbtCompound>())
                {
                    var cellList = cTag.GetList("Cell");
                    if (cellList != null && cellList.Count == 3)
                    {
                        int cx = GetIntFromTag(cellList[0]);
                        int cy = GetIntFromTag(cellList[1]);
                        int cz = GetIntFromTag(cellList[2]);
                        if (cx >= 0 && cx < sx && cy >= 0 && cy < sy && cz >= 0 && cz < sz)
                        {
                            int idxY = (cy * sz + cz) * sx + cx;
                            int idxZ = (cz * sy + cy) * sx + cx;
                            if (idxY < rawBlocks.Length && idxZ < rawBlocks.Length && rawBlocks[idxY] == 0 && rawBlocks[idxZ] != 0)
                            {
                                useZMajor = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        // 6. Unpack cells and turned orientations
        var cells = new ushort[S * S * S];
        var turned = new List<TurnedCell>();

        for (int y = 0; y < sy; y++)
        {
            for (int z = 0; z < sz; z++)
            {
                for (int x = 0; x < sx; x++)
                {
                    int sbsIdx = useZMajor ? ((z * sy + y) * sx + x) : ((y * sz + z) * sx + x);
                    int sbsPalIndex = rawBlocks[sbsIdx];

                    ushort cellPalIdx = 0;
                    if (sbsPalIndex >= 0 && sbsPalIndex < sbsIdxToShipPal.Length)
                    {
                        cellPalIdx = (ushort)sbsIdxToShipPal[sbsPalIndex];
                    }

                    int cellIndex = WorldSnapshot.CellIndex(S, x, y, z);
                    cells[cellIndex] = cellPalIdx;

                    byte orientByte = 0;
                    if (rawOrientations != null && sbsIdx < rawOrientations.Length)
                    {
                        orientByte = rawOrientations[sbsIdx];
                    }

                    if (orientByte != 0)
                    {
                        int orientIndex = orientByte;
                        if (orientIndex >= 24)
                        {
                            orientIndex = CorneroidsOrientation.FromByte(orientByte);
                        }
                        if (orientIndex > 0 && orientIndex < 24)
                        {
                            turned.Add(new TurnedCell(new Int3(x, y, z), orientIndex));
                        }
                    }
                }
            }
        }

        // 7. Consoles
        var consoles = new List<SavedConsole>();
        if (root.Contains("Consoles"))
        {
            var consolesTag = root.GetList("Consoles");
            if (consolesTag != null)
            {
                foreach (var cTag in consolesTag.OfType<NbtCompound>())
                {
                    var cellTag = cTag.GetList("Cell");
                    if (cellTag == null || cellTag.Count != 3) continue;

                    var consoleCell = new Int3(
                        GetIntFromTag(cellTag[0]),
                        GetIntFromTag(cellTag[1]),
                        GetIntFromTag(cellTag[2]));

                    Int3? cameraCell = null;
                    if (cTag.Contains("Camera"))
                    {
                        var camTag = cTag.GetList("Camera");
                        if (camTag != null && camTag.Count == 3)
                        {
                            cameraCell = new Int3(
                                GetIntFromTag(camTag[0]),
                                GetIntFromTag(camTag[1]),
                                GetIntFromTag(camTag[2]));
                        }
                    }

                    var groups = new List<SavedConsoleGroup>();
                    if (cTag.Contains("Groups"))
                    {
                        var groupsTag = cTag.GetList("Groups");
                        if (groupsTag != null)
                        {
                            foreach (var gTag in groupsTag.OfType<NbtCompound>())
                            {
                                int gIndex = gTag.GetInt("Index", 0);
                                string gKey = gTag.GetString("Key", "Space");
                                var targets = new List<Int3>();

                                if (gTag.Contains("Targets"))
                                {
                                    var targetsTag = gTag.GetList("Targets");
                                    if (targetsTag != null)
                                    {
                                        foreach (var tItem in targetsTag)
                                        {
                                            if (tItem is NbtList tList && tList.Count == 3)
                                            {
                                                targets.Add(new Int3(
                                                    GetIntFromTag(tList[0]),
                                                    GetIntFromTag(tList[1]),
                                                    GetIntFromTag(tList[2])));
                                            }
                                            else if (tItem is NbtIntArray tArr && tArr.Value.Length == 3)
                                            {
                                                targets.Add(new Int3(tArr.Value[0], tArr.Value[1], tArr.Value[2]));
                                            }
                                        }
                                    }
                                }

                                groups.Add(new SavedConsoleGroup(gIndex, gKey, targets));
                            }
                        }
                    }

                    consoles.Add(new SavedConsole(consoleCell, groups, cameraCell));
                }
            }
        }

        return new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: S,
            Palette: shipPalette,
            Cells: cells)
        {
            Turned = turned,
            Consoles = consoles,
        };
    }

    private static ShipSnapshot ImportChunkedEntity(NbtCompound root, NbtCompound entity, NbtList chunksList, string fallbackName)
    {
        string shipName = root.TryGet<NbtString>("name", out var nameTag) && !string.IsNullOrWhiteSpace(nameTag.Value)
            ? nameTag.Value
            : fallbackName;

        var placedBlocks = new List<(int wx, int wy, int wz, string name, byte orientation)>();
        var consoles = new List<SavedConsole>();

        foreach (var chunkTag in chunksList.OfType<NbtCompound>())
        {
            sbyte cx = (sbyte)chunkTag.GetByte("index_x");
            sbyte cy = (sbyte)chunkTag.GetByte("index_y");
            sbyte cz = (sbyte)chunkTag.GetByte("index_z");

            var blocks = chunkTag.GetByteArray("blocks");
            var paletteList = chunkTag.GetList("palette_blocks");
            if (blocks == null || paletteList == null) continue;

            var paletteNames = new string[paletteList.Count];
            for (int i = 0; i < paletteList.Count; i++)
                paletteNames[i] = ExtractBlockNameFromPaletteTag(paletteList[i]);

            var directions = chunkTag.GetIntArray("directions");
            var rotations = chunkTag.GetIntArray("rotations");
            var orients = new Dictionary<int, byte>();
            if (directions != null)
            {
                var rotMap = new Dictionary<int, int>();
                if (rotations != null)
                {
                    for (int r = 0; r + 1 < rotations.Length; r += 2)
                        rotMap[rotations[r]] = rotations[r + 1];
                }
                for (int d = 0; d + 1 < directions.Length; d += 2)
                {
                    int flatIdx = directions[d];
                    int dir = directions[d + 1];
                    int rot = rotMap.TryGetValue(flatIdx, out var rVal) ? rVal : 0;
                    orients[flatIdx] = (byte)((dir * 4 + rot) % 24);
                }
            }

            for (int idx = 0; idx < blocks.Length; idx++)
            {
                byte val = blocks[idx];
                if (val == 0 || val >= paletteNames.Length) continue;

                string blockName = paletteNames[val];
                if (string.Equals(blockName, "default:void", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(blockName, "void", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(blockName, "air", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(blockName, "default:air", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int lx = idx / 1024;
                int ly = (idx / 32) % 32;
                int lz = idx % 32;

                int wx = cx * 32 + lx;
                int wy = cy * 32 + ly;
                int wz = cz * 32 + lz;

                byte orientation = orients.TryGetValue(idx, out var o) ? o : (byte)0;
                placedBlocks.Add((wx, wy, wz, blockName, orientation));
            }
        }

        if (placedBlocks.Count == 0)
        {
            return new ShipSnapshot(
                Position: Vector3.Zero,
                Rotation: Quaternion.Identity,
                LinearVelocity: Vector3.Zero,
                AngularVelocity: Vector3.Zero,
                Size: 16,
                Palette: [WorldSnapshot.AirBlockId],
                Cells: new ushort[16 * 16 * 16]
            )
            {
                Objects = [],
                Turned = [],
                Consoles = []
            };
        }

        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        foreach (var b in placedBlocks)
        {
            minX = Math.Min(minX, b.wx);
            minY = Math.Min(minY, b.wy);
            minZ = Math.Min(minZ, b.wz);
            maxX = Math.Max(maxX, b.wx);
            maxY = Math.Max(maxY, b.wy);
            maxZ = Math.Max(maxZ, b.wz);
        }

        int sx = maxX - minX + 1;
        int sy = maxY - minY + 1;
        int sz = maxZ - minZ + 1;

        int maxDim = Math.Max(sx, Math.Max(sy, sz));
        int S = 16;
        while (S < maxDim && S < MaxDimension) S *= 2;
        if (S > MaxDimension)
        {
            throw new WorldSaveException($"Structure dimensions [{sx}, {sy}, {sz}] exceed maximum supported size of {MaxDimension}.");
        }

        var shipPalette = new List<int> { WorldSnapshot.AirBlockId };
        var blockIdToPalIdx = new Dictionary<int, ushort> { { WorldSnapshot.AirBlockId, 0 } };
        var cells = new ushort[S * S * S];
        var turnedList = new List<TurnedCell>();

        foreach (var b in placedBlocks)
        {
            int blockId = ResolveBlockNameToId(b.name, 0);
            if (blockId == WorldSnapshot.AirBlockId) continue;

            if (!blockIdToPalIdx.TryGetValue(blockId, out ushort palIdx))
            {
                palIdx = (ushort)shipPalette.Count;
                shipPalette.Add(blockId);
                blockIdToPalIdx[blockId] = palIdx;
            }

            int lx = b.wx - minX;
            int ly = b.wy - minY;
            int lz = b.wz - minZ;

            if (lx >= 0 && lx < S && ly >= 0 && ly < S && lz >= 0 && lz < S)
            {
                int cellIndex = (lz * S + ly) * S + lx;
                cells[cellIndex] = palIdx;

                if (b.orientation > 0)
                {
                    turnedList.Add(new TurnedCell(new Int3(lx, ly, lz), b.orientation));
                }

                if (blockId == 23)
                {
                    consoles.Add(new SavedConsole(new Int3(lx, ly, lz), [], null));
                }
            }
        }

        return new ShipSnapshot(
            Position: Vector3.Zero,
            Rotation: Quaternion.Identity,
            LinearVelocity: Vector3.Zero,
            AngularVelocity: Vector3.Zero,
            Size: S,
            Palette: shipPalette,
            Cells: cells
        )
        {
            Objects = [],
            Turned = turnedList,
            Consoles = consoles
        };
    }

    /// <summary>
    /// Serializes an OpenCorneroids <see cref="ShipSnapshot"/> into a Spacebox .sbs GZIP-compressed NBT structure.
    /// Computes the tight bounding box of solid voxels and normalizes coordinates.
    /// </summary>
    /// <param name="ship">The ship snapshot to export.</param>
    /// <param name="destinationStream">Target stream to write GZIP NBT bytes to.</param>
    /// <param name="author">Creator name written to the Author metadata tag.</param>
    public static void Export(ShipSnapshot ship, Stream destinationStream, string author = "OpenCorneroids")
    {
        ArgumentNullException.ThrowIfNull(ship);
        ArgumentNullException.ThrowIfNull(destinationStream);

        // 1. Measure tight bounding box of non-air cells
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        bool hasNonAir = false;

        for (int z = 0; z < ship.Size; z++)
        {
            for (int y = 0; y < ship.Size; y++)
            {
                for (int x = 0; x < ship.Size; x++)
                {
                    ushort palIdx = ship.Cells[WorldSnapshot.CellIndex(ship.Size, x, y, z)];
                    int blockId = (palIdx < ship.Palette.Count) ? ship.Palette[palIdx] : WorldSnapshot.AirBlockId;
                    if (blockId != WorldSnapshot.AirBlockId)
                    {
                        hasNonAir = true;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (z < minZ) minZ = z;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                        if (z > maxZ) maxZ = z;
                    }
                }
            }
        }

        if (!hasNonAir)
        {
            minX = 0; minY = 0; minZ = 0;
            maxX = 0; maxY = 0; maxZ = 0;
        }

        int sizeX = maxX - minX + 1;
        int sizeY = maxY - minY + 1;
        int sizeZ = maxZ - minZ + 1;

        // 2. Build SBS Palette
        var sbsPalette = new List<string> { "default:air" };
        var blockIdToSbsIdx = new Dictionary<int, int> { { WorldSnapshot.AirBlockId, 0 } };

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    ushort palIdx = ship.Cells[WorldSnapshot.CellIndex(ship.Size, x, y, z)];
                    int blockId = (palIdx < ship.Palette.Count) ? ship.Palette[palIdx] : WorldSnapshot.AirBlockId;
                    if (blockId != WorldSnapshot.AirBlockId && !blockIdToSbsIdx.ContainsKey(blockId))
                    {
                        int newIdx = sbsPalette.Count;
                        sbsPalette.Add(ResolveBlockIdToName(blockId));
                        blockIdToSbsIdx[blockId] = newIdx;
                    }
                }
            }
        }

        // 3. Build Blocks and Orientations arrays
        int totalCells = sizeX * sizeY * sizeZ;
        var blocks = new int[totalCells];
        var orientations = new byte[totalCells];

        var turnedMap = new Dictionary<Int3, byte>();
        foreach (var tc in ship.Turned)
        {
            turnedMap[tc.Cell] = (byte)tc.Orientation;
        }

        for (int y = 0; y < sizeY; y++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int shipX = minX + x;
                    int shipY = minY + y;
                    int shipZ = minZ + z;
                    int sbsIndex = (y * sizeZ + z) * sizeX + x;

                    ushort palIdx = ship.Cells[WorldSnapshot.CellIndex(ship.Size, shipX, shipY, shipZ)];
                    int blockId = (palIdx < ship.Palette.Count) ? ship.Palette[palIdx] : WorldSnapshot.AirBlockId;
                    blocks[sbsIndex] = blockIdToSbsIdx.TryGetValue(blockId, out int sbsIdx) ? sbsIdx : 0;

                    if (turnedMap.TryGetValue(new Int3(shipX, shipY, shipZ), out byte orientByte))
                    {
                        orientations[sbsIndex] = orientByte;
                    }
                }
            }
        }

        // 4. Build Root Compound
        var root = new NbtCompound();
        root.SetInt("Version", 1);
        root.SetInt("format", 1);
        root.SetString("Name", "Ship");
        root.SetString("Author", author ?? "OpenCorneroids");

        var sizeTag = new NbtList("Size", NbtTagType.Int)
        {
            new NbtInt(sizeX),
            new NbtInt(sizeY),
            new NbtInt(sizeZ)
        };
        root.Add(sizeTag);

        var paletteTag = new NbtList("Palette", NbtTagType.Compound);
        foreach (var blockName in sbsPalette)
        {
            var entry = new NbtCompound();
            entry.SetString("Name", blockName);
            paletteTag.Add(entry);
        }
        root.Add(paletteTag);

        root.SetIntArray("Blocks", blocks);
        root.SetByteArray("Orientations", orientations);

        // 5. Consoles
        if (ship.Consoles.Count > 0)
        {
            var consolesTag = new NbtList("Consoles", NbtTagType.Compound);
            var shift = new Int3(minX, minY, minZ);

            foreach (var console in ship.Consoles)
            {
                var relCell = console.Cell - shift;
                var consoleCompound = new NbtCompound();

                var cellList = new NbtList("Cell", NbtTagType.Int)
                {
                    new NbtInt(relCell.X),
                    new NbtInt(relCell.Y),
                    new NbtInt(relCell.Z)
                };
                consoleCompound.Add(cellList);

                var groupsList = new NbtList("Groups", NbtTagType.Compound);
                foreach (var g in console.Groups)
                {
                    var groupCompound = new NbtCompound();
                    groupCompound.SetInt("Index", g.Index);
                    groupCompound.SetString("Key", g.Key ?? "Space");

                    var targetsList = new NbtList("Targets", NbtTagType.List);
                    foreach (var target in g.Targets)
                    {
                        var relTarget = target - shift;
                        var targetCoords = new NbtList(NbtTagType.Int)
                        {
                            new NbtInt(relTarget.X),
                            new NbtInt(relTarget.Y),
                            new NbtInt(relTarget.Z)
                        };
                        targetsList.Add(targetCoords);
                    }
                    groupCompound.Add(targetsList);
                    groupsList.Add(groupCompound);
                }
                consoleCompound.Add(groupsList);

                if (console.Camera.HasValue)
                {
                    var relCam = console.Camera.Value - shift;
                    var camList = new NbtList("Camera", NbtTagType.Int)
                    {
                        new NbtInt(relCam.X),
                        new NbtInt(relCam.Y),
                        new NbtInt(relCam.Z)
                    };
                    consoleCompound.Add(camList);
                }

                consolesTag.Add(consoleCompound);
            }

            root.Add(consolesTag);
        }

        // 6. Serialize to GZip stream
        NbtWriter.WriteRootToGzip(destinationStream, root, string.Empty, CompressionLevel.Optimal);
    }

    private static string ExtractBlockNameFromPaletteTag(NbtTag tag)
    {
        if (tag is NbtCompound comp)
        {
            return comp.GetString("Name", string.Empty);
        }
        if (tag is NbtString str)
        {
            return str.Value;
        }
        return string.Empty;
    }

    private static int ResolveBlockNameToId(string blockName, int paletteIndex)
    {
        if (string.IsNullOrWhiteSpace(blockName))
            return WorldSnapshot.AirBlockId;

        string normalized = blockName.Trim();
        if (KnownBlockNameToId.TryGetValue(normalized, out int id))
            return id;

        // Strip namespace if present
        int colonIdx = normalized.IndexOf(':');
        string localName = colonIdx >= 0 ? normalized[(colonIdx + 1)..] : normalized;

        if (KnownBlockNameToId.TryGetValue(localName, out id))
            return id;

        // Check for "default:block_X"
        if (localName.StartsWith("block_", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(localName[6..], out int parsedId))
        {
            return parsedId;
        }

        // Heuristic category matching
        if (localName.Equals("air", StringComparison.OrdinalIgnoreCase) ||
            localName.StartsWith("air_", StringComparison.OrdinalIgnoreCase) ||
            localName.EndsWith("_air", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("_air_", StringComparison.OrdinalIgnoreCase))
            return WorldSnapshot.AirBlockId;
        if (localName.Contains("door", StringComparison.OrdinalIgnoreCase))
            return 25;
        if (localName.Contains("cargo", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("crate", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("container", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("storage", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("safe", StringComparison.OrdinalIgnoreCase))
            return 26;
        if (localName.Contains("cockpit", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("console", StringComparison.OrdinalIgnoreCase))
            return 23;
        if (localName.Contains("camera", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("radar", StringComparison.OrdinalIgnoreCase))
            return 26;
        if (localName.Contains("thruster", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("engine", StringComparison.OrdinalIgnoreCase))
            return 28;
        if (localName.Contains("cannon", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("weapon", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("laser", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("gun", StringComparison.OrdinalIgnoreCase))
            return 30;
        if (localName.Contains("window", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("glass", StringComparison.OrdinalIgnoreCase))
            return 15;
        if (localName.Contains("light", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("lamp", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("torch", StringComparison.OrdinalIgnoreCase))
            return 16;
        if (localName.Contains("cable", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("wire", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("pipe", StringComparison.OrdinalIgnoreCase))
            return 19;
        if (localName.Contains("reactor", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("power", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("generator", StringComparison.OrdinalIgnoreCase))
            return 20;
        if (localName.Contains("craft", StringComparison.OrdinalIgnoreCase))
            return 34;
        if (localName.Contains("crush", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("extract", StringComparison.OrdinalIgnoreCase))
            return 35;
        if (localName.Contains("furnace", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("smelt", StringComparison.OrdinalIgnoreCase))
            return 36;
        if (localName.Equals("ice", StringComparison.OrdinalIgnoreCase) ||
            localName.StartsWith("ice_", StringComparison.OrdinalIgnoreCase) ||
            localName.EndsWith("_ice", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("_ice_", StringComparison.OrdinalIgnoreCase))
            return 32;
        if (localName.Contains("rock", StringComparison.OrdinalIgnoreCase) ||
            localName.Contains("stone", StringComparison.OrdinalIgnoreCase))
            return 0;

        // Default fallback: solid steel hull (11)
        return 11;
    }

    private static string ResolveBlockIdToName(int blockId)
    {
        if (KnownIdToBlockName.TryGetValue(blockId, out string? name))
            return name;

        return $"default:block_{blockId}";
    }

    private static int GetIntFromTag(NbtTag tag)
    {
        return tag switch
        {
            NbtInt i => i.Value,
            NbtShort s => s.Value,
            NbtByte b => b.Value,
            NbtLong l => (int)l.Value,
            _ => throw new WorldSaveException($"Expected integer tag, got {tag.TagType}.")
        };
    }
}
