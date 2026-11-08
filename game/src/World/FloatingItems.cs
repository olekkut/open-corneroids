using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Items floating in space, as the original drops them: a mined block leaves one of itself where
/// it was, standing still; it lasts 30 seconds; and once the player is within 1.5 m with room for it, it flies to them
/// and goes into the hotbar first, then the inventory. Not saved, as they last only half a minute.
/// </summary>
public partial class FloatingItems : Node3D
{
    /// <summary><c>sv_item_lifetime</c>: 30 s, as in the original.</summary>
    public static double LifetimeSeconds => Settings.GameSettings.Cvars.Get(Simulation.Settings.Cvars.ItemLifetime);
    /// <summary><c>sv_item_pickup_reach</c>: 1.5 m, as in the original.</summary>
    public static float PickupReach => Settings.GameSettings.Cvars.Float(Simulation.Settings.Cvars.ItemPickupReach);
    /// <summary>How long the flight to the player takes. Ours.</summary>
    const double FlightSeconds = 0.15;
    /// <summary>Drawn this big, in metres. Ours.</summary>
    const float Size = 0.35f;

    sealed class Item
    {
        public required int ItemId;
        public required int Quantity;
        public required Sprite3D Sprite;
        public double Age;
        public double Flying = -1;   // seconds into the flight to the player, or -1 while floating
        public Vector3 From;
    }

    readonly List<Item> _items = new();
    Inventory _inventory = null!;
    ItemsetDef _itemset = null!;
    ItemIcons _icons = null!;
    Func<Vector3> _player = null!;

    /// <summary>Raised when an item reaches the player, with its name and count, for the HUD.</summary>
    public event Action<string, int>? PickedUp;

    public void Setup(Inventory inventory, ItemsetDef itemset, ItemIcons icons, Func<Vector3> player) =>
        (_inventory, _itemset, _icons, _player) = (inventory, itemset, icons, player);

    public int Count => _items.Count;

    /// <summary>Leaves <paramref name="quantity"/> of an item floating at <paramref name="at"/>.</summary>
    public void Drop(int itemId, int quantity, Vector3 at)
    {
        if (quantity <= 0 || !_itemset.ItemsById.TryGetValue(itemId, out var def)) return;
        var texture = _icons.Of(def);
        var sprite = new Sprite3D
        {
            Texture = texture,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Shaded = false,
            PixelSize = texture is null ? 0.01f : Size / Math.Max(1, texture.GetWidth()),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(sprite);
        sprite.GlobalPosition = at;
        _items.Add(new Item { ItemId = itemId, Quantity = quantity, Sprite = sprite });
    }

    public override void _Process(double delta)
    {
        var player = _player();
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            item.Age += delta;
            if (item.Flying >= 0)
            {
                item.Flying += delta;
                float t = (float)Math.Min(1, item.Flying / FlightSeconds);
                item.Sprite.GlobalPosition = item.From.Lerp(player, t);
                if (t < 1) continue;
                int left = _inventory.Add(item.ItemId, item.Quantity);
                int taken = item.Quantity - left;
                if (taken > 0) PickedUp?.Invoke(_itemset.ItemsById[item.ItemId].Name, taken);
                if (left > 0)
                {
                    // Room ran out on the way: the rest floats where the player is.
                    item.Quantity = left;
                    item.Flying = -1;
                    continue;
                }
                Remove(i);
                continue;
            }
            if (item.Age >= LifetimeSeconds)
            {
                Remove(i);
                continue;
            }
            if (item.Sprite.GlobalPosition.DistanceTo(player) <= PickupReach && _inventory.CanAdd([new ItemStack(item.ItemId, 1)]))
            {
                item.Flying = 0;
                item.From = item.Sprite.GlobalPosition;
            }
        }
    }

    void Remove(int index)
    {
        _items[index].Sprite.QueueFree();
        _items.RemoveAt(index);
    }
}
