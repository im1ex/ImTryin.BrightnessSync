using ImTryin.WindowsConsoleService;

namespace ImTryin.BrightnessSync;

internal class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 1 && args[0].ToLowerInvariant() == "/manual")
        {
            Manual.Run();
            return;
        }

        ServiceApplication.Run(
            args,
            new ServiceInfo
            {
                Name = "BrightnessSync",
                DisplayName = "Brightness Sync",
                Description = "Synchronizes brightness between multiple monitors.",

                ConsoleServiceInfo = new ConsoleServiceInfo
                {
                    SingletonId = "BrightnessSync-49804384-a405-4285-a59e-7d87268f3f16"
                }
            },
            new ActualService());
    }
}