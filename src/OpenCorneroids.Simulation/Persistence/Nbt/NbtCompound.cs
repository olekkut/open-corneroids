using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace OpenCorneroids.Simulation.Persistence.Nbt;

/// <summary>
/// A collection of uniquely named NBT tags. Tags are addressed case-insensitively.
/// </summary>
public sealed class NbtCompound : NbtTag, IEnumerable<NbtTag>
{
    private readonly Dictionary<string, NbtTag> _tags = new(StringComparer.OrdinalIgnoreCase);

    public override NbtTagType TagType => NbtTagType.Compound;

    public int Count => _tags.Count;

    public ICollection<string> Names => _tags.Keys;

    public ICollection<NbtTag> Tags => _tags.Values;

    public NbtCompound() { }

    public NbtCompound(string name) : base(name) { }

    public NbtCompound(string name, IEnumerable<NbtTag> tags) : base(name)
    {
        if (tags != null)
        {
            foreach (var tag in tags) Add(tag);
        }
    }

    public NbtTag this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            return _tags[name];
        }
        set
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(value);
            value.Name = name;
            _tags[name] = value;
        }
    }

    public void Add(NbtTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        _tags[tag.Name] = tag;
    }

    public void Set(string name, NbtTag tag)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(tag);
        tag.Name = name;
        _tags[name] = tag;
    }

    public bool Contains(string name) => _tags.ContainsKey(name);
    public bool ContainsKey(string name) => _tags.ContainsKey(name);

    public bool TryGetValue(string name, [NotNullWhen(true)] out NbtTag? tag) =>
        _tags.TryGetValue(name, out tag);

    public bool TryGet<T>(string name, [NotNullWhen(true)] out T? tag) where T : NbtTag
    {
        if (_tags.TryGetValue(name, out var raw) && raw is T typed)
        {
            tag = typed;
            return true;
        }
        tag = null;
        return false;
    }

    public T? Get<T>(string name) where T : NbtTag =>
        TryGet<T>(name, out var tag) ? tag : null;

    public string GetString(string name, string defaultValue = "") =>
        TryGet<NbtString>(name, out var tag) ? tag.Value : defaultValue;

    public int GetInt(string name, int defaultValue = 0) =>
        TryGet<NbtInt>(name, out var tag) ? tag.Value : defaultValue;

    public byte GetByte(string name, byte defaultValue = 0) =>
        TryGet<NbtByte>(name, out var tag) ? tag.Value : defaultValue;

    public short GetShort(string name, short defaultValue = 0) =>
        TryGet<NbtShort>(name, out var tag) ? tag.Value : defaultValue;

    public long GetLong(string name, long defaultValue = 0L) =>
        TryGet<NbtLong>(name, out var tag) ? tag.Value : defaultValue;

    public float GetFloat(string name, float defaultValue = 0f) =>
        TryGet<NbtFloat>(name, out var tag) ? tag.Value : defaultValue;

    public double GetDouble(string name, double defaultValue = 0.0) =>
        TryGet<NbtDouble>(name, out var tag) ? tag.Value : defaultValue;

    public byte[]? GetByteArray(string name) =>
        TryGet<NbtByteArray>(name, out var tag) ? tag.Value : null;

    public int[]? GetIntArray(string name) =>
        TryGet<NbtIntArray>(name, out var tag) ? tag.Value : null;

    public long[]? GetLongArray(string name) =>
        TryGet<NbtLongArray>(name, out var tag) ? tag.Value : null;

    public NbtCompound? GetCompound(string name) =>
        TryGet<NbtCompound>(name, out var tag) ? tag : null;

    public NbtList? GetList(string name) =>
        TryGet<NbtList>(name, out var tag) ? tag : null;

    public void SetByte(string name, byte value) => Set(name, new NbtByte(name, value));
    public void SetShort(string name, short value) => Set(name, new NbtShort(name, value));
    public void SetInt(string name, int value) => Set(name, new NbtInt(name, value));
    public void SetLong(string name, long value) => Set(name, new NbtLong(name, value));
    public void SetFloat(string name, float value) => Set(name, new NbtFloat(name, value));
    public void SetDouble(string name, double value) => Set(name, new NbtDouble(name, value));
    public void SetString(string name, string value) => Set(name, new NbtString(name, value));
    public void SetByteArray(string name, byte[] value) => Set(name, new NbtByteArray(name, value));
    public void SetIntArray(string name, int[] value) => Set(name, new NbtIntArray(name, value));
    public void SetLongArray(string name, long[] value) => Set(name, new NbtLongArray(name, value));

    public bool Remove(string name) => _tags.Remove(name);
    public void Clear() => _tags.Clear();

    public IEnumerator<NbtTag> GetEnumerator() => _tags.Values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _tags.Values.GetEnumerator();

    public override string ToString() => $"TAG_Compound(\"{Name}\"): {Count} entries";
}
