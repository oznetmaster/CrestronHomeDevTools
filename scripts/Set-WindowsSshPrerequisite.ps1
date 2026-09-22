#requires -Version 7.6
#requires -PSEdition Core
# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
param(
    [Parameter(Mandatory=$true)][ValidateSet('Inspect','Install','Service','Firewall')][string]$Operation,
    [Parameter(Mandatory=$true)][string]$RemoteAddress
)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$WarningPreference='SilentlyContinue'
try {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    $admin=([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if(-not $admin){throw 'Run prerequisite setup from an elevated terminal.'}
    $capability='OpenSSH.Server~~~~0.0.1.0'
    $ruleName='CrestronHomeDevTools-OpenSSH'
    switch($Operation) {
        'Inspect' {
            $feature=Get-WindowsCapability -Online -Name $capability
            $service=Get-Service sshd -ErrorAction SilentlyContinue
            $rule=Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
            $ruleMatches=$false
            if($null -ne $rule) {
                $address=@($rule | Get-NetFirewallAddressFilter | Select-Object -ExpandProperty RemoteAddress)
                $port=$rule | Get-NetFirewallPortFilter
                $ruleMatches=$rule.Enabled -eq 'True' -and $rule.Direction -eq 'Inbound' -and $rule.Action -eq 'Allow' -and
                    $address.Count -eq 1 -and $address[0] -eq $RemoteAddress -and $port.LocalPort -eq '22' -and $port.Protocol -eq 'TCP'
            }
            $defaultRule=Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue
            if($null -ne $defaultRule) {
                $defaultAddress=@($defaultRule | Get-NetFirewallAddressFilter | Select-Object -ExpandProperty RemoteAddress)
                $ruleMatches=$ruleMatches -and $defaultAddress.Count -eq 1 -and $defaultAddress[0] -eq $RemoteAddress
            }
            [ordered]@{Installed=$feature.State.ToString() -eq 'Installed';
                ServiceReady=($null -ne $service -and $service.Status -eq 'Running' -and $service.StartType -eq 'Automatic');
                FirewallReady=$ruleMatches;
                BootUtc=(Get-CimInstance Win32_OperatingSystem -OperationTimeoutSec 10).LastBootUpTime.ToUniversalTime().ToString('o')
            } | ConvertTo-Json -Compress
        }
        'Install' {
            $result=Add-WindowsCapability -Online -Name $capability
            @{RestartRequired=[bool]$result.RestartNeeded} | ConvertTo-Json -Compress
        }
        'Service' {
            Set-Service sshd -StartupType Automatic
            Start-Service sshd
            '{}'
        }
        'Firewall' {
            # The reviewed plan scopes our rule and Windows' standard OpenSSH rule. Preserve other operator rules.
            $defaultRule=Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -ErrorAction SilentlyContinue
            if($null -ne $defaultRule) {
                $defaultRule | Get-NetFirewallAddressFilter | Set-NetFirewallAddressFilter -RemoteAddress $RemoteAddress
            }
            $rule=Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue
            if($null -ne $rule) {
                $rule | Set-NetFirewallRule -Enabled True -Direction Inbound -Action Allow -Profile Any
                $rule | Get-NetFirewallAddressFilter | Set-NetFirewallAddressFilter -RemoteAddress $RemoteAddress
                $rule | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol TCP -LocalPort 22
            } else {
                New-NetFirewallRule -Name $ruleName -DisplayName 'Crestron Home DevTools SSH access' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22 -RemoteAddress $RemoteAddress -Profile Any | Out-Null
            }
            '{}'
        }
    }
} catch {
    [Console]::Error.WriteLine('Windows SSH setup did not complete. Check administrator rights, Windows servicing and firewall policy. No reboot was performed.')
    exit 2
}
