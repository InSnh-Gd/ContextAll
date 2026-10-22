namespace ContextAll.Host.Tests;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["two-daemon"])
        {
            return await TwoDaemonTests.RunScenario();
        }

        Console.Error.WriteLine("The test host runs these tests.");
        return 2;
    }
}
