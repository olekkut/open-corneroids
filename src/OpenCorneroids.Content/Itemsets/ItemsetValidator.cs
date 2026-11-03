using OpenCorneroids.Content.Atlases;

namespace OpenCorneroids.Content.Itemsets;

/// <summary>Checks an itemset against itself and against the atlases its view resolves to.</summary>
public static class ItemsetValidator
{
    public static void Validate(ItemsetDef set, AtlasGrid? blockAtlas, AtlasGrid? spriteAtlas, DiagnosticBag diagnostics)
    {
        void Report(Severity severity, string code, string message, int? line) =>
            diagnostics.Add(new Diagnostic(severity, code, message, set.Path, line));

        foreach (var blueprint in set.Blueprints)
        foreach (var stack in blueprint.Ingredients.Append(blueprint.Result))
        {
            if (!set.ItemsById.ContainsKey(stack.ItemId))
                Report(Severity.Error, Codes.DanglingItemRef, $"blueprint '{blueprint.Name}' uses unknown itemId {stack.ItemId}", blueprint.Line);
        }

        foreach (var id in set.DefaultItems.Where(id => !set.ItemsById.ContainsKey(id)))
            Report(Severity.Error, Codes.DanglingItemRef, $"defaultItems lists unknown itemId {id}", null);

        foreach (var item in set.Items)
        {
            var projectileId = item.Get<GunComponent>()?.ProjectileId ?? item.Get<WeaponComponent>()?.ProjectileId;
            if (projectileId is { } pid && !set.Projectiles.ContainsKey(pid))
                Report(Severity.Error, Codes.DanglingProjectileRef, $"'{item.Name}' fires unknown projectileId {pid}", item.Line);

            if (item.Block is { } block)
            {
                if (blockAtlas is not null)
                {
                    CheckFaces(item, block.Active, "active", blockAtlas);
                    if (block.Inactive is { } inactive) CheckFaces(item, inactive, "inactive", blockAtlas);
                }
                // Informational: big blocks (e.g. an 11x11 bay door) stretch a smaller region, which is legitimate.
                var visual = block.VisualSize;
                var walls = block.Active.Walls;
                if (!walls.IsEmpty && (walls.W != (int)visual.X || walls.H != (int)visual.Y))
                    Report(Severity.Info, Codes.RegionSizeDiffers,
                        $"'{item.Name}' walls region is {walls.W}x{walls.H} cells but its model face is {visual.X}x{visual.Y}", item.Line);
            }
            else if (spriteAtlas is not null)
            {
                CheckRegion(item, item.Sprite, "sprite", spriteAtlas);
            }
        }

        void CheckFaces(ItemDef item, FaceSet faces, string state, AtlasGrid atlas)
        {
            foreach (var (face, rect) in faces.Faces) CheckRegion(item, rect, $"{state} {face}", atlas);
        }

        void CheckRegion(ItemDef item, CellRect rect, string what, AtlasGrid atlas)
        {
            if (rect.IsEmpty) return;   // already reported by the parser
            if (!atlas.Contains(rect))
                Report(Severity.Error, Codes.RegionOutOfGrid,
                    $"'{item.Name}' {what} {rect} is outside the {atlas.Columns}x{atlas.Rows} grid of '{atlas.Def.Key}'", item.Line);
            else if (atlas.Classify(rect) == AlphaClass.Empty)
                Report(Severity.Warning, Codes.RegionEmpty,
                    $"'{item.Name}' {what} {rect} points at empty cells of '{atlas.Def.Key}' (served by {atlas.ServedBy})", item.Line);
        }
    }
}
