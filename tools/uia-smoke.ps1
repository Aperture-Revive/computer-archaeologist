# Headless UI smoke test for Computer Archaeologist.
#
# Drives the real WinUI window through UI Automation: starts a scan from Home, answers the scope
# dialog, waits for the run to finish, then walks the Discoveries, detail, Reports and Settings pages
# and reports what the shell actually displayed.
#
# Prerequisites:
#   * Build the app first (dotnet build "Computer Archaeologist.slnx" -c Debug -p:Platform=x64).
#   * Give the run something safe to scan by writing a settings.json under
#     %LOCALAPPDATA%\Computer Archaeologist that restricts the scope, for example:
#
#       "archaeology": { "minInterestingness": 0, "maxAiAnalysis": 0, "privacyMode": "Disabled",
#                        "includedRoots": [ "C:\\Users\\me\\source\\repos\\demo-fixture" ] }
#
#   Otherwise the default scope ("Entire computer") is used, which walks every fixed drive.
param(
    [string]$Exe = 'C:\Users\l7594\source\repos\Computer Archaeologist\Computer Archaeologist\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Computer Archaeologist.exe',
    [int]$TimeoutSeconds = 90
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$ErrorActionPreference = 'Stop'

function Find-ByName {
    param($Root, [string]$Name, [int]$Retries = 40)
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    for ($i = 0; $i -lt $Retries; $i++) {
        $found = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $found) { return $found }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Invoke-Element {
    param($Element)
    try {
        $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        return $true
    } catch [System.InvalidOperationException] { }

    try {
        $pattern = $Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $pattern.Select()
        return $true
    } catch [System.InvalidOperationException] { }
    Write-Output "WARN: no invokable pattern on '$($Element.Current.Name)'"
    return $false
}

function Get-Names {
    param($Root)
    $all = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $names = @()
    foreach ($item in $all) {
        $n = $item.Current.Name
        if (-not [string]::IsNullOrWhiteSpace($n)) { $names += $n }
    }
    return $names
}

$proc = Start-Process -FilePath $Exe -PassThru
Write-Output "STARTED pid=$($proc.Id)"

try {
    Start-Sleep -Seconds 8

    if ($proc.HasExited) { Write-Output "FAIL: process exited with $($proc.ExitCode)"; exit 1 }

    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $windowCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
    $window = $desktop.FindFirst([System.Windows.Automation.TreeScope]::Children, $windowCondition)

    if ($null -eq $window) { Write-Output 'FAIL: no window found'; exit 1 }
    Write-Output "WINDOW TITLE: $($window.Current.Name)"

    $names = Get-Names $window
    Write-Output "HOME NAV ITEMS: $(($names | Where-Object { $_ -in @('Home','Archaeology','Discoveries','Reports','Settings') }) -join ', ')"
    Write-Output "HOME HERO PRESENT: $($names -contains 'Computer Archaeologist')"
    Write-Output "START BUTTON PRESENT: $($names -contains 'Start Archaeology')"

    $start = Find-ByName $window 'Start Archaeology' 20
    if ($null -eq $start) { Write-Output 'FAIL: start button not found'; exit 1 }
    Invoke-Element $start
    Write-Output 'CLICKED: Start Archaeology'

    $scope = Find-ByName $window 'Choose what to explore' 40
    if ($null -eq $scope) {
        Write-Output 'FAIL: scope dialog did not open'
        Write-Output "NAMES AFTER CLICK: $((Get-Names $window) -join ' | ')"
        exit 1
    }
    Write-Output 'DIALOG: scope chooser opened'
    Write-Output "DIALOG OPTIONS: $((Get-Names $window | Where-Object { $_ -like 'Entire*' -or $_ -like 'Selected*' -or $_ -like 'Drive*' }) -join ', ')"

    $go = Find-ByName $window 'Begin archaeology' 20
    if ($null -eq $go) { Write-Output 'FAIL: begin button not found'; exit 1 }
    Invoke-Element $go
    Write-Output 'CLICKED: Begin archaeology'

    # Watch the shell while the run proceeds.
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $sawProgress = $false
    $lastSnapshot = ''
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if ($proc.HasExited) { Write-Output "FAIL: process exited during the run ($($proc.ExitCode))"; exit 1 }

        $snapshot = (Get-Names $window) -join ' | '
        if ($snapshot -match 'Files discovered|Currently examining|Candidates|AI analysed') { $sawProgress = $true }

        if ($snapshot -match 'fps|interesting') { }
        if ($snapshot -match 'worth looking at|No archaeological discoveries') { $lastSnapshot = $snapshot; break }
        $lastSnapshot = $snapshot
    }

    Write-Output "PROGRESS OBSERVED: $sawProgress"

    $finalNames = Get-Names $window
    $discoveryPage = ($finalNames | Where-Object { $_ -match 'artifacts worth looking at|No archaeological discoveries yet' }) -join ' ; '
    Write-Output "DISCOVERIES HEADLINE: $discoveryPage"

    $scores = $finalNames | Where-Object { $_ -match '^\d+([.,]\d+)?$' } | Select-Object -First 12
    Write-Output "SCORES VISIBLE: $($scores -join ', ')"

    $cards = $finalNames | Where-Object { $_ -match '\.(cs|txt|json)$' } | Select-Object -First 10
    Write-Output "ARTIFACT NAMES VISIBLE: $($cards -join ', ')"

    $detailButton = Find-ByName $window 'Details' 8
    if ($null -ne $detailButton) {
        Invoke-Element $detailButton | Out-Null
        Start-Sleep -Seconds 3
        $detailNames = Get-Names $window
        Write-Output "DETAIL PAGE OPENED: $($detailNames -contains 'Score breakdown')"
        Write-Output "DETAIL FACT PRESENT: $($detailNames -contains 'File information')"
        Write-Output "DETAIL PREVIEW PRESENT: $($detailNames -contains 'Preview')"
        Write-Output "DETAIL SCORE BREAKDOWN ROWS: $((($detailNames | Where-Object { $_ -in @('Age','Rarity','Folder context','File name','Project cluster','Personal','Modification pattern','Unusualness') }) | Select-Object -Unique) -join ', ')"
        Write-Output "DETAIL ACTIONS: $((($detailNames | Where-Object { $_ -in @('Open file','Open folder','Copy path') }) | Select-Object -Unique) -join ', ')"

        $navHome = Find-ByName $window 'Discoveries' 8
        if ($null -ne $navHome) { Invoke-Element $navHome | Out-Null; Start-Sleep -Seconds 2 }
    }

    foreach ($nav in @('Reports', 'Settings')) {
        $item = Find-ByName $window $nav 15
        if ($null -ne $item) {
            Invoke-Element $item
            Start-Sleep -Seconds 3
            $pageNames = Get-Names $window
            if ($nav -eq 'Reports') {
                Write-Output "REPORTS PAGE: $($pageNames -contains 'Computer Archaeology Report')"
                Write-Output "REPORT SECTIONS: $((($pageNames | Where-Object { $_ -match 'Most Interesting Discoveries|Forgotten Projects|Old Documents|Timeline|AI Observations|Final Summary' }) | Select-Object -Unique) -join ', ')"
                $reportHeadline = ($pageNames | Where-Object { $_ -match 'contains|candidates|particularly interesting' }) -join ' '
                Write-Output "REPORT OVERVIEW: $reportHeadline"
            }
            else {
                Write-Output "SETTINGS PAGE: $($pageNames -contains 'OpenAI-compatible API')"
                Write-Output "SETTINGS THEME OPTIONS: $((($pageNames | Where-Object { $_ -in @('Follow system','Light','Dark') }) | Select-Object -Unique) -join ', ')"
                Write-Output "SETTINGS PRIVACY NOTICE: $(($pageNames | Where-Object { $_ -match 'may send selected file metadata' }) -join ' ')"
                Write-Output "SETTINGS DISCOVERY SECTION: $($pageNames -contains 'File discovery')"
            }
        }
        else {
            Write-Output "FAIL: navigation item '$nav' not found"
        }
    }

    # Verify the Archaeology page reflects the finished run.
    $arch = Find-ByName $window 'Archaeology' 15
    if ($null -ne $arch) {
        Invoke-Element $arch | Out-Null
        Start-Sleep -Seconds 3
        $archNames = Get-Names $window
        Write-Output "ARCHAEOLOGY STAGES: $((($archNames | Where-Object { $_ -match 'Preparing the scan|Searching files|Collecting file metadata|Computing local interestingness|AI analysis of candidates|Combining scores|Generating report' }) | Select-Object -Unique) -join ', ')"
        Write-Output "ARCHAEOLOGY METRICS: $((($archNames | Where-Object { $_ -match '^(Files discovered|Candidates|AI analysed|Overall progress)$' }) | Select-Object -Unique) -join ', ')"
        Write-Output "ARCHAEOLOGY OUTCOME: $(($archNames | Where-Object { $_ -match 'worth looking at|archaeology complete|Run cancelled' }) -join ' ')"
        $logLines = $archNames | Where-Object { $_ -match '^\d{2}:\d{2}:\d{2}\s' }
        Write-Output "ARCHAEOLOGY LOG LINES: $($logLines.Count)"
        if ($logLines.Count -gt 0) { Write-Output "ARCHAEOLOGY LAST LOG: $($logLines[-1])" }
    }

    Write-Output 'SMOKE TEST COMPLETE'
}
finally {
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
        Write-Output 'STOPPED'
    }
}
