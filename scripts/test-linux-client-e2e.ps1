param(
    [int]$ServerPort = 53910,
    [string]$DeviceId = "linux-e2e-01",
    [string]$ComposeFile = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ComposeFile) {
    $ComposeFile = Join-Path $repoRoot "packaging\e2e\compose.yaml"
}
elseif (-not [System.IO.Path]::IsPathRooted($ComposeFile)) {
    $ComposeFile = Join-Path $repoRoot $ComposeFile
}
if (-not (Test-Path -LiteralPath $ComposeFile)) {
    throw "Compose 文件不存在：$ComposeFile"
}
$composeFile = $ComposeFile
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

function Invoke-FileUpload {
    param(
        [string]$Uri,
        [string]$Path
    )

    $client = [System.Net.Http.HttpClient]::new()
    $form = [System.Net.Http.MultipartFormDataContent]::new()
    $stream = [System.IO.File]::OpenRead($Path)
    $content = [System.Net.Http.StreamContent]::new($stream)
    try {
        $form.Add($content, "file", [System.IO.Path]::GetFileName($Path))
        $response = $client.PostAsync($Uri, $form).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw "上传失败（$([int]$response.StatusCode)）：$body"
        }
        return $body | ConvertFrom-Json
    }
    finally {
        $content.Dispose()
        $stream.Dispose()
        $form.Dispose()
        $client.Dispose()
    }
}

function Assert-LinuxClientBoundary {
    $healthJson = & docker compose -f $composeFile exec -T labelframe-client curl --fail --silent "http://127.0.0.1:53960/healthz"
    if ($LASTEXITCODE -ne 0) { throw "Linux Client 健康检查失败。" }
    $health = $healthJson | ConvertFrom-Json
    if ($health.status -ne "ok" -or $health.platform -ne "linux" -or -not $health.headless) {
        throw "Linux Client 健康信息与能力声明不符：$healthJson"
    }

    $pluginsJson = & docker compose -f $composeFile exec -T labelframe-client curl --fail --silent "http://127.0.0.1:53960/api/transport/plugins"
    if ($LASTEXITCODE -ne 0) { throw "Linux Client 传输插件查询失败。" }
    $plugins = @($pluginsJson | ConvertFrom-Json)
    if ($plugins.Count -ne 1 -or $plugins[0].id -ne "log") {
        throw "Linux Client 应仅暴露 log 传输，实际为：$pluginsJson"
    }

    $rootStatus = (& docker compose -f $composeFile exec -T labelframe-client curl --silent --output /dev/null --write-out "%{http_code}" "http://127.0.0.1:53960/").Trim()
    $pluginStatus = (& docker compose -f $composeFile exec -T labelframe-client curl --silent --output /dev/null --write-out "%{http_code}" "http://127.0.0.1:53960/api/plugins/installed").Trim()
    if ($rootStatus -ne "404" -or $pluginStatus -ne "404") {
        throw "Linux Client 能力边界异常：root=$rootStatus, plugins=$pluginStatus（预期均为 404）。"
    }
}

function Test-SharedApis {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "labelframe-shared-e2e-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    try {
        $template = Invoke-RestMethod -Uri "$baseUrl/api/templates/$([Uri]::EscapeDataString('Linux E2E 标签'))"
        if ($template.name -ne "Linux E2E 标签" -or $template.testData.code -ne "LF-E2E-001") {
            throw "模板详情回读不一致。"
        }
        $templates = @(Invoke-RestMethod -Uri "$baseUrl/api/templates")
        if (-not ($templates | Where-Object { $_.name -eq "Linux E2E 标签" })) {
            throw "模板列表未返回刚保存的模板。"
        }

        $previewDir = Join-Path $tempRoot "preview"
        New-Item -ItemType Directory -Path $previewDir | Out-Null
        $previewPath = Join-Path $previewDir "label-1.png"
        $previewBody = @{ data = @{ code = "LF-PREVIEW-001" } } | ConvertTo-Json -Depth 5
        Invoke-WebRequest -Method Post -Uri "$baseUrl/api/templates/$([Uri]::EscapeDataString('Linux E2E 标签'))/preview" -ContentType "application/json; charset=utf-8" -Body $previewBody -OutFile $previewPath | Out-Null
        & dotnet run --project $verifierProject --configuration Release --no-build -- $previewDir "LF-PREVIEW-001" | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "模板预览 PNG 条码解码失败。" }

        $packagePath = Join-Path $tempRoot "linux-e2e.lfpkg"
        Invoke-WebRequest -Uri "$baseUrl/api/templates/$([Uri]::EscapeDataString('Linux E2E 标签'))/export" -OutFile $packagePath | Out-Null
        Invoke-RestMethod -Method Delete -Uri "$baseUrl/api/templates/$([Uri]::EscapeDataString('Linux E2E 标签'))" | Out-Null
        $afterDelete = @(Invoke-RestMethod -Uri "$baseUrl/api/templates")
        if ($afterDelete | Where-Object { $_.name -eq "Linux E2E 标签" }) {
            throw "模板删除后仍出现在列表中。"
        }
        $importedName = Invoke-FileUpload -Uri "$baseUrl/api/templates/import" -Path $packagePath
        if ($importedName -ne "Linux E2E 标签") { throw "模板包导入返回名称异常：$importedName" }

        $excelPath = Join-Path $tempRoot "excel-template.xlsx"
        $excelBody = @{
            columns = @(
                @{ key = "code"; displayName = "编码" },
                @{ key = "zone"; displayName = "区域" }
            )
            sampleRow = @{ code = "A-01"; zone = "A" }
        } | ConvertTo-Json -Depth 6
        Invoke-WebRequest -Method Post -Uri "$baseUrl/api/import/excel-template" -ContentType "application/json; charset=utf-8" -Body $excelBody -OutFile $excelPath | Out-Null
        $excel = Invoke-FileUpload -Uri "$baseUrl/api/import/excel" -Path $excelPath
        if (@($excel.headers).Count -ne 2 -or @($excel.rows).Count -ne 1 -or $excel.rows[0][0] -ne "A-01") {
            throw "Excel 模板生成 / 导入回读异常。"
        }

        $logBody = @{ deviceId = $DeviceId; lines = @("release-e2e-a", "release-e2e-b") } | ConvertTo-Json -Depth 4
        $received = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/logs" -ContentType "application/json; charset=utf-8" -Body $logBody
        $logs = @(Invoke-RestMethod -Uri "$baseUrl/api/logs?deviceId=$([Uri]::EscapeDataString($DeviceId))")
        if ($received.received -ne 2 -or -not ($logs | Where-Object { $_.line -match "release-e2e-a" -and $_.line -match "release-e2e-b" })) {
            throw "设备日志回传 / 查询回读异常。"
        }
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force
        }
    }
}

function Submit-TestJob {
    param(
        [int]$LabelCount,
        [switch]$StartClientAfterSubmit
    )

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

    if ($StartClientAfterSubmit) {
        Invoke-Compose start labelframe-client
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

Assert-LinuxClientBoundary

$serverUi = Invoke-WebRequest -Uri $baseUrl
if ($serverUi.StatusCode -ne 200 -or $serverUi.Content -notmatch "<title>") {
    throw "Server 管理界面未随测试镜像启用。"
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

Test-SharedApis

$singleJob = Submit-TestJob -LabelCount 1
$batchJob = Submit-TestJob -LabelCount 3

$deviceBeforeRestart = (Invoke-RestMethod -Uri "$baseUrl/api/devices") |
    Where-Object { $_.deviceId -eq $DeviceId } |
    Select-Object -First 1
$lastSeenBefore = [DateTimeOffset]::Parse($deviceBeforeRestart.lastSeenAt)
Invoke-Compose stop labelframe-client
$offlineJob = Submit-TestJob -LabelCount 1 -StartClientAfterSubmit
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
    OfflineQueuedJobId = $offlineJob.ServerJobId
    PostRestartJobId = $postRestartJob.ServerJobId
    Result = "PASS"
} | Format-List
