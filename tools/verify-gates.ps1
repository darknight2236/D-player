#Requires -Version 5.1
<#
.SYNOPSIS
  Runs D-player's verification steps and stops loudly on the first failure.

.DESCRIPTION
  Vocabulary (keep every document on these three terms):

    GATE (runs on EVERY change, fast):
      1. dotnet build D-player.slnf -c Debug --nologo -v q   -> 0 warnings / 0 errors
      2. dotnet test  D-player.slnf -c Debug -v q            -> 180 passed / 0 failed
    SHELL CHECK (not a gate - nothing runs it automatically):
      3. dotnet build D-player.WinUI/D-player.WinUI.csproj -c Debug --nologo -v q
         Run it whenever D-player.Core's PUBLIC SURFACE changes (a ctor parameter, a new
         IFileDialogService member, HandleDoubleClickPlay's arity, the ViewedPlaylist
         setter, ...). That is exactly when the .slnf gate stays green while the shell
         quietly stops compiling. Incremental after the first restore: seconds.
    OPTIONAL:
      dotnet build D-player.sln -c Debug --nologo -v q   (whole solution, only to check
      the IDE "Build Solution" path; it drags in the Windows App SDK toolchain)

  -Fast runs the gates (1+2).  -Full runs the gates plus the shell check (1+2+3).

  Why the distinction matters: no gate compiles WinUI, so a change that only breaks the
  shell is invisible to the automated chain. A real case from 2026-10-07: a "dead using"
  cleanup in MainWindow.xaml.cs removed the namespace that actually carries MicaBackdrop,
  which the .slnf gate cannot see at all - only step 3 would ever have noticed.

  Note: 'dotnet test' must NOT get --nologo. Microsoft.Testing.Platform does not accept it;
  it then runs zero tests while printing "Passed: 0", which reads like a green run.
  --nologo is valid on 'dotnet build' only.

  Both build steps assert the FULL gate sentence, not just the exit code: 'dotnet build' exits 0
  when a build emits warnings, so "$LASTEXITCODE -eq 0" alone would print "ok" on a build that
  violates the "0 warnings / 0 errors" rule the docs state. Warnings are therefore counted from a
  quiet MSBuild file log of the same build (see Invoke-BuildStep for why the console text is not
  captured here, and Get-WarningEvidence for why the count cannot come from the summary line).
  The 'dotnet test' step keeps its previous behaviour (exit code only); its pass or fail is
  already carried by the exit code.

  Pure ASCII on purpose: PowerShell 5.1 reads BOM-less files as GBK on this machine.
#>
param(
    [switch]$Fast,
    [switch]$Full
)

$ErrorActionPreference = 'Stop'

if ($Fast -and $Full) { throw 'Choose either -Fast or -Full, not both.' }
$withShell = [bool]$Full

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location -LiteralPath $repoRoot

function Get-WarningEvidence {
    param([string[]]$Lines)

    # The scan is ASCII-only on purpose, because the summary line is not: on this machine
    # 'dotnet build' prints the Chinese word for "warning" there, which an ASCII script cannot match.
    # What IS locale-invariant is the per-warning prefix
    # "<file>(<line>,<col>): <area> warning <CODE>: <message>" - measured 2026-10-07 under a Chinese
    # CLI: csc emitted "warning CS0219" and the XAML compiler "XamlCompiler warning WMC1509", both with
    # the severity token and code in ASCII and only the message body localised.
    # An English "N Warning(s)" line is read for its number instead of being counted as a warning line,
    # so "0 Warning(s)" can never trip this.
    $hits = New-Object System.Collections.Generic.List[string]
    foreach ($line in $Lines) {
        if ($line -match '^\s*(\d+)\s+Warning\(s\)\s*$') {
            if ([int]$Matches[1] -gt 0) { $hits.Add($line) }
            continue
        }
        if ($line -imatch '\bwarning\b') { $hits.Add($line) }
    }
    return $hits
}

function Invoke-Step {
    param([string]$Label, [string[]]$Arguments)

    Write-Host ''
    Write-Host ('--- {0}' -f $Label)
    Write-Host ('    dotnet {0}' -f ($Arguments -join ' '))

    & dotnet @Arguments
    $code = $LASTEXITCODE

    if ($code -ne 0) {
        throw ('step failed (exit code {0}): dotnet {1}' -f $code, ($Arguments -join ' '))
    }
    Write-Host '    -> ok'
}

function Invoke-BuildStep {
    param([string]$Label, [string[]]$Arguments)

    # Why this is not just "exit code 0": the gate sentence is "0 warnings / 0 errors", but
    # 'dotnet build' exits 0 when a build only warns. Measured 2026-10-07 with a temporary
    # CS0219 in D-player.Core: the console showed the warning, the summary said "1 <warnings>", and
    # the exit code was 0 - so the old check would have printed "ok" on a gate violation.
    #
    # The warnings are counted from MSBuild's own file logger instead of from the console text,
    # because PowerShell 5.1 mangles a child process' CJK output as soon as it is captured (measured:
    # every capture path - Write-Host, Write-Output, forcing [Console]::OutputEncoding - lost the last
    # character of each CJK line). Streaming to the console is left exactly as it was; the child writes
    # the log itself, and it is read byte-wise as Latin-1 so no ASCII token can be eaten by a
    # multi-byte sequence.
    #
    # Scope, stated honestly: warnings come from the compilations that actually ran. A fully up-to-date
    # incremental build recompiles nothing and therefore reports nothing (measured); the assertion
    # covers the work the build did, which is the same thing a human reading the console gets.
    Write-Host ''
    Write-Host ('--- {0}' -f $Label)

    $log = Join-Path ([System.IO.Path]::GetTempPath()) ('dplayer-verify-gates-{0}.log' -f [Guid]::NewGuid().ToString('N'))
    $loggerSwitch = '/flp:logfile={0};verbosity=quiet;errorandwarningonly=true' -f $log
    Write-Host ('    dotnet {0} {1}' -f ($Arguments -join ' '), $loggerSwitch)

    try {
        & dotnet ($Arguments + @($loggerSwitch))
        $code = $LASTEXITCODE

        if ($code -ne 0) {
            throw ('step failed (exit code {0}): dotnet {1}' -f $code, ($Arguments -join ' '))
        }

        if (-not (Test-Path -LiteralPath $log)) {
            throw ('cannot assert the warning count: the build log was not written ({0})' -f $log)
        }

        $text = [System.Text.Encoding]::GetEncoding(28591).GetString([System.IO.File]::ReadAllBytes($log))
        $warnings = @(Get-WarningEvidence -Lines @($text -split "`r?`n"))
        if ($warnings.Count -gt 0) {
            # Echoed with every non-ASCII byte replaced: the log was read as Latin-1 on purpose, so a
            # localised message body would print as mojibake. The part that identifies the warning
            # (file, line, column, severity token, code, project) is ASCII and survives intact.
            foreach ($w in $warnings) {
                Write-Host ('    [warning] {0}' -f ($w -replace '[^\x20-\x7E]', '?'))
            }
            throw ('build reported {0} warning line(s); the gate is 0 warnings / 0 errors: dotnet {1}' -f $warnings.Count, ($Arguments -join ' '))
        }
    }
    finally {
        if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
    }

    Write-Host '    -> ok (exit 0, 0 warning lines)'
}

Write-Host ('mode: {0}' -f $(if ($withShell) { 'Full (gates + WinUI shell check)' } else { 'Fast (gates only)' }))

Invoke-BuildStep -Label 'gate 1/2: build the solution filter (Core + WPF + Tests), 0 warnings required' `
                 -Arguments @('build', 'D-player.slnf', '-c', 'Debug', '--nologo', '-v', 'q')

Invoke-Step -Label 'gate 2/2: test the solution filter (no --nologo here)' `
            -Arguments @('test', 'D-player.slnf', '-c', 'Debug', '-v', 'q')

if ($withShell) {
    Invoke-BuildStep -Label 'shell check (not a gate): build the WinUI 3 project, 0 warnings required' `
                     -Arguments @('build', 'D-player.WinUI/D-player.WinUI.csproj', '-c', 'Debug', '--nologo', '-v', 'q')
}

Write-Host ''
Write-Host 'all requested steps passed.'
