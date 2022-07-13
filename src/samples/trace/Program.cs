static class Program
{
    public static int Main()
    {
        var trace = CallTrace.Capture();

        Console.WriteLine(trace);

        return trace.Frames.Length > 20 && trace.Frames[0].ManagedMethod?.Name == nameof(Main) ? 0 : 1;
    }
}
