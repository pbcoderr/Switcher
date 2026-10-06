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
    $existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue

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
            foreach ($name in @('NetworkSwitcher.exe', 'installation.json', 'settings.json')) {
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
    if (-not (Test-Path -LiteralPath $source)) { throw 'Build the project first, then run Install.cmd from bin\Release or bin\Debug next to Switcher.exe.' }
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
