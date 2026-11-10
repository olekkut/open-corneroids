using Godot;
using OpenCorneroids.Content.Itemsets;
using OpenCorneroids.Game.Content;
using OpenCorneroids.Simulation.Items;

namespace OpenCorneroids.Game.World;

/// <summary>
/// Items floating in space, as the original drops them: a mined block leaves one of itself where
/// it was, standing still; it lasts 30 seconds; and once the player is within 1.5 m with room for it, it flies to them
/// and goes into the hotbar first, then the inventory. A stack thrown out of an item window moves along the view and
/// slows down (<c>sv_item_drag</c>, see <see cref="FloatingMotion"/>), and can't be picked up for its first
/// 2 seconds. Not saved, as they last only half a minute. Ours: moving items pass through blocks; the original makes
/// them slide along what they hit.
/// </summary>
public partial class FloatingItems : Node3D
{
    /// <summary><c>sv_item_lifetime</c>: 30 s, as in the original.</summary>
    public static double LifetimeSeconds => Settings.GameSettings.Cvars.Get(Simulation.Settings.Cvars.ItemLifetime);
    /// <summary><c>sv_item_pickup_reach</c>: 1.5 m, as in the original.</summary>
    public static float PickupReach => Settings.GameSettings.Cvars.Float(Simulation.Settings.Cvars.ItemPickupReach);
    /// <summary>
    /// <c>sv_inventory_pickup_whole</c>: what the hotbar can't take goes into the main inventory only if all of it fits,
    /// as in the original (see <see cref="Inventory.PickUp"/>).
    /// </summary>
    static bool PickUpWhole => Settings.GameSettings.Cvars.Bool(Simulation.Settings.Cvars.InventoryPickupWhole);
    /// <summary><c>sv_item_drag</c>: a moving item keeps ×0.98 of its speed per 1/60 s, as in the original.</summary>
    static double Drag => Settings.GameSettings.Cvars.Get(Simulation.Settings.Cvars.ItemDrag);
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
        public double PickupDelay;
        public System.Numerics.Vector3 Velocity;
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

    /// <summary>
    /// Leaves <paramref name="quantity"/> of an item floating at <paramref name="at"/>. An item the itemset does not
    /// define (kept in the inventory from a save made with other content) floats too, drawn as a grey square, so
    /// dropping it never deletes it.
    /// </summary>
    public void Drop(int itemId, int quantity, Vector3 at) => Drop(itemId, quantity, at, Vector3.Zero, 0);

    /// <summary>
    /// As <see cref="Drop(int, int, Vector3)"/>, moving at <paramref name="velocity"/> (slowing down by
    /// <c>sv_item_drag</c>) and out of reach of pickup for its first <paramref name="pickupDelay"/> seconds: a stack
    /// thrown out of an item window, as in the original.
    /// </summary>
    public void Drop(int itemId, int quantity, Vector3 at, Vector3 velocity, double pickupDelay)
    {
        if (quantity <= 0) return;
        var texture = _itemset.ItemsById.TryGetValue(itemId, out var def) ? _icons.Of(def) : null;
        texture ??= Placeholder;
        var sprite = new Sprite3D
        {
            Texture = texture,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Shaded = false,
            PixelSize = Size / Math.Max(1, texture.GetWidth()),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(sprite);
        sprite.GlobalPosition = at;
        _items.Add(new Item
        {
            ItemId = itemId,
            Quantity = quantity,
            Sprite = sprite,
            PickupDelay = Math.Max(0, pickupDelay),
            Velocity = new System.Numerics.Vector3(velocity.X, velocity.Y, velocity.Z),
        });
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
                int left = _inventory.PickUp(item.ItemId, item.Quantity, PickUpWhole);
                int taken = item.Quantity - left;
                if (taken > 0) PickedUp?.Invoke(NameOf(item.ItemId), taken);
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
            if (item.Velocity != System.Numerics.Vector3.Zero) Move(item, delta);
            if (item.Age < item.PickupDelay) continue;
            if (item.Sprite.GlobalPosition.DistanceTo(player) <= PickupReach && _inventory.CanPickUp(item.ItemId, item.Quantity, PickUpWhole))
            {
                item.Flying = 0;
                item.Velocity = System.Numerics.Vector3.Zero;
                item.From = item.Sprite.GlobalPosition;
            }
        }
    }

    static void Move(Item item, double delta)
    {
        var at = item.Sprite.GlobalPosition;
        var (position, velocity) = FloatingMotion.Step(new System.Numerics.Vector3(at.X, at.Y, at.Z), item.Velocity, Drag, delta);
        item.Sprite.GlobalPosition = new Vector3(position.X, position.Y, position.Z);
        item.Velocity = velocity;
    }

    string NameOf(int itemId) => _itemset.ItemsById.GetValueOrDefault(itemId)?.Name ?? $"Unknown item {itemId}";

    ImageTexture? _placeholder;

    /// <summary>What an item without a drawable icon looks like: a small grey square with a darker rim. Ours.</summary>
    ImageTexture Placeholder
    {
        get
        {
            if (_placeholder is not null) return _placeholder;
            const int side = 8;
            var image = Image.CreateEmpty(side, side, false, Image.Format.Rgba8);
            image.Fill(new Color(0.35f, 0.35f, 0.38f));
            image.FillRect(new Rect2I(1, 1, side - 2, side - 2), new Color(0.62f, 0.62f, 0.66f));
            return _placeholder = ImageTexture.CreateFromImage(image);
        }
    }

    void Remove(int index)
    {
        _items[index].Sprite.QueueFree();
        _items.RemoveAt(index);
    }
}
