param(
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [Parameter(Mandatory = $true)][string]$HeadlessDll
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workRoot = Join-Path $ResultsDirectory 'full-process-work'
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$rows = [System.Collections.Generic.List[object]]::new()
$uniqueStdout = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$uniqueDatabases = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$total = [Diagnostics.Stopwatch]::StartNew()

function Invoke-OneRun {
    param([int]$Seed, [string]$Suffix)

    $directory = Join-Path $workRoot ("seed-{0:D4}-{1}" -f $Seed, $Suffix)
    New-Item -ItemType Directory -Path $directory | Out-Null

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $dotnet
    $startInfo.WorkingDirectory = $directory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @($HeadlessDll, '--turns', '20', '--seed', [string]$Seed, '--actors', 'scripted')) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(120000)) {
        $process.Kill($true)
        throw "Seed $Seed run $Suffix timed out."
    }
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
        throw "Seed $Seed run $Suffix exited $($process.ExitCode): $stderr"
    }

    $stdoutBytes = [Text.Encoding]::UTF8.GetBytes($stdout)
    $stdoutHash = ConvertTo-Hex ([Security.Cryptography.SHA256]::HashData($stdoutBytes))
    $databasePath = Join-Path $directory 'run.db'
    $databaseHash = (Get-FileHash -LiteralPath $databasePath -Algorithm SHA256).Hash

    [pscustomobject]@{
        Directory = $directory
        StdoutHash = $stdoutHash
        DatabaseHash = $databaseHash
    }
}

function ConvertTo-Hex([byte[]]$Bytes) {
    return [Convert]::ToHexString($Bytes)
}

for ($seed = 1; $seed -le 40; $seed++) {
    $pair = [Diagnostics.Stopwatch]::StartNew()
    $first = Invoke-OneRun -Seed $seed -Suffix 'a'
    $second = Invoke-OneRun -Seed $seed -Suffix 'b'
    $pair.Stop()

    $stdoutEqual = $first.StdoutHash -eq $second.StdoutHash
    $databaseEqual = $first.DatabaseHash -eq $second.DatabaseHash
    [void]$uniqueStdout.Add($first.StdoutHash)
    [void]$uniqueDatabases.Add($first.DatabaseHash)

    $rows.Add([pscustomobject]@{
        seed = $seed
        pair_seconds = [Math]::Round($pair.Elapsed.TotalSeconds, 6)
        cumulative_seconds = [Math]::Round($total.Elapsed.TotalSeconds, 6)
        stdout_equal = $stdoutEqual
        database_equal = $databaseEqual
        stdout_hash = $first.StdoutHash
        database_hash = $first.DatabaseHash
    })

    foreach ($directory in @($first.Directory, $second.Directory)) {
        $resolved = [IO.Path]::GetFullPath($directory)
        if (-not $resolved.StartsWith([IO.Path]::GetFullPath($workRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean unexpected directory: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }

    Write-Output "seed=$seed pair_seconds=$($pair.Elapsed.TotalSeconds.ToString('0.000')) stdout=$stdoutEqual db=$databaseEqual"
}

$total.Stop()
$rows | Export-Csv -LiteralPath (Join-Path $ResultsDirectory 'full_process_pairs.csv') -NoTypeInformation
$mismatches = @($rows | Where-Object { -not $_.stdout_equal -or -not $_.database_equal }).Count
$summary = [ordered]@{
    seeds = 40
    runs_per_seed = 2
    total_process_runs = 80
    elapsed_seconds = $total.Elapsed.TotalSeconds
    mismatches = $mismatches
    distinct_stdout_trajectories = $uniqueStdout.Count
    distinct_database_files = $uniqueDatabases.Count
    mean_pair_seconds = ($rows | Measure-Object -Property pair_seconds -Average).Average
    pairs_per_second = 40 / $total.Elapsed.TotalSeconds
}
[IO.File]::WriteAllText(
    (Join-Path $ResultsDirectory 'full_process_summary.json'),
    ($summary | ConvertTo-Json -Depth 4),
    [Text.UTF8Encoding]::new($false))

Write-Output ($summary | ConvertTo-Json -Depth 4)
if ($mismatches -ne 0) { exit 1 }
