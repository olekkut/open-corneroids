using System.Collections;

namespace OpenCorneroids.Simulation.Persistence.Nbt;

/// <summary>
/// An ordered list of unnamed NBT tags of a single <see cref="ItemType"/>.
/// </summary>
public sealed class NbtList : NbtTag, IList<NbtTag>, IReadOnlyList<NbtTag>
{
    private readonly List<NbtTag> _items = [];

    public override NbtTagType TagType => NbtTagType.List;

    public NbtTagType ItemType { get; set; } = NbtTagType.End;

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public NbtList() { }

    public NbtList(NbtTagType itemType)
    {
        ItemType = itemType;
    }

    public NbtList(string name, NbtTagType itemType) : base(name)
    {
        ItemType = itemType;
    }

    public NbtList(string name, NbtTagType itemType, IEnumerable<NbtTag> items) : base(name)
    {
        ItemType = itemType;
        if (items != null)
        {
            _items.AddRange(items);
        }
    }

    public NbtTag this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ItemType == NbtTagType.End)
            {
                ItemType = value.TagType;
            }
            else if (value.TagType != ItemType)
            {
                throw new ArgumentException($"Cannot assign tag of type {value.TagType} to list of {ItemType}.", nameof(value));
            }
            _items[index] = value;
        }
    }

    public void Add(NbtTag item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (ItemType == NbtTagType.End)
        {
            ItemType = item.TagType;
        }
        else if (item.TagType != ItemType)
        {
            throw new ArgumentException($"Cannot add tag of type {item.TagType} to list of {ItemType}.", nameof(item));
        }
        _items.Add(item);
    }

    public void Add(int value) => Add(new NbtInt(value));
    public void Add(string value) => Add(new NbtString(value));
    public void Add(byte value) => Add(new NbtByte(value));

    public void Clear() => _items.Clear();
    public bool Contains(NbtTag item) => _items.Contains(item);
    public void CopyTo(NbtTag[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public IEnumerator<NbtTag> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    public int IndexOf(NbtTag item) => _items.IndexOf(item);
    public void Insert(int index, NbtTag item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (ItemType == NbtTagType.End) ItemType = item.TagType;
        else if (item.TagType != ItemType) throw new ArgumentException($"Cannot insert tag of type {item.TagType} to list of {ItemType}.");
        _items.Insert(index, item);
    }
    public bool Remove(NbtTag item) => _items.Remove(item);
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public override string ToString() => $"TAG_List(\"{Name}\"): [{Count} entries of type {ItemType}]";
}
