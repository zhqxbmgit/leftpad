param(
    [Parameter(Mandatory = $true)][string]$JobsPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common

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

$jobs = Get-Content -Raw -LiteralPath $JobsPath | ConvertFrom-Json
foreach ($job in $jobs) {
    $source = [System.Drawing.Bitmap]::new([string]$job.input)
    try {
        $decoded = [System.Drawing.Bitmap]::new(
            $source.Width,
            $source.Height,
            [System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($decoded)
            try {
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.DrawImageUnscaled($source, 0, 0)
            }
            finally {
                $graphics.Dispose()
            }
            Write-PArgbFile $decoded ([string]$job.output)
        }
        finally {
            $decoded.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}
