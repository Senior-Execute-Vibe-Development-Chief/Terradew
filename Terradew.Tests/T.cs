using System;

/// <summary>Tiny test helpers shared by every test file.</summary>
internal static class T
{
    public static int Check(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"ok  {name}");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine($"FAIL {name}: {error.Message}");
            return 1;
        }
    }

    public static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
