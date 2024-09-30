# 导入必要的模块
Import-Module -Name Microsoft.PowerShell.Archive

# 定义目录路径
$directoryPath = ".\dist\BetterGI"
$outputJsonPath = "E:\HuiTask\BetterGIBuild\UploadGit\bettergi-installation-data\hash.json"
$destinationDir = "E:\HuiTask\BetterGIBuild\UploadGit\bettergi-installation-data\installation"

# 将相对路径转换为绝对路径
$absoluteDirectoryPath = (Resolve-Path -Path $directoryPath).Path

# 定义要跳过的目录
$excludedDirectories = @(
    ".\dist\BetterGI\Script",
    ".\dist\BetterGI\User"
)
# 将相对路径转换为绝对路径
$excludedDirectories = $excludedDirectories | ForEach-Object { (Resolve-Path -Path $_).Path }

# 初始化一个空的哈希表来存储文件路径和哈希值
$fileHashes = @{}

# 获取目录下的所有文件，包括子目录
$files = Get-ChildItem -Path $directoryPath -Recurse -File

foreach ($file in $files) {
    # 跳过已经是 .zip 的文件
    if ($file.Extension -eq ".zip") {
        continue
    }
    # 检查文件是否在要跳过的目录中
    $skipFile = $false
    foreach ($excludedDir in $excludedDirectories) {
        if ($file.FullName.StartsWith($excludedDir)) {
            $skipFile = $true
            break
        }
    }
    if ($skipFile) {
        Write-Host "Skipping file in excluded directory: $($file.FullName)"
        continue
    }

    # 计算文件的哈希值
    $hash = Get-FileHash -Path $file.FullName -Algorithm SHA256

    # 检查哈希值是否为空
    if ($null -eq $hash) {
        Write-Host "Failed to compute hash for file: $($file.FullName)"
        continue
    }

    # 计算相对路径
    $relativePath = $file.FullName.Replace($absoluteDirectoryPath, "").TrimStart("\\")

    # 将相对路径和哈希值添加到哈希表中
    $fileHashes[$relativePath] = $hash.Hash

    # 定义压缩文件的路径
    $zipFilePath = "$($file.FullName).zip"

    # 压缩文件并替换同名压缩文件
    Compress-Archive -Path $file.FullName -DestinationPath $zipFilePath -Force
}

# 将哈希表转换为 JSON 格式
$jsonContent = $fileHashes | ConvertTo-Json -Depth 10

# 使用 UTF-8 编码写入 JSON 文件
[System.IO.File]::WriteAllText($outputJsonPath, $jsonContent, [System.Text.Encoding]::UTF8)



# 获取所有 .zip 文件，包括子目录
$zipFiles = Get-ChildItem -Path $absoluteDirectoryPath -Recurse -Filter *.zip

foreach ($file in $zipFiles) {
    # 计算目标路径
    $relativePath = $file.FullName.Substring($absoluteDirectoryPath.Length)
    $destinationPath = Join-Path $destinationDir $relativePath

    # 创建目标目录
    $destinationDirPath = Split-Path $destinationPath
    if (-not (Test-Path $destinationDirPath)) {
        New-Item -ItemType Directory -Path $destinationDirPath -Force
    }

    # 拷贝文件
    Copy-Item -Path $file.FullName -Destination $destinationPath -Force
}

Remove-Item -Path $absoluteDirectoryPath -Recurse -Force