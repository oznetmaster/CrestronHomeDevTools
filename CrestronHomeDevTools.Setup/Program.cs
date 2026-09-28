// Copyright (c) 2026 Neil Colvin. Licensed under the MIT License.
using CrestronHomeDevTools;
using CrestronHomeDevTools.Setup;

internal static class Program
{
 [STAThread]
 private static void Main (string[] args)
 {
  ApplicationConfiguration.Initialize ();
  if (args.Length == 4 && args[0] == "--operator-inbox" && args[2] == "--run-key")
  {
   using var inbox = new OperatorInboxContext (args[1], args[3]); Application.Run (inbox); Environment.ExitCode = inbox.Completed ? 0 : 4; return;
  }
  if (args.Length == 4 && args[0] == "--operator-request" && args[2] == "--request-sha256")
  {
   Application.Run (new OperatorWindow (new (args[1], args[3]))); return;
  }
  string directory = args.Length == 2 && args[0] == "--store" ? Path.GetFullPath (args[1]) : DevToolsPrivateStore.DefaultDirectory;
  if (args.Length != 0 && !(args.Length == 2 && args[0] == "--store")) { MessageBox.Show ("Use --store DIRECTORY, --operator-request DIRECTORY --request-sha256 SHA256, --operator-inbox DIRECTORY --run-key RUNKEY, or launch without arguments."); return; }
  Application.Run (new SetupWindow (directory));
 }
}
