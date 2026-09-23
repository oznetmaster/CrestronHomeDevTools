// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using CrestronHomeDevTools;
using CrestronHomeDevTools.Setup;

internal static class Program
{
 [STAThread]
 private static void Main (string[] args)
 {
  ApplicationConfiguration.Initialize ();
  string directory = args.Length == 2 && args[0] == "--store" ? Path.GetFullPath (args[1]) : DevToolsPrivateStore.DefaultDirectory;
  if (args.Length != 0 && !(args.Length == 2 && args[0] == "--store")) { MessageBox.Show ("Use --store followed by an existing local private-store directory, or launch without arguments."); return; }
  Application.Run (new SetupWindow (directory));
 }
}
