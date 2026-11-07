namespace OpenCorneroids.Simulation.Persistence.Nbt;

/// <summary>
/// Abstract base class for all Named Binary Tag (NBT) elements.
/// </summary>
public abstract class NbtTag
{
    public string Name { get; set; } = string.Empty;

    public abstract NbtTagType TagType { get; }

    protected NbtTag() { }

    protected NbtTag(string name)
    {
        Name = name ?? string.Empty;
    }
}

public sealed class NbtByte : NbtTag
{
    public byte Value { get; set; }

    public override NbtTagType TagType => NbtTagType.Byte;

    public NbtByte() { }
    public NbtByte(byte value) => Value = value;
    public NbtByte(string name, byte value) : base(name) => Value = value;

    public override string ToString() => $"TAG_Byte(\"{Name}\"): {Value}";
}

public sealed class NbtShort : NbtTag
{
    public short Value { get; set; }

    public override NbtTagType TagType => NbtTagType.Short;

    public NbtShort() { }
    public NbtShort(short value) => Value = value;
    public NbtShort(string name, short value) : base(name) => Value = value;

    public override string ToString() => $"TAG_Short(\"{Name}\"): {Value}";
}

public sealed class NbtInt : NbtTag
{
    public int Value { get; set; }

    public override NbtTagType TagType => NbtTagType.Int;

    public NbtInt() { }
    public NbtInt(int value) => Value = value;
    public NbtInt(string name, int value) : base(name) => Value = value;

    public override string ToString() => $"TAG_Int(\"{Name}\"): {Value}";
}

public sealed class NbtLong : NbtTag
{
    public long Value { get; set; }

    public override NbtTagType TagType => NbtTagType.Long;

    public NbtLong() { }
    public NbtLong(long value) => Value = value;
    public NbtLong(string name, long value) : base(name) => Value = value;

    public override string ToString() => $"TAG_Long(\"{Name}\"): {Value}";
}

public sealed class NbtFloat : NbtTag
{
    public float Value { get; set; }

    public override NbtTagType TagType => NbtTagType.Float;

    public NbtFloat() { }
    public NbtFloat(float value) => Value = value;
    public NbtFloat(string name, float value) : base(name) => Value = value;

    public override string ToString() => $"TAG_Float(\"{Name}\"): {Value}";
}

public sealed class NbtDouble : NbtTag
{
    public double Value { get; set; }

    public override NbtTagType TagType => NbtTagType.Double;

    public NbtDouble() { }
    public NbtDouble(double value) => Value = value;
    public NbtDouble(string name, double value) : base(name) => Value = value;

    public override string ToString() => $"TAG_Double(\"{Name}\"): {Value}";
}

public sealed class NbtByteArray : NbtTag
{
    public byte[] Value { get; set; } = [];

    public override NbtTagType TagType => NbtTagType.ByteArray;

    public NbtByteArray() { }
    public NbtByteArray(byte[] value) => Value = value ?? [];
    public NbtByteArray(string name, byte[] value) : base(name) => Value = value ?? [];

    public override string ToString() => $"TAG_Byte_Array(\"{Name}\"): [{Value.Length} bytes]";
}

public sealed class NbtString : NbtTag
{
    public string Value { get; set; } = string.Empty;

    public override NbtTagType TagType => NbtTagType.String;

    public NbtString() { }
    public NbtString(string value) => Value = value ?? string.Empty;
    public NbtString(string name, string value) : base(name) => Value = value ?? string.Empty;

    public override string ToString() => $"TAG_String(\"{Name}\"): \"{Value}\"";
}

public sealed class NbtIntArray : NbtTag
{
    public int[] Value { get; set; } = [];

    public override NbtTagType TagType => NbtTagType.IntArray;

    public NbtIntArray() { }
    public NbtIntArray(int[] value) => Value = value ?? [];
    public NbtIntArray(string name, int[] value) : base(name) => Value = value ?? [];

    public override string ToString() => $"TAG_Int_Array(\"{Name}\"): [{Value.Length} ints]";
}

public sealed class NbtLongArray : NbtTag
{
    public long[] Value { get; set; } = [];

    public override NbtTagType TagType => NbtTagType.LongArray;

    public NbtLongArray() { }
    public NbtLongArray(long[] value) => Value = value ?? [];
    public NbtLongArray(string name, long[] value) : base(name) => Value = value ?? [];

    public override string ToString() => $"TAG_Long_Array(\"{Name}\"): [{Value.Length} longs]";
}
