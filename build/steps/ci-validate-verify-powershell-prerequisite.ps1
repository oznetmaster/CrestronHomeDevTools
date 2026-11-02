if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion -lt [version]'7.6') { throw 'PowerShell 7.6 or later is required. See docs/PowerShell.md.' }
