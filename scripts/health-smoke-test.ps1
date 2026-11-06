param([Parameter(Mandatory=$true)][string]$BaseUrl,[int]$Requests=50)
$uri = $BaseUrl.TrimEnd('/') + '/Dashboard/Health'
$failures = 0
$watch = [System.Diagnostics.Stopwatch]::StartNew()
1..$Requests | ForEach-Object {
  try { Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 20 | Out-Null }
  catch { $failures++ }
}
$watch.Stop()
[pscustomobject]@{ Url=$uri; Requests=$Requests; Failures=$failures; ElapsedSeconds=[math]::Round($watch.Elapsed.TotalSeconds,2); RequestsPerSecond=[math]::Round($Requests/$watch.Elapsed.TotalSeconds,2) } | Format-List
