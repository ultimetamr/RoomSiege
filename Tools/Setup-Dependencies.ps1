[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SdkPath,
    [Parameter(Mandatory = $true)][string]$UnityEditorPath,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sdkRoot = (Resolve-Path -LiteralPath $SdkPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $sdkRoot 'package.json'))) {
    $sdkRoot = Join-Path $sdkRoot 'XR'
}
$sdkPackageFile = Join-Path $sdkRoot 'package.json'
if (-not (Test-Path -LiteralPath $sdkPackageFile)) { throw 'Pass the SDK XR directory or its parent.' }
$sdkPackage = Get-Content -LiteralPath $sdkPackageFile -Raw | ConvertFrom-Json
if ($sdkPackage.name -ne 'com.bytedance.pico.xr' -or $sdkPackage.version -ne '6.1.1') {
    throw 'This checkout requires the official PICO XR package 6.1.1.'
}
$editorRoot = (Resolve-Path -LiteralPath $UnityEditorPath).Path
if (Test-Path -LiteralPath $editorRoot -PathType Leaf) { $editorRoot = Split-Path $editorRoot -Parent }
if (-not (Test-Path -LiteralPath (Join-Path $editorRoot 'Unity.exe'))) {
    $editorRoot = Join-Path $editorRoot 'Editor'
}
$compositionArchive = Join-Path $editorRoot 'Data\Resources\PackageManager\Editor\com.unity.xr.compositionlayers-2.6.0.tgz'
if (-not (Test-Path -LiteralPath $compositionArchive)) {
    throw 'Composition Layers 2.6.0 was not found. Use the Unity 6000.6.8f1 Editor installation.'
}
$sdkDestination = Join-Path $projectRoot 'Packages\com.bytedance.pico.xr'
$loaderRelativePath = 'Runtime\Android\LoaderForUnitySDK.aar'
$winAudioRelativePath = 'SpatialAudio\Plugins\Win\x64\yggdrasil.dll'
$androidAudioRelativePath = 'SpatialAudio\Plugins\Android\libs\arm64-v8a\libyggdrasil.so'
foreach ($relativePath in @($loaderRelativePath, $winAudioRelativePath, $androidAudioRelativePath)) {
    if (-not (Test-Path -LiteralPath (Join-Path $sdkRoot $relativePath))) { throw "Missing SDK file: $relativePath" }
}
if ($VerifyOnly) {
    Write-Output 'Dependency inputs verified: PICO 6.1.1, custom OpenXR loader, spatial audio libraries, Composition Layers 2.6.0.'
    return
}
if (-not (Test-Path -LiteralPath $sdkDestination)) {
    Copy-Item -LiteralPath $sdkRoot -Destination $sdkDestination -Recurse
} else {
    $installedPackage = Get-Content -LiteralPath (Join-Path $sdkDestination 'package.json') -Raw | ConvertFrom-Json
    if ($installedPackage.name -ne $sdkPackage.name -or $installedPackage.version -ne $sdkPackage.version) {
        throw 'A different embedded SDK already exists. Keep a backup and select the required SDK explicitly.'
    }
}

# Modify only the private project copy; do not modify the downloaded SDK.
$utf8 = New-Object Text.UTF8Encoding($false)
$featurePath = Join-Path $sdkDestination 'Runtime\PICOFeature.cs'
$featureText = [IO.File]::ReadAllText($featurePath)
$oldLoader = 'CustomRuntimeLoaderName = "LoaderForUnitySDK_1_1_0"'
$newLoader = 'CustomRuntimeLoaderName = "LoaderForUnitySDK"'
if (-not $featureText.Contains($oldLoader) -and -not $featureText.Contains($newLoader)) {
    throw 'Unexpected SDK loader declaration; inspect it before applying a different compatibility fix.'
}
[IO.File]::WriteAllText($featurePath, $featureText.Replace($oldLoader, $newLoader), $utf8)

$samplePath = Join-Path $sdkDestination 'Enterprise\Sample\VirtualDisplay\OpenXR\VirtualDisplayDemo_OpenXR.cs'
$sampleText = [IO.File]::ReadAllText($samplePath)
$oldSurfaceCall = 'surface = OpenXRLayerUtility.GetLayerAndroidSurfaceObject(_overlay.GetInstanceID());'
if ($sampleText.Contains($oldSurfaceCall)) {
    if (-not $sampleText.Contains('using Unity.XR.CompositionLayers.Services;')) {
        $sampleText = $sampleText.Replace('using Unity.XR.CompositionLayers;', "using Unity.XR.CompositionLayers;`r`nusing Unity.XR.CompositionLayers.Services;")
    }
    $sampleText = $sampleText.Replace($oldSurfaceCall, "if (!CompositionLayerManager.TryGetLayerId(_overlay, out int layerId)) return false;`r`n            surface = OpenXRLayerUtility.GetLayerAndroidSurfaceObject(layerId);")
    [IO.File]::WriteAllText($samplePath, $sampleText, $utf8)
} elseif (-not $sampleText.Contains('CompositionLayerManager.TryGetLayerId')) {
    throw 'Unexpected SDK virtual-display sample; review its Unity 6 compatibility.'
}

$folderMetaPath = Join-Path $sdkDestination 'Runtime\CompositionLayers.meta'
if (Test-Path -LiteralPath $folderMetaPath) {
    $metaText = [IO.File]::ReadAllText($folderMetaPath)
    if ($metaText -match '(?m)^<<<<<<<') {
        $metaText = [regex]::Replace($metaText, '(?ms)^<<<<<<<[^\r\n]*\r?\n(.*?)^=======[^\r\n]*\r?\n.*?^>>>>>>>[^\r\n]*\r?\n', '$1')
        [IO.File]::WriteAllText($folderMetaPath, $metaText, $utf8)
    }
}

Copy-Item -LiteralPath $compositionArchive -Destination (Join-Path $projectRoot 'Packages\com.unity.xr.compositionlayers-2.6.0.tgz') -Force
$winDestination = Join-Path $projectRoot 'Assets\Audio\Plugins\Win64\PicoSpatializer.dll'
$androidDestination = Join-Path $projectRoot 'Assets\Audio\Plugins\Android\libPicoSpatializer.so'
foreach ($destinationPath in @($winDestination, $androidDestination)) {
    New-Item -ItemType Directory -Path (Split-Path $destinationPath -Parent) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $sdkDestination $winAudioRelativePath) -Destination $winDestination -Force
Copy-Item -LiteralPath (Join-Path $sdkDestination $androidAudioRelativePath) -Destination $androidDestination -Force
Write-Output 'Dependencies ready. Open this project with Unity 6000.6.8f1.'
