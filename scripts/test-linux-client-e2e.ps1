param(
    [int]$ServerPort = 53910,
    [string]$DeviceId = "linux-e2e-01",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot "packaging\e2e\compose.yaml"
$verifierProject = Join-Path $repoRoot "tools\LabelFrame.PrintImageVerifier\LabelFrame.PrintImageVerifier.csproj"
$env:LABELFRAME_E2E_SERVER_PORT = $ServerPort.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:LABELFRAME_DEVICE_ID = $DeviceId
$baseUrl = "http://127.0.0.1:$ServerPort"

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & docker compose -f $composeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose 执行失败：$($Arguments -join ' ')"
    }
}

function Wait-Until {
    param(
        [scriptblock]$Condition,
        [string]$FailureMessage,
        [int]$TimeoutSeconds = 90
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while (([DateTimeOffset]::UtcNow) -lt $deadline) {
        try {
            $value = & $Condition
            if ($null -ne $value) {
                return $value
            }
        }
        catch {
            # 服务启动和路由收敛期间允许短暂失败。
        }
        Start-Sleep -Milliseconds 500
    }

    throw $FailureMessage
}

function Submit-TestJob {
    param([int]$LabelCount)

    $requestId = "linux-e2e-$LabelCount-$([Guid]::NewGuid().ToString('N'))"
    $expectedCodes = @(1..$LabelCount | ForEach-Object {
        "LF-E2E-$LabelCount-$_"
    })
    $labels = @($expectedCodes | ForEach-Object {
        @{ data = @{ code = $_ } }
    })
    $body = @{
        requestId = $requestId
        targetDeviceId = $DeviceId
        templateName = "Linux E2E 标签"
        labels = $labels
    } | ConvertTo-Json -Depth 8

    $job = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/jobs" -ContentType "application/json; charset=utf-8" -Body $body
    $jobId = $job.jobId
    $replay = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/jobs" -ContentType "application/json; charset=utf-8" -Body $body
    if ($replay.jobId -ne $jobId) {
        throw "幂等重放产生了不同作业：$jobId / $($replay.jobId)"
    }

    $terminal = Wait-Until -FailureMessage "作业 $jobId 未在期限内完成。" -Condition {
        $current = Invoke-RestMethod -Uri "$baseUrl/api/jobs/$jobId"
        if ($current.status -in @("Completed", "Failed")) { return $current }
        return $null
    }

    if ($terminal.status -eq "Failed") {
        throw "作业失败：$($terminal.errorMessage)"
    }

    if ($terminal.completedItems -ne $LabelCount) {
        throw "作业 $jobId 完成数量异常：$($terminal.completedItems)/$LabelCount"
    }

    $localJob = Wait-Until -FailureMessage "Server 作业 $jobId 未找到对应的客户端本地作业。" -Condition {
        $jobsJson = & docker compose -f $composeFile exec -T labelframe-client curl --fail --silent "http://127.0.0.1:53960/api/jobs?limit=500"
        if ($LASTEXITCODE -ne 0) { return $null }
        return ($jobsJson | ConvertFrom-Json) |
            Where-Object { $_.requestId -eq $requestId } |
            Select-Object -First 1
    }
    if ($localJob.printImageCount -ne $LabelCount) {
        throw "客户端本地作业 $($localJob.jobId) 的 API PNG 数量异常：$($localJob.printImageCount)/$LabelCount"
    }

    $countCommand = "find '/var/lib/labelframe/client/print/$($localJob.jobId)' -type f -name '*.png' -size +0c | wc -l"
    $pngCount = (& docker compose -f $composeFile exec -T labelframe-client sh -c $countCommand).Trim()
    if ($LASTEXITCODE -ne 0 -or [int]$pngCount -ne $LabelCount) {
        throw "客户端本地作业 $($localJob.jobId) 的 PNG 数量异常：$pngCount/$LabelCount"
    }

    $copyRoot = Join-Path ([System.IO.Path]::GetTempPath()) "labelframe-e2e-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $copyRoot | Out-Null
    try {
        Invoke-Compose cp "labelframe-client:/var/lib/labelframe/client/print/$($localJob.jobId)/." $copyRoot | Out-Host
        & dotnet run --project $verifierProject --configuration Release --no-build -- $copyRoot @expectedCodes | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "客户端本地作业 $($localJob.jobId) 的 Compose PNG 条码解码失败。"
        }
    }
    finally {
        if (Test-Path -LiteralPath $copyRoot) {
            Remove-Item -LiteralPath $copyRoot -Recurse -Force
        }
    }

    return [pscustomobject]@{
        ServerJobId = $jobId
        ClientJobId = $localJob.jobId
        RequestId = $requestId
    }
}

& dotnet build $verifierProject --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Compose PNG 校验器构建失败。"
}

$upArgs = @("up", "-d")
if (-not $SkipBuild) {
    $upArgs += "--build"
}
Invoke-Compose @upArgs

Wait-Until -FailureMessage "Server 未在期限内就绪。" -Condition {
    $health = Invoke-RestMethod -Uri "$baseUrl/healthz"
    if ($health.status -eq "ok") { return $health }
    return $null
} | Out-Null

$device = Wait-Until -FailureMessage "Linux Client 未注册到 Server。" -Condition {
    $devices = Invoke-RestMethod -Uri "$baseUrl/api/devices"
    return $devices | Where-Object { $_.deviceId -eq $DeviceId -and $_.status -eq "Online" } | Select-Object -First 1
}

$templateBody = @{
    name = "Linux E2E 标签"
    group = "E2E"
    contract = @{
        name = "linux-e2e"
        version = "1.0"
        fields = @(@{ key = "code"; displayName = "编码"; isRequired = $true; type = "text" })
    }
    layout = @{
        name = "linux-e2e-layout"
        contractName = "linux-e2e"
        contractVersion = "1.0"
        widthMm = 50
        heightMm = 30
        elements = @(
            @{ type = "text"; literal = "Linux E2E"; xMm = 2; yMm = 2; widthMm = 46; fontHeightMm = 3; textAlign = "Center" },
            @{ type = "barcode"; sourceKey = "code"; xMm = 4; yMm = 9; widthMm = 42; heightMm = 15; displayValue = $true }
        )
    }
    testData = @{ code = "LF-E2E-001" }
} | ConvertTo-Json -Depth 10
Invoke-RestMethod -Method Post -Uri "$baseUrl/api/templates" -ContentType "application/json; charset=utf-8" -Body $templateBody | Out-Null

$singleJob = Submit-TestJob -LabelCount 1
$batchJob = Submit-TestJob -LabelCount 3

$deviceBeforeRestart = (Invoke-RestMethod -Uri "$baseUrl/api/devices") |
    Where-Object { $_.deviceId -eq $DeviceId } |
    Select-Object -First 1
$lastSeenBefore = [DateTimeOffset]::Parse($deviceBeforeRestart.lastSeenAt)
Invoke-Compose restart labelframe-client
$deviceAfterRestart = Wait-Until -FailureMessage "Linux Client 重启后未恢复心跳。" -Condition {
    $devices = Invoke-RestMethod -Uri "$baseUrl/api/devices"
    $current = $devices | Where-Object { $_.deviceId -eq $DeviceId -and $_.status -eq "Online" } | Select-Object -First 1
    if ($null -ne $current -and [DateTimeOffset]::Parse($current.lastSeenAt) -gt $lastSeenBefore) { return $current }
    return $null
}

$persistedRequestIds = @($singleJob.RequestId, $batchJob.RequestId)
Wait-Until -FailureMessage "Linux Client 重启后未保留重启前的本地作业。" -Condition {
    $jobsJson = & docker compose -f $composeFile exec -T labelframe-client curl --fail --silent "http://127.0.0.1:53960/api/jobs?limit=500"
    if ($LASTEXITCODE -ne 0) { return $null }
    $actualRequestIds = @(($jobsJson | ConvertFrom-Json).requestId)
    if (@($persistedRequestIds | Where-Object { $_ -notin $actualRequestIds }).Count -eq 0) { return $true }
    return $null
} | Out-Null

$postRestartJob = Submit-TestJob -LabelCount 1

[pscustomobject]@{
    Server = $baseUrl
    DeviceId = $deviceAfterRestart.deviceId
    DeviceStatus = $deviceAfterRestart.status
    SingleJobId = $singleJob.ServerJobId
    BatchJobId = $batchJob.ServerJobId
    PostRestartJobId = $postRestartJob.ServerJobId
    Result = "PASS"
} | Format-List
