// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text;

internal static class ConsoleCommandLine
	{
	public static string[] Split (string line)
		{
		var result = new List<string> ();
		var word = new StringBuilder ();
		var quoted = false;
		var started = false;
		foreach (var character in line)
			{
			if (character == '"')
				{
				quoted = !quoted;
				started = true;
				}
			else if (char.IsWhiteSpace (character) && !quoted)
				{
				if (started)
					{
					result.Add (word.ToString ());
					word.Clear ();
					started = false;
					}
				}
			else
				{
				word.Append (character);
				started = true;
				}
			}
		if (quoted)
			throw new ArgumentException ("Close the double-quoted argument.");
		if (started)
			result.Add (word.ToString ());
		return result.ToArray ();
		}
	}