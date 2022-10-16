namespace Vezel.Ruptura.Memory.Code;

public readonly unsafe struct CodePlacement : IEquatable<CodePlacement>
{
    // Specifies a range of memory that a code allocation must start within. Both values are inclusive. Notably, the
    // allocation can extend beyond the highest address, as long as the first byte is reachable. A code manager is free
    // to shrink this range if required to satisfy e.g. OS constraints.

    public static CodePlacement Anywhere { get; } = new(null, (byte*)null - 1);

    public void* LowestAddress { get; }

    public void* HighestAddress { get; }

    public bool IsRange => LowestAddress != HighestAddress;

    private CodePlacement(void* lowestAddress, void* highestAddress)
    {
        Check.Argument(lowestAddress <= highestAddress);

        LowestAddress = lowestAddress;
        HighestAddress = highestAddress;
    }

    public static bool operator ==(CodePlacement left, CodePlacement right) => left.Equals(right);

    public static bool operator !=(CodePlacement left, CodePlacement right) => !left.Equals(right);

    public static CodePlacement Fixed(void* address)
    {
        return new(address, address);
    }

    public static CodePlacement Range(void* lowestAddress, void* highestAddress)
    {
        return new(lowestAddress, highestAddress);
    }

    public bool Contains(void* address)
    {
        return address >= LowestAddress && address <= HighestAddress;
    }

    public bool Equals(CodePlacement other)
    {
        return LowestAddress == other.LowestAddress && HighestAddress == other.HighestAddress;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is CodePlacement p && Equals(p);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine((nuint)LowestAddress, (nuint)HighestAddress);
    }

    public override string ToString()
    {
        return IsRange ? $"0x{(nuint)LowestAddress:x}..0x{(nuint)HighestAddress:x}" : $"0x{(nuint)LowestAddress:x}";
    }
}
