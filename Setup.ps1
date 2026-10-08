param([switch]$Uninstall, [string]$OriginalSid)
$ErrorActionPreference = 'Stop'
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $sid = $identity.User.Value
    if ($OriginalSid -and $OriginalSid -ne $sid) {
        throw 'Run setup from an administrator account and confirm elevation for that same account.'
    }
    $admin = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) {
        $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -OriginalSid "{1}"' -f $PSCommandPath, $sid
        if ($Uninstall) { $arguments += ' -Uninstall' }
        $child = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
        exit $child.ExitCode
    }
    $installDir = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'TailscaleZapretSwitcher'
    $exe = Join-Path $installDir 'NetworkSwitcher.exe'
    $taskName = 'TailscaleZapretSwitcher-' + $sid
    $desktop = [Environment]::GetFolderPath('DesktopDirectory')
    $shortcutPath = Join-Path $desktop 'Tailscale - zapret.lnk'
    $serviceKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\zapret'
    $settingsFile = Join-Path $installDir 'installation.json'
    $engineNames = @('LICENSE', 'COPYING', 'SOURCE.md', 'SOURCE.zip')
    $existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath (Join-Path $installDir 'routing-recovery.json')) {
        throw 'Stop routing in Switcher using Stop / Restore before updating or uninstalling. If Switcher crashed, start it and restore the previous connections first.'
    }

    if ($Uninstall) {
        if ($existingTask) {
            Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        }
        if (Test-Path -LiteralPath $settingsFile) {
            $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
            # Restore only the startup setting changed by this installer.
            if ((Test-Path -LiteralPath $serviceKey) -and $settings.OriginalZapretStart -eq 2) {
                Set-Service -Name zapret -StartupType Automatic
            }
        }
        if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath -Force }
        if (Test-Path -LiteralPath $installDir) {
            $resolved = (Resolve-Path -LiteralPath $installDir).Path
            $expected = [IO.Path]::Combine([Environment]::GetFolderPath('ProgramFiles'), 'TailscaleZapretSwitcher')
            if ($resolved -ne $expected -or ((Get-Item -LiteralPath $installDir).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected installation path.' }
            # Delete only our known files; never recursively delete an installation directory.
            $engineDirectory = Join-Path $resolved 'Engine'
            if (Test-Path -LiteralPath $engineDirectory) {
                if ((Get-Item -LiteralPath $engineDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unexpected linked Engine directory.' }
                foreach ($name in ($engineNames + @('sing-box.exe', 'libcronet.dll'))) {
                    $engineFile = Join-Path $engineDirectory $name
                    if (Test-Path -LiteralPath $engineFile) {
                        if ((Get-Item -LiteralPath $engineFile).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unexpected linked engine file.' }
                        Remove-Item -LiteralPath $engineFile -Force
                    }
                }
                if (-not (Get-ChildItem -LiteralPath $engineDirectory -Force)) { Remove-Item -LiteralPath $engineDirectory }
            }
            $routingState = Join-Path $resolved 'routing-state'
            if (Test-Path -LiteralPath $routingState) {
                $stateResolved = (Resolve-Path -LiteralPath $routingState).Path
                if ($stateResolved -ne [IO.Path]::Combine($expected, 'routing-state') -or ((Get-Item -LiteralPath $routingState).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected routing state path.' }
                if (Get-ChildItem -LiteralPath $stateResolved -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Routing state contains a link. Remove that link before uninstalling.' }
                Remove-Item -LiteralPath $stateResolved -Recurse -Force
            }
            foreach ($name in @('NetworkSwitcher.exe', 'installation.json', 'settings.json', 'routing.json', 'routing-runtime.json', 'routing-last.log')) {
                $path = Join-Path $resolved $name
                if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
            }
            if (-not (Get-ChildItem -LiteralPath $resolved -Force)) { Remove-Item -LiteralPath $resolved }
        }
        Add-Type -AssemblyName System.Windows.Forms
        [Windows.Forms.MessageBox]::Show('Switcher removed. Tailscale and zapret were not uninstalled.', 'Network Switcher') | Out-Null
        exit 0
    }

    $source = Join-Path $PSScriptRoot 'Switcher.exe'
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'This build requires 64-bit Windows.' }
    if (-not (Test-Path -LiteralPath $source)) { throw 'Build the project first, then run Install.cmd from bin\Release or bin\Debug next to Switcher.exe.' }
    foreach ($name in $engineNames) {
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot ('Engine\' + $name)))) { throw ('Incomplete package: Engine\' + $name + ' is missing. Extract the full Switcher archive.') }
    }
    $service = Get-ItemProperty -LiteralPath $serviceKey
    if ($service.Start -eq 4) { throw 'The zapret service is disabled. Enable it before installing the switcher.' }
    if ((Test-Path -LiteralPath $installDir) -and ((Get-Item -LiteralPath $installDir).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Installation folder must not be a link.' }
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null

    # The elevated scheduled task must execute a binary protected from ordinary-user writes.
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
    foreach ($entry in @(@('S-1-5-18','FullControl'), @('S-1-5-32-544','FullControl'), @('S-1-5-32-545','ReadAndExecute'))) {
        $principal = New-Object Security.Principal.SecurityIdentifier($entry[0])
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($principal, $entry[1], 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $installDir -AclObject $acl
    if ($existingTask) { Stop-ScheduledTask -TaskName $taskName; Start-Sleep -Milliseconds 700 }
    # Refuse to overwrite links left at these fixed file paths.
    foreach ($path in @($exe, $settingsFile)) {
        if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected linked installation file.' }
    }
    Copy-Item -LiteralPath $source -Destination $exe -Force
    $engineDirectory = Join-Path $installDir 'Engine'
    if ((Test-Path -LiteralPath $engineDirectory) -and ((Get-Item -LiteralPath $engineDirectory).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected linked Engine directory.' }
    New-Item -ItemType Directory -Path $engineDirectory -Force | Out-Null
    foreach ($name in $engineNames) {
        $destination = Join-Path $engineDirectory $name
        if ((Test-Path -LiteralPath $destination) -and ((Get-Item -LiteralPath $destination).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected linked engine file.' }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('Engine\' + $name)) -Destination $destination -Force
    }
    # Unpack and verify the embedded engine without starting a VPN or tray instance.
    $prepared = Start-Process -FilePath $exe -ArgumentList '--prepare-engine' -WorkingDirectory $installDir -WindowStyle Hidden -Wait -PassThru
    if ($prepared.ExitCode -ne 0) { throw 'Bundled routing engine preparation failed.' }
    $userSettings = Join-Path $installDir 'settings.json'
    if ((Test-Path -LiteralPath $userSettings) -and ((Get-Item -LiteralPath $userSettings).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected linked settings file.' }
    # Fresh installations always start with the setup window; never import build-machine settings.
    if (-not (Test-Path -LiteralPath $settingsFile)) {
        @{ OriginalZapretStart = [int]$service.Start } | ConvertTo-Json | Set-Content -LiteralPath $settingsFile -Encoding UTF8
    }

    $action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $installDir
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $sid
    $principal = New-ScheduledTaskPrincipal -UserId $sid -LogonType Interactive -RunLevel Highest
    $taskSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $taskSettings -Description 'Tailscale / zapret tray switcher with configurable hotkeys and strategy.' -Force | Out-Null
    # Do not start zapret beside Tailscale automatically after a reboot.
    Set-Service -Name zapret -StartupType Manual
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = "$env:SystemRoot\System32\schtasks.exe"
    $shortcut.Arguments = '/Run /TN "' + $taskName + '"'
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = $exe + ',0'
    $shortcut.WindowStyle = 7
    $shortcut.Description = 'Tailscale / zapret switcher'
    $shortcut.Save()
    Start-ScheduledTask -TaskName $taskName
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show('Installed. Find T / Z in the system tray (possibly under the arrow). Right-click it to open Settings: folders, zapret strategy and hotkeys. Default switch key: Ctrl+Alt+F8. Existing settings are preserved. The switcher starts at sign-in.', 'Network Switcher') | Out-Null
}
catch {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Network Switcher: setup error') | Out-Null
    exit 1
}

