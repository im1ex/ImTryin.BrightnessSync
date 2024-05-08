using System;
using ImTryin.BrightnessSync.Api;

namespace ImTryin.BrightnessSync;

internal class Manual
{
    public static void Run()
    {
        Console.WriteLine();
        int x = Console.CursorLeft;
        int y = Console.CursorTop;

        using var monitorCollection = new MonitorCollection();

        int activeMonitor = 0;

        while (true)
        {
            Render(x, y, monitorCollection, activeMonitor);

            var key = Console.ReadKey(true);

            var activeMonitorInstance = monitorCollection.MonitorInstances[activeMonitor];

            var change = (key.Modifiers & ConsoleModifiers.Control) != 0 ? 10 : 1;

            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                    if (activeMonitor > 0)
                        activeMonitor--;
                    break;
                case ConsoleKey.RightArrow:
                    if (activeMonitor < monitorCollection.MonitorInstances.Count - 1)
                        activeMonitor++;
                    break;

                case ConsoleKey.DownArrow:
                    activeMonitorInstance.Brightness = (byte)Math.Max(0, activeMonitorInstance.Brightness - change);
                    break;
                case ConsoleKey.UpArrow:
                    activeMonitorInstance.Brightness = (byte)Math.Min(activeMonitorInstance.Brightness + change, 100);
                    break;
            }
        }
    }

    private static void Render(int x, int y, MonitorCollection monitorCollection, int activeMonitor)
    {
        Console.CursorLeft = x;
        Console.CursorTop = y;

        Console.Write("Monitors:       ");
        int i = 0;
        foreach (var monitorInstance in monitorCollection.MonitorInstances)
        {
            Console.BackgroundColor = activeMonitor == i ? ConsoleColor.DarkCyan : ConsoleColor.Black;
            Console.ForegroundColor = activeMonitor == i ? ConsoleColor.Black : ConsoleColor.White;

            Console.Write("{0,16}", monitorInstance.ManufacturerName);

            i++;
        }

        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();

        Console.Write("Brightness:     ");
        i = 0;
        foreach (var monitorInstance in monitorCollection.MonitorInstances)
        {
            Console.BackgroundColor = activeMonitor == i ? ConsoleColor.DarkCyan : ConsoleColor.Black;
            Console.ForegroundColor = activeMonitor == i ? ConsoleColor.Black : ConsoleColor.White;

            Console.Write("{0,16}", monitorInstance.Brightness);

            i++;
        }

        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
    }
}