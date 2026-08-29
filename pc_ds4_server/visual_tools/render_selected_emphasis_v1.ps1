param(
    [Parameter(Mandatory = $true)][string]$BasePath,
    [Parameter(Mandatory = $true)][string]$SelectedPath,
    [Parameter(Mandatory = $true)][string]$LayoutPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common

$masterSize = 1254
$layout = Get-Content -Raw -LiteralPath $LayoutPath | ConvertFrom-Json
$centerX = [single]$layout.wheelCenter.x
$centerY = [single]$layout.wheelCenter.y
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($output) | Out-Null

function New-DecodedMaster([string]$Path) {
    $source = [System.Drawing.Bitmap]::new($Path)
    try {
        if ($source.Width -ne $masterSize -or $source.Height -ne $masterSize) {
            throw "Expected a ${masterSize}x${masterSize} master: $Path"
        }
        $decoded = [System.Drawing.Bitmap]::new(
            $source.Width,
            $source.Height,
            [System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($decoded)
        try {
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.DrawImageUnscaled($source, 0, 0)
        }
        finally {
            $graphics.Dispose()
        }
        return $decoded
    }
    finally {
        $source.Dispose()
    }
}

function New-RenderedLayer(
    [System.Drawing.Bitmap]$Source,
    [double]$RotationDegrees
) {
    $target = [System.Drawing.Bitmap]::new(
        $masterSize,
        $masterSize,
        [System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($target)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            if ($RotationDegrees -ne 0) {
                $graphics.TranslateTransform($centerX, $centerY)
                $graphics.RotateTransform([single]$RotationDegrees)
                $graphics.TranslateTransform(-$centerX, -$centerY)
            }
            $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
            try {
                $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
                $destination = [System.Drawing.Rectangle]::new(0, 0, $masterSize, $masterSize)
                $graphics.DrawImage(
                    $Source,
                    $destination,
                    0,
                    0,
                    $Source.Width,
                    $Source.Height,
                    [System.Drawing.GraphicsUnit]::Pixel,
                    $attributes)
            }
            finally {
                $attributes.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }
        return $target
    }
    catch {
        $target.Dispose()
        throw
    }
}

function Write-PArgbFile(
    [System.Drawing.Bitmap]$Bitmap,
    [string]$Path
) {
    $rectangle = [System.Drawing.Rectangle]::new(0, 0, $Bitmap.Width, $Bitmap.Height)
    $data = $Bitmap.LockBits(
        $rectangle,
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    try {
        $rowBytes = $Bitmap.Width * 4
        $payload = [byte[]]::new(12 + $rowBytes * $Bitmap.Height)
        [System.Text.Encoding]::ASCII.GetBytes('PARG').CopyTo($payload, 0)
        [System.BitConverter]::GetBytes([uint32]$Bitmap.Width).CopyTo($payload, 4)
        [System.BitConverter]::GetBytes([uint32]$Bitmap.Height).CopyTo($payload, 8)
        for ($y = 0; $y -lt $Bitmap.Height; $y++) {
            $row = [System.IntPtr]::Add($data.Scan0, $y * $data.Stride)
            [System.Runtime.InteropServices.Marshal]::Copy(
                $row,
                $payload,
                12 + $y * $rowBytes,
                $rowBytes)
        }
        [System.IO.File]::WriteAllBytes($Path, $payload)
    }
    finally {
        $Bitmap.UnlockBits($data)
    }
}

$baseMaster = New-DecodedMaster $BasePath
$selectedMaster = New-DecodedMaster $SelectedPath
try {
    $scaledBase = New-RenderedLayer $baseMaster 0
    try {
        $scaledBase.Save(
            [System.IO.Path]::Combine($output, 'base-static.png'),
            [System.Drawing.Imaging.ImageFormat]::Png)
        Write-PArgbFile $scaledBase ([System.IO.Path]::Combine($output, 'base-static.pargb'))
        for ($index = 0; $index -lt $layout.slotAnglesDegrees.Count; $index++) {
            $selectedLayer = New-RenderedLayer $selectedMaster ([double]$layout.slotAnglesDegrees[$index])
            try {
                $fullSelected = [System.Drawing.Bitmap]::new(
                    $masterSize,
                    $masterSize,
                    [System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
                try {
                    $graphics = [System.Drawing.Graphics]::FromImage($fullSelected)
                    try {
                        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                        $graphics.DrawImageUnscaled($scaledBase, 0, 0)
                        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
                        $graphics.DrawImageUnscaled($selectedLayer, 0, 0)
                    }
                    finally {
                        $graphics.Dispose()
                    }
                    $fullSelected.Save(
                        [System.IO.Path]::Combine($output, "selected-$($index + 1)-source.png"),
                        [System.Drawing.Imaging.ImageFormat]::Png)
                    Write-PArgbFile $fullSelected (
                        [System.IO.Path]::Combine($output, "selected-$($index + 1)-source.pargb"))
                }
                finally {
                    $fullSelected.Dispose()
                }
            }
            finally {
                $selectedLayer.Dispose()
            }
        }
    }
    finally {
        $scaledBase.Dispose()
    }
}
finally {
    $selectedMaster.Dispose()
    $baseMaster.Dispose()
}
