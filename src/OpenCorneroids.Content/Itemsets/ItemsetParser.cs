using System.Globalization;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;
using OpenCorneroids.Content.Vfs;
using OpenCorneroids.Content.Xml;

namespace OpenCorneroids.Content.Itemsets;

/// <summary>
/// Parses Corneroids itemset XML: vanilla <c>defaultItemset.xml</c> and community itemsets go through the same code.
/// </summary>
public static class ItemsetParser
{
    delegate IEnumerable<IItemComponent> ComponentParser(XElement e, FieldReader r);

    sealed record KindSpec(string[] Fields, ComponentParser Parse);

    static readonly string[] CommonFields = ["color", "description", "maxStackSize", "modelDepth", "name", "powerUsage", "sprite"];
    static readonly string[] BlockFields = ["mass", "health", "powerToDrill", "dimensions", "activeTextures", "deactiveTextures", "power"];
    static readonly string[] Sections = ["name", "author", "key", "defaultItems", "textures", "blocks", "projectiles", "crafting", "items"];

    static readonly KindSpec Plain = new([], (_, _) => []);

    // Keyed by "<category>/<element>" because tag names are reused in different roles: <crafting> is a category,
    // a block and the blueprint section; <power> is a category, a block, a block field and a drill field; and so on.
    static readonly Dictionary<string, KindSpec> BlockKinds = new(StringComparer.Ordinal)
    {
        ["armors/armor"] = Plain,
        ["asteroids/asteroid"] = new(["asteroidType"], (e, r) => [new AsteroidComponent(Asteroid(e, r))]),
        ["cameras/camera"] = Plain,
        ["consoles/console"] = new(["buttons"], (e, r) => [new ConsoleComponent(r.Int(e, "buttons"))]),
        ["crafting/crafting"] = new([], (e, r) => [new CraftingStationComponent(Station(e, r))]),
        ["doors/door"] = new(["door"], (e, r) => [Door(e.Element("door"), r)]),
        ["gunConsoles/gunConsole"] = new(["buttons"], (e, r) => [new ConsoleComponent(r.Int(e, "buttons"), GunsOnly: true)]),
        ["guns/gun"] = new(["barrelLength", "reloadTime", "projectileId"],
            (e, r) => [new GunComponent(r.Float(e, "barrelLength"), r.Int(e, "reloadTime"), r.Int(e, "projectileId"))]),
        ["pointLights/pointLight"] = new(["light"], Lights),
        ["spotLights/spotLight"] = new(["light"], Lights),
        ["minerals/mineral"] = new(["asteroidType", "rarity"], (e, r) => [new MineralComponent(Asteroid(e, r), r.Int(e, "rarity"))]),
        ["power/power"] = Plain,
        ["thrusters/thruster"] = new(["thruster"], (e, r) => [Thruster(e.Element("thruster"), r)]),
        ["containers/container"] = new(["container"],
            (e, r) => [new ContainerComponent(r.Int(e.Element("container") ?? e, "width"), r.Int(e.Element("container") ?? e, "height"))]),
        ["windows/window"] = Plain,
    };

    static readonly Dictionary<string, KindSpec> ItemKinds = new(StringComparer.Ordinal)
    {
        ["consumables/consumable"] = new(["type", "armor", "health", "power", "duration", "buffSpriteCoords"], (e, r) =>
            [new ConsumableComponent(Enum<ConsumableKind>(e, "type", r), r.Int(e, "armor"), r.Int(e, "health"),
                r.Int(e, "power"), r.Int(e, "duration"), r.Cell(e.Element("buffSpriteCoords")))]),
        ["ingredients/ingredient"] = Plain,
        ["drills/drill"] = new(["light", "power"], (e, r) => [new DrillComponent(r.Int(e, "power")), .. Lights(e, r)]),
        ["torches/torch"] = new(["light", "burningTime"], (e, r) => [new TorchComponent(r.Int(e, "burningTime")), .. Lights(e, r)]),
        ["weapons/weapon"] = new(["light", "reloadTime", "projectileId", "pushback", "spread"], (e, r) =>
            [new WeaponComponent(r.Int(e, "reloadTime"), r.Int(e, "projectileId"), r.Float(e, "pushback"), r.Float(e, "spread")),
             .. Lights(e, r)]),
        ["wearables/wearable"] = new(["slot", "armorRating", "healthRating", "powerRechargeRate"], (e, r) =>
            [new WearableComponent(Enum<WearableSlot>(e, "slot", r), r.Int(e, "armorRating"), r.Int(e, "healthRating"),
                r.Int(e, "powerRechargeRate"))]),
    };

    public static ItemsetDef? Parse(IContentSource source, string path, DiagnosticBag diagnostics) =>
        XmlInput.TryLoad(source, path, diagnostics) is { } doc ? Parse(doc, path, diagnostics) : null;

    public static ItemsetDef? Parse(XDocument doc, string path, DiagnosticBag diagnostics)
    {
        var r = new FieldReader(path, diagnostics);
        if (doc.Root is not { Name.LocalName: "itemset" } root)
        {
            r.Report(Severity.Error, Codes.UnexpectedRoot, $"expected <itemset>, found <{doc.Root?.Name.LocalName}>", doc.Root);
            return null;
        }
        foreach (var section in root.Elements().Where(e => !Sections.Contains(e.Name.LocalName)))
            r.Report(Severity.Warning, Codes.UnknownSection, $"unknown section <{section.Name.LocalName}>; skipped", section);

        var items = new List<ItemDef>();
        var byId = new Dictionary<int, ItemDef>();
        var byBlockId = new Dictionary<int, ItemDef>();
        ReadItems(root.Element("blocks"), BlockKinds, isBlock: true, r, items, byId, byBlockId);
        ReadItems(root.Element("items"), ItemKinds, isBlock: false, r, items, byId, byBlockId);

        return new ItemsetDef
        {
            Path = path,
            Header = ReadHeader(root, r),
            Items = items,
            ItemsById = byId,
            ItemsByBlockId = byBlockId,
            Projectiles = ReadProjectiles(root.Element("projectiles"), r),
            Blueprints = ReadBlueprints(root.Element("crafting")?.Element("blueprints"), r),
            DefaultItems = root.Element("defaultItems")?.Elements("item").Select(e => r.IntAttr(e, "itemId", -1)).ToList() ?? [],
            Fingerprint = ItemsetFingerprint.Compute(doc),
        };
    }

    /// <summary>
    /// Reads only the header (name, author, key, version, texture keys) without building the item model; used to
    /// list itemsets cheaply.
    /// </summary>
    public static ItemsetHeader? ScanHeader(IContentSource source, string path, DiagnosticBag diagnostics)
    {
        if (!source.TryOpen(path, out var stream))
        {
            diagnostics.Report(Severity.Error, Codes.FileMissing, "file not found", path);
            return null;
        }
        try
        {
            using (stream)
            using (var reader = XmlInput.CreateReader(stream))
            {
                reader.MoveToContent();
                if (reader.LocalName != "itemset")
                {
                    diagnostics.Report(Severity.Error, Codes.UnexpectedRoot, $"expected <itemset>, found <{reader.LocalName}>", path);
                    return null;
                }
                int? version = int.TryParse(reader.GetAttribute("version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
                string name = "", author = "", key = "";
                string? blockKey = null, spriteKey = null;
                if (reader.IsEmptyElement) return new ItemsetHeader(name, author, key, version, null, null);

                reader.Read();
                while (reader.NodeType != XmlNodeType.EndElement && !reader.EOF)
                {
                    if (reader.NodeType != XmlNodeType.Element) { reader.Read(); continue; }
                    switch (reader.LocalName)
                    {
                        case "name": name = reader.ReadElementContentAsString().Trim(); break;
                        case "author": author = reader.ReadElementContentAsString().Trim(); break;
                        case "key": key = reader.ReadElementContentAsString().Trim(); break;
                        case "textures":
                            var textures = (XElement)XNode.ReadFrom(reader);
                            blockKey = textures.Element("blockTexture")?.Element("key")?.Value.Trim();
                            spriteKey = textures.Element("spriteTexture")?.Element("key")?.Value.Trim();
                            break;
                        default: reader.Skip(); break;
                    }
                }
                return new ItemsetHeader(name, author, key, version, blockKey, spriteKey);
            }
        }
        catch (XmlException e)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, Codes.XmlMalformed, e.Message, path, e.LineNumber > 0 ? e.LineNumber : null));
            return null;
        }
    }

    static ItemsetHeader ReadHeader(XElement root, FieldReader r)
    {
        var textures = root.Element("textures");
        return new ItemsetHeader(
            r.Text(root, "name"), r.Text(root, "author"), r.Text(root, "key"),
            root.Attribute("version") is not null ? r.IntAttr(root, "version") : null,
            textures?.Element("blockTexture")?.Element("key")?.Value.Trim(),
            textures?.Element("spriteTexture")?.Element("key")?.Value.Trim());
    }

    static void ReadItems(XElement? section, Dictionary<string, KindSpec> kinds, bool isBlock, FieldReader r,
        List<ItemDef> items, Dictionary<int, ItemDef> byId, Dictionary<int, ItemDef> byBlockId)
    {
        if (section is null) return;
        foreach (var category in section.Elements())
        foreach (var element in category.Elements())
        {
            var kindKey = $"{category.Name.LocalName}/{element.Name.LocalName}";
            if (!kinds.TryGetValue(kindKey, out var spec))
            {
                r.ReportOnce(kindKey, Severity.Warning, Codes.UnknownCategory,
                    $"<{section.Name.LocalName}/{kindKey}> is not a known kind; skipped", element);
                continue;
            }
            if (ReadItem(element, category.Name.LocalName, spec, isBlock, r) is not { } item) continue;

            if (byId.ContainsKey(item.ItemId))
            {
                r.Report(Severity.Error, Codes.DuplicateItemId, $"itemId {item.ItemId} is already used by '{byId[item.ItemId].Name}'; '{item.Name}' skipped", element);
                continue;
            }
            if (item.Block is { } block && byBlockId.TryGetValue(block.BlockId, out var other))
            {
                r.Report(Severity.Error, Codes.DuplicateBlockId, $"blockId {block.BlockId} is already used by '{other.Name}'; '{item.Name}' skipped", element);
                continue;
            }
            byId.Add(item.ItemId, item);
            if (item.Block is not null) byBlockId.Add(item.Block.BlockId, item);
            items.Add(item);
        }
    }

    static ItemDef? ReadItem(XElement e, string category, KindSpec spec, bool isBlock, FieldReader r)
    {
        if (r.RequiredIntAttr(e, "itemId") is not { } itemId) return null;

        BlockDef? block = null;
        if (isBlock)
        {
            if (r.RequiredIntAttr(e, "blockId") is not { } blockId) return null;
            var dims = e.Element("dimensions");
            var inactive = e.Element("deactiveTextures");
            block = new BlockDef(
                blockId, r.Float(e, "mass"), r.Int(e, "health"), r.Int(e, "powerToDrill"),
                r.Whd(dims?.Element("size"), Vector3.One), r.Whd(dims?.Element("modelSize")), r.Xyz(dims?.Element("modelPlacement")),
                Faces(e, "activeTextures", r) ?? new FaceSet(default, default, default),
                inactive is null ? null : Faces(e, "deactiveTextures", r),
                e.Element("power") is null ? null : r.Int(e, "power"));
        }

        var known = new HashSet<string>(CommonFields.Concat(isBlock ? BlockFields : []).Concat(spec.Fields), StringComparer.Ordinal);
        var extras = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in e.Elements().Where(f => !known.Contains(f.Name.LocalName)))
        {
            extras[field.Name.LocalName] = field.HasElements ? field.ToString(SaveOptions.DisableFormatting) : field.Value.Trim();
            r.ReportOnce($"{e.Name.LocalName}.{field.Name.LocalName}", Severity.Info, Codes.UnknownField,
                $"<{e.Name.LocalName}> has unknown field <{field.Name.LocalName}>; kept as an extra", field);
        }

        return new ItemDef(
            itemId, e.Name.LocalName, category, r.Text(e, "name"), r.Text(e, "description"), r.Rgb(e.Element("color")),
            r.Int(e, "maxStackSize", 64), r.Int(e, "modelDepth", 1), r.Int(e, "powerUsage"), r.Cell(e.Element("sprite")),
            block, spec.Parse(e, r).ToArray(), extras, DiagnosticBag.LineOf(e));
    }

    static FaceSet? Faces(XElement block, string name, FieldReader r)
    {
        if (block.Element(name) is not { } faces)
        {
            r.Report(Severity.Warning, Codes.MissingField, $"<{block.Name.LocalName}> has no <{name}>", block);
            return null;
        }
        CellRect Face(string face)
        {
            if (faces.Element(face) is { } f) return r.Cell(f);
            r.Report(Severity.Warning, Codes.MissingField, $"<{name}> has no <{face}>", faces);
            return default;
        }
        return new FaceSet(Face("walls"), Face("top"), Face("bottom"));
    }

    static IReadOnlyDictionary<int, ProjectileDef> ReadProjectiles(XElement? section, FieldReader r)
    {
        var projectiles = new Dictionary<int, ProjectileDef>();
        foreach (var e in section?.Elements("projectile") ?? [])
        {
            if (r.RequiredIntAttr(e, "projectileId") is not { } id) continue;
            var projectile = new ProjectileDef(id, r.Rgb(e.Element("color")), r.Int(e, "damage"), r.Int(e, "lifetime"),
                r.Float(e, "ricochetAngle"), r.Float(e, "speed"), r.Whd(e.Element("size")), DiagnosticBag.LineOf(e));
            if (!projectiles.TryAdd(id, projectile))
                r.Report(Severity.Error, Codes.DuplicateProjectileId, $"projectileId {id} is defined twice; second skipped", e);
        }
        return projectiles;
    }

    static IReadOnlyList<BlueprintDef> ReadBlueprints(XElement? section, FieldReader r) =>
        (section?.Elements("blueprint") ?? []).Select(e => new BlueprintDef(
            e.Attribute("name")?.Value ?? "",
            EnumAttr<CraftingStation>(e, "usage", r),
            e.Element("ingredients")?.Elements("ingredient").Select(i => Stack(i, r)).ToList() ?? [],
            e.Element("result") is { } result ? Stack(result, r) : new ItemStack(-1, 0),
            DiagnosticBag.LineOf(e))).ToList();

    static ItemStack Stack(XElement e, FieldReader r) => new(r.IntAttr(e, "itemId", -1), r.IntAttr(e, "quantity", 1));

    static IEnumerable<IItemComponent> Lights(XElement e, FieldReader r) =>
        e.Element("light") is { } light
            ? [new LightComponent(new LightDef(light.Attribute("type")?.Value ?? "point", r.Rgb(light.Element("color")), r.Float(light, "radius")))]
            : [];

    static DoorComponent Door(XElement? d, FieldReader r) =>
        d is null ? new DoorComponent(Vector3.Zero, 0, 0, 0)
            : new DoorComponent(r.Xyz(d.Element("direction")), r.Float(d, "distance"), r.Float(d, "speed"), r.Int(d, "time"));

    static ThrusterComponent Thruster(XElement? t, FieldReader r) =>
        t is null ? new ThrusterComponent(0, 0, Vector3.Zero)
            : new ThrusterComponent(r.Float(t, "force"), r.Float(t, "flameLength"), r.Xyz(t.Element("flameOffset")));

    static AsteroidType Asteroid(XElement e, FieldReader r)
    {
        var text = r.Text(e, "asteroidType");
        if (System.Enum.TryParse<AsteroidType>(text, ignoreCase: true, out var type)) return type;
        r.Report(Severity.Warning, Codes.BadEnum, $"unknown asteroidType '{text}'; using light", e.Element("asteroidType") ?? e);
        return AsteroidType.Light;
    }

    static CraftingStation Station(XElement e, FieldReader r)
    {
        var text = e.Attribute("type")?.Value.Trim() ?? "";
        switch (text.ToLowerInvariant())
        {
            case "craftingtable": return CraftingStation.CraftingTable;
            case "extractor": return CraftingStation.Extractor;
            case "smelter": return CraftingStation.Smelter;
        }
        r.Report(Severity.Warning, Codes.BadEnum, $"unknown crafting type '{text}'; using craftingtable", e);
        return CraftingStation.CraftingTable;
    }

    static T Enum<T>(XElement e, string field, FieldReader r) where T : struct, System.Enum => Checked<T>(r.Int(e, field), e.Element(field) ?? e, r);

    static T EnumAttr<T>(XElement e, string attribute, FieldReader r) where T : struct, System.Enum =>
        Checked<T>(r.IntAttr(e, attribute), (XObject?)e.Attribute(attribute) ?? e, r);

    static T Checked<T>(int value, XObject at, FieldReader r) where T : struct, System.Enum
    {
        var result = (T)(object)value;
        if (!System.Enum.IsDefined(result))
            r.Report(Severity.Warning, Codes.BadEnum, $"{value} is not a known {typeof(T).Name}; kept as is", at);
        return result;
    }
}
