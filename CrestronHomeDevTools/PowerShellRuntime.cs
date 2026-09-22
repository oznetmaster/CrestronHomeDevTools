// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

namespace CrestronHomeDevTools;

/// <summary>The Windows automation prerequisite, independent of Windows PowerShell.</summary>
internal static class PowerShellRuntime
	{
	internal const string CommandName = "pwsh.exe";
	internal const string VersionGuard = "$ErrorActionPreference='Stop';if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion -lt [version]'7.6') { throw 'PowerShell 7.6 or later is required. Install it for all users before running DevTools Windows automation.' };";
	internal static string LocalExecutable
		{
		get
			{
			string path = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", CommandName);
			if (!File.Exists (path))
				throw new FileNotFoundException ("PowerShell 7.6 or later is required at Program Files\\PowerShell\\7\\pwsh.exe. Install PowerShell for all users; Windows PowerShell 5.1 is not supported.", path);
			return path;
			}
		}
	internal static string RequireSupportedVersion (string script) => VersionGuard + "\r\n" + script;
	}
