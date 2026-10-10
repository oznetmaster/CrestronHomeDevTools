// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
namespace CrestronHomeDevTools.DriverUpdates;
internal static class Program
    {
    [STAThread]
    private static void Main ()
        {
        ApplicationConfiguration.Initialize ();
        Application.Run (new MainWindow ());
        }
    }
