namespace Vezel.Ruptura.Memory.Code;

public readonly unsafe struct CodePlacement
{
    // Specifies a range of memory that a code allocation must start within. Both values are inclusive. Notably, the
    // allocation can extend beyond the highest address, as long as the first byte is reachable. A code manager is free
    // to shrink this range if required to satisfy e.g. OS constraints.

    public static CodePlacement Anywhere { get; } = new(null, (byte*)null - 1);

    public void* LowestAddress { get; }

    public void* HighestAddress { get; }

    public bool IsRange => LowestAddress != HighestAddress;

    CodePlacement(void* lowestAddress, void* highestAddress)
    {
        _ = lowestAddress <= highestAddress ? true : throw new ArgumentException(null);

        LowestAddress = lowestAddress;
        HighestAddress = highestAddress;
    }

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
}
