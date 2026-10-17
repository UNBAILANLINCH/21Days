# 完整贴图导入已触发 Unity 崩溃；仅缩小导入副本，完整导出与源文件保留。
param([string]$ModelPath, [string]$OutputFolder)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$null = New-Item -ItemType Directory -Path $OutputFolder -Force
$stream = [IO.File]::OpenRead((Resolve-Path -LiteralPath $ModelPath))
$reader = [IO.BinaryReader]::new($stream)
try {
    $stream.Position = 12
    $jsonSize = $reader.ReadInt32()
    if ($reader.ReadUInt32() -ne 0x4e4f534a) { throw 'GLB JSON chunk 无效' }
    $model = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($jsonSize)) | ConvertFrom-Json
    $null = $reader.ReadUInt32()
    if ($reader.ReadUInt32() -ne 0x004e4942) { throw 'GLB BIN chunk 无效' }
    $binaryStart = $stream.Position
    for ($index = 0; $index -lt $model.images.Count; $index++) {
        $view = $model.bufferViews[$model.images[$index].bufferView]
        $stream.Position = $binaryStart + $view.byteOffset
        $encoded = $reader.ReadBytes($view.byteLength)
        if ($encoded.Length -ne $view.byteLength) { throw '贴图数据不完整' }
        $memory = [IO.MemoryStream]::new($encoded, $false)
        $image = [Drawing.Image]::FromStream($memory)
        try {
            $ratio = [Math]::Min(1.0, 1024.0 / [Math]::Max($image.Width, $image.Height))
            $width = [Math]::Max(1, [int]($image.Width * $ratio))
            $height = [Math]::Max(1, [int]($image.Height * $ratio))
            $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $graphics = [Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $graphics.DrawImage($image, [Drawing.Rectangle]::new(0, 0, $width, $height))
                } finally { $graphics.Dispose() }
                $destination = Join-Path $OutputFolder ('image_{0:D3}.png' -f $index)
                $bitmap.Save($destination, [Drawing.Imaging.ImageFormat]::Png)
                if ($bitmap.Width -gt 1024 -or $bitmap.Height -gt 1024) { throw '贴图缩放自检失败' }
            } finally { $bitmap.Dispose() }
        } finally { $image.Dispose(); $memory.Dispose() }
    }
    Write-Output ('COURTYARD_TEXTURE_RESIZE_OK ' + $model.images.Count)
} finally { $reader.Dispose(); $stream.Dispose() }
