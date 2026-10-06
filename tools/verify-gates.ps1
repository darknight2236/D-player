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

Write-Host ('mode: {0}' -f $(if ($withShell) { 'Full (gates + WinUI shell check)' } else { 'Fast (gates only)' }))

Invoke-Step -Label 'gate 1/2: build the solution filter (Core + WPF + Tests)' `
            -Arguments @('build', 'D-player.slnf', '-c', 'Debug', '--nologo', '-v', 'q')

Invoke-Step -Label 'gate 2/2: test the solution filter (no --nologo here)' `
            -Arguments @('test', 'D-player.slnf', '-c', 'Debug', '-v', 'q')

if ($withShell) {
    Invoke-Step -Label 'shell check (not a gate): build the WinUI 3 project' `
                -Arguments @('build', 'D-player.WinUI/D-player.WinUI.csproj', '-c', 'Debug', '--nologo', '-v', 'q')
}

Write-Host ''
Write-Host 'all requested steps passed.'
