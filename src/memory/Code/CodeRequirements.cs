namespace Vezel.Ruptura.Memory.Code;

public readonly unsafe struct CodeRequirements
{
    public void* LowestAddress { get; }

    public void* HighestAddress { get; }

    public CodeRequirements(void* lowestAddress, void* highestAddress)
    {
        LowestAddress = lowestAddress;
        HighestAddress = highestAddress;
    }
}
