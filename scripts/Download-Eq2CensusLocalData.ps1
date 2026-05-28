[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\artifacts\census\eq2"),
    [string]$BaseUrl = "https://census.daybreakgames.com",
    [string]$ServiceId = "s:example",
    [string[]]$Collections = @(
        "quest",
        "questgiver",
        "item",
        "npc",
        "faction",
        "zone",
        "world"
    ),
    [int]$PageSize = 3000,
    [int]$BatchSize = 100,
    [int]$RequestDelaySeconds = 7,
    [switch]$NoFieldSelection,
    [switch]$NoRawPages
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if ($PageSize -lt 1 -or $PageSize -gt 5000) {
    throw "PageSize must be between 1 and 5000. Census currently caps c:limit at 5000."
}

if ($BatchSize -lt 1 -or $BatchSize -gt 100) {
    throw "BatchSize must be between 1 and 100. Census full-object responses are capped at 100 rows."
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$rawRoot = Join-Path $resolvedOutput "raw"

New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
if (-not $NoRawPages) {
    New-Item -ItemType Directory -Path $rawRoot -Force | Out-Null
}

function Get-CensusRoot {
    $root = $BaseUrl.TrimEnd("/")
    $sid = $ServiceId.Trim("/")
    return "$root/$sid/json/get/eq2"
}

function Get-CensusUri {
    param(
        [Parameter(Mandatory)]
        [string]$Collection,
        [Parameter(Mandatory)]
        [string]$Query
    )

    $root = Get-CensusRoot
    return "$root/${Collection}?$Query"
}

function Read-CensusJson {
    param(
        [Parameter(Mandatory)]
        [string]$Uri,
        [Parameter(Mandatory)]
        [string]$OutFile
    )

    $displayUri = if ($Uri.Length -gt 240) { $Uri.Substring(0, 240) + "..." } else { $Uri }
    Write-Host "GET $displayUri"
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $Uri -Method Get -Headers @{ Accept = "application/json" } -TimeoutSec 180
            $content = if ($response.Content -is [byte[]]) {
                [System.Text.Encoding]::UTF8.GetString($response.Content)
            } else {
                [string]$response.Content
            }
            $json = $content | ConvertFrom-Json -Depth 100

            $errorProperty = $json.PSObject.Properties | Where-Object { $_.Name -eq "error" } | Select-Object -First 1
            if ($null -ne $errorProperty) {
                throw "Census returned error: $($errorProperty.Value)"
            }

            [System.IO.File]::WriteAllText($OutFile, $content, $utf8NoBom)
            return $json
        }
        catch {
            if ($attempt -eq 5) {
                throw
            }

            $delaySeconds = [Math]::Max(30, $RequestDelaySeconds * ($attempt + 1))
            Write-Warning "Request failed on attempt $attempt. Retrying in $delaySeconds seconds. $($_.Exception.Message)"
            Start-Sleep -Seconds $delaySeconds
        }
    }
}

function Write-JsonObject {
    param(
        [Parameter(Mandatory)]
        [object]$Value,
        [Parameter(Mandatory)]
        [string]$Path
    )

    $json = $Value | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText($Path, $json, $utf8NoBom)
}

function Copy-IfDifferentPath {
    param(
        [Parameter(Mandatory)]
        [string]$Source,
        [Parameter(Mandatory)]
        [string]$Destination
    )

    if ([System.IO.Path]::GetFullPath($Source) -eq [System.IO.Path]::GetFullPath($Destination)) {
        return
    }

    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Format-CensusId {
    param(
        [Parameter(Mandatory)]
        [object]$Id
    )

    if ($Id -is [IFormattable]) {
        return $Id.ToString($null, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [string]$Id
}

function Get-CollectionIds {
    param(
        [Parameter(Mandatory)]
        [string]$Collection,
        [Parameter(Mandatory)]
        [string]$ListProperty,
        [Nullable[int]]$ExpectedCount,
        [Parameter(Mandatory)]
        [string]$CollectionRawRoot
    )

    $ids = [System.Collections.Generic.List[string]]::new()
    $pages = @()
    $start = 0

    while ($true) {
        $pageFileName = "{0}-ids-{1:D6}.json" -f $Collection, $start
        $pagePath = if ($NoRawPages) {
            Join-Path $env:TEMP $pageFileName
        } else {
            Join-Path $CollectionRawRoot $pageFileName
        }

        $query = "c:limit=$PageSize&c:start=$start&c:show=id&c:sort=id"
        $page = Read-CensusJson -Uri (Get-CensusUri -Collection $Collection -Query $query) -OutFile $pagePath

        if (-not ($page.PSObject.Properties.Name -contains $ListProperty)) {
            throw "Census response for '$Collection' did not contain '$ListProperty'."
        }

        $pageItems = @($page.$ListProperty)
        foreach ($item in $pageItems) {
            if ($item.PSObject.Properties.Name -contains "id") {
                $ids.Add((Format-CensusId -Id $item.id))
            }
        }

        $returned = [int]$page.returned
        $limit = [int]$page.limit
        $pages += [ordered]@{
            file = if ($NoRawPages) { $null } else { "raw/$Collection/$pageFileName" }
            start = $start
            returned = $returned
            limit = $limit
        }

        if ($NoRawPages) {
            Remove-Item -LiteralPath $pagePath -Force
        }

        if ($null -ne $ExpectedCount -and $ids.Count -ge [int]$ExpectedCount) {
            break
        }

        if ($returned -le 0 -or ($returned -lt $limit -and $null -eq $ExpectedCount)) {
            break
        }

        $start += $returned
        Start-Sleep -Seconds $RequestDelaySeconds
    }

    return [ordered]@{
        ids = $ids
        pages = $pages
    }
}

function Get-CollectionObjects {
    param(
        [Parameter(Mandatory)]
        [string]$Collection,
        [Parameter(Mandatory)]
        [string]$ListProperty,
        [Parameter(Mandatory)]
        [System.Collections.Generic.List[string]]$Ids,
        [Parameter(Mandatory)]
        [string]$CollectionRawRoot,
        [Parameter(Mandatory)]
        [string]$CollectionOutput
    )

    $items = [System.Collections.Generic.List[object]]::new()
    $pages = @()
    $batchIndex = 0

    for ($offset = 0; $offset -lt $Ids.Count; $offset += $BatchSize) {
        $take = [Math]::Min($BatchSize, $Ids.Count - $offset)
        $batchIds = $Ids.GetRange($offset, $take)
        $pageFileName = "{0}-batch-{1:D6}.json" -f $Collection, $offset
        $pagePath = if ($NoRawPages) {
            Join-Path $env:TEMP $pageFileName
        } else {
            Join-Path $CollectionRawRoot $pageFileName
        }

        $idList = [string]::Join(",", $batchIds)
        $query = "id=$idList&c:limit=$take"
        $page = Read-CensusJson -Uri (Get-CensusUri -Collection $Collection -Query $query) -OutFile $pagePath

        if (-not ($page.PSObject.Properties.Name -contains $ListProperty)) {
            throw "Census response for '$Collection' did not contain '$ListProperty'."
        }

        $pageItems = @($page.$ListProperty)
        foreach ($item in $pageItems) {
            $items.Add($item)
        }

        $pages += [ordered]@{
            file = if ($NoRawPages) { $null } else { "raw/$Collection/$pageFileName" }
            offset = $offset
            requested = $take
            returned = [int]$page.returned
            limit = [int]$page.limit
        }

        if ($Ids.Count -le $BatchSize -and $batchIndex -eq 0) {
            Copy-IfDifferentPath -Source $pagePath -Destination $CollectionOutput
        }

        if ($NoRawPages) {
            Remove-Item -LiteralPath $pagePath -Force
        }

        $batchIndex++
        if ($offset + $take -lt $Ids.Count) {
            Start-Sleep -Seconds $RequestDelaySeconds
        }
    }

    if ($Ids.Count -eq 0) {
        $empty = [ordered]@{}
        $empty[$ListProperty] = @()
        $empty["returned"] = 0
        $empty["limit"] = 0
        Write-JsonObject -Value $empty -Path $CollectionOutput
    }
    elseif ($Ids.Count -gt $BatchSize) {
        $combined = [ordered]@{}
        $combined[$ListProperty] = $items
        $combined["returned"] = $items.Count
        $combined["limit"] = $items.Count
        Write-JsonObject -Value $combined -Path $CollectionOutput
    }

    return [ordered]@{
        items = $items
        pages = $pages
    }
}

function Get-CollectionByShow {
    param(
        [Parameter(Mandatory)]
        [string]$Collection,
        [Parameter(Mandatory)]
        [string]$ListProperty,
        [Parameter(Mandatory)]
        [string]$ShowFields,
        [Nullable[int]]$ExpectedCount,
        [Parameter(Mandatory)]
        [string]$CollectionRawRoot,
        [Parameter(Mandatory)]
        [string]$CollectionOutput
    )

    $items = [System.Collections.Generic.List[object]]::new()
    $pages = @()
    $start = 0
    $pageIndex = 0

    while ($true) {
        $pageFileName = "{0}-page-{1:D6}.json" -f $Collection, $start
        $pagePath = if ($NoRawPages) {
            Join-Path $env:TEMP $pageFileName
        } else {
            Join-Path $CollectionRawRoot $pageFileName
        }

        $query = "c:limit=$PageSize&c:start=$start&c:show=$ShowFields&c:sort=id"
        $page = Read-CensusJson -Uri (Get-CensusUri -Collection $Collection -Query $query) -OutFile $pagePath

        if (-not ($page.PSObject.Properties.Name -contains $ListProperty)) {
            throw "Census response for '$Collection' did not contain '$ListProperty'."
        }

        $pageItems = @($page.$ListProperty)
        foreach ($item in $pageItems) {
            $items.Add($item)
        }

        $returned = [int]$page.returned
        $limit = [int]$page.limit
        $pages += [ordered]@{
            file = if ($NoRawPages) { $null } else { "raw/$Collection/$pageFileName" }
            start = $start
            returned = $returned
            limit = $limit
            show = $ShowFields
        }

        if ($NoRawPages) {
            Remove-Item -LiteralPath $pagePath -Force
        }

        if ($null -ne $ExpectedCount -and $items.Count -ge [int]$ExpectedCount) {
            break
        }

        if ($returned -le 0 -or ($returned -lt $limit -and $null -eq $ExpectedCount)) {
            break
        }

        $start += $returned
        $pageIndex++
        Start-Sleep -Seconds $RequestDelaySeconds
    }

    if ($items.Count -le $PageSize -and $pageIndex -eq 0 -and -not $NoRawPages) {
        Copy-IfDifferentPath -Source (Join-Path $CollectionRawRoot ("{0}-page-{1:D6}.json" -f $Collection, 0)) -Destination $CollectionOutput
    } else {
        $combined = [ordered]@{}
        $combined[$ListProperty] = $items
        $combined["returned"] = $items.Count
        $combined["limit"] = $items.Count
        Write-JsonObject -Value $combined -Path $CollectionOutput
    }

    return [ordered]@{
        items = $items
        pages = $pages
    }
}

$datatypePath = Join-Path $resolvedOutput "datatypes.json"
$datatypeRawPath = if ($NoRawPages) { $datatypePath } else { Join-Path $rawRoot "datatypes.json" }
$datatypeUri = "$(Get-CensusRoot)/"
$datatypes = Read-CensusJson -Uri $datatypeUri -OutFile $datatypeRawPath
if (-not $NoRawPages) {
    Copy-IfDifferentPath -Source $datatypeRawPath -Destination $datatypePath
}

$datatypeCounts = @{}
foreach ($datatype in @($datatypes.datatype_list)) {
    $datatypeCounts[$datatype.name] = [int]$datatype.count
}

$fieldSelections = @{
    quest = "category,name,level,scales_with_level,is_tradeskill,ts,stage_list,last_update,crc,completion_text,shareable,starter_text,complete_shareable,tier,repeatable,reward_list,id"
    questgiver = "id,name,zone,quest_list,ts,last_update"
    item = "id,displayname,itemlevel,visible,ts,last_update"
}

$manifestCollections = @()
$requestCount = 1
Start-Sleep -Seconds $RequestDelaySeconds

foreach ($collection in $Collections) {
    $listProperty = "${collection}_list"
    $collectionOutput = Join-Path $resolvedOutput "$collection.json"
    $collectionRawRoot = Join-Path $rawRoot $collection
    if (-not $NoRawPages) {
        New-Item -ItemType Directory -Path $collectionRawRoot -Force | Out-Null
    }

    $expectedCount = if ($datatypeCounts.ContainsKey($collection)) { [int]$datatypeCounts[$collection] } else { $null }
    if (-not $NoFieldSelection -and $fieldSelections.ContainsKey($collection)) {
        $objectResult = Get-CollectionByShow -Collection $collection -ListProperty $listProperty -ShowFields $fieldSelections[$collection] -ExpectedCount $expectedCount -CollectionRawRoot $collectionRawRoot -CollectionOutput $collectionOutput
        $requestCount += @($objectResult.pages).Count
        $idCount = $objectResult.items.Count
        $idPages = @()
    } else {
        $idResult = Get-CollectionIds -Collection $collection -ListProperty $listProperty -ExpectedCount $expectedCount -CollectionRawRoot $collectionRawRoot
        $requestCount += @($idResult.pages).Count

        Start-Sleep -Seconds $RequestDelaySeconds

        $objectResult = Get-CollectionObjects -Collection $collection -ListProperty $listProperty -Ids $idResult.ids -CollectionRawRoot $collectionRawRoot -CollectionOutput $collectionOutput
        $requestCount += @($objectResult.pages).Count
        $idCount = $idResult.ids.Count
        $idPages = $idResult.pages
    }

    if ($collection -eq "questgiver") {
        Copy-IfDifferentPath -Source $collectionOutput -Destination (Join-Path $resolvedOutput "questgivers.json")
    }

    $manifestCollections += [ordered]@{
        name = $collection
        file = "$collection.json"
        census_count = if ($null -ne $expectedCount) { [int]$expectedCount } else { $null }
        id_count = $idCount
        returned = $objectResult.items.Count
        id_pages = $idPages
        object_pages = $objectResult.pages
    }

    Write-Host ("Saved {0} rows to {1}" -f $objectResult.items.Count, $collectionOutput)
    Start-Sleep -Seconds $RequestDelaySeconds
}

$manifest = [ordered]@{
    generated_at_utc = (Get-Date).ToUniversalTime().ToString("o")
    source = Get-CensusRoot
    page_size = $PageSize
    batch_size = $BatchSize
    field_selection_enabled = -not $NoFieldSelection
    request_delay_seconds = $RequestDelaySeconds
    request_count = $requestCount
    parser_ready_files = @("quest.json", "questgiver.json", "questgivers.json", "item.json")
    collections = $manifestCollections
}

Write-JsonObject -Value $manifest -Path (Join-Path $resolvedOutput "manifest.json")
Write-Host "Done. Local Census directory: $resolvedOutput"
