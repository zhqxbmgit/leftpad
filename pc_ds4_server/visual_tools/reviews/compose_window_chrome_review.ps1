param(
    [Parameter(Mandatory = $true)][string]$AcceptanceDirectory,
    [Parameter(Mandatory = $true)][string]$BeforeControllerCrop
)

Add-Type -AssemblyName System.Drawing

function New-ChromeCrop([string]$Path) {
    $source = [System.Drawing.Bitmap]::FromFile($Path)
    try {
        $width = 204
        $height = 106
        $x = $source.Width - $width
        $crop = New-Object System.Drawing.Bitmap($width, $height)
        $graphics = [System.Drawing.Graphics]::FromImage($crop)
        try {
            $graphics.DrawImage(
                $source,
                (New-Object System.Drawing.Rectangle(0, 0, $width, $height)),
                (New-Object System.Drawing.Rectangle($x, 0, $width, $height)),
                [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally { $graphics.Dispose() }
        return $crop
    }
    finally { $source.Dispose() }
}

function Write-LabeledStrip(
    [System.Collections.Generic.List[System.Drawing.Bitmap]]$Images,
    [string[]]$Labels,
    [string]$OutputPath
) {
    $labelHeight = 40
    $gap = 16
    $width = $gap * ($Images.Count - 1)
    $imageHeight = 0
    foreach ($image in $Images) {
        $width += $image.Width
        $imageHeight = [Math]::Max($imageHeight, $image.Height)
    }
    $height = $imageHeight + $labelHeight
    $output = New-Object System.Drawing.Bitmap([int]$width, [int]$height)
    $graphics = [System.Drawing.Graphics]::FromImage($output)
    $font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(248, 247, 255))
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(45, 64, 126))
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
        $x = 0
        for ($index = 0; $index -lt $Images.Count; $index++) {
            $graphics.DrawString($Labels[$index], $font, $brush, $x + 6, 10)
            $graphics.DrawImageUnscaled($Images[$index], $x, $labelHeight)
            $x += $Images[$index].Width + $gap
        }
        $output.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $brush.Dispose()
        $font.Dispose()
        $graphics.Dispose()
        $output.Dispose()
    }
}

$pageNames = @('overview', 'controller', 'settings', 'logs')
$pageLabels = @('Overview', 'Controller', 'Settings', 'Logs')
$pageCrops = New-Object 'System.Collections.Generic.List[System.Drawing.Bitmap]'
try {
    foreach ($page in $pageNames) {
        $pageCrops.Add((New-ChromeCrop (Join-Path $AcceptanceDirectory "$page-window-chrome-fixed.png")))
    }
    Write-LabeledStrip `
        $pageCrops `
        $pageLabels `
        (Join-Path $AcceptanceDirectory 'window-chrome-four-page-comparison.png')
}
finally {
    foreach ($image in $pageCrops) { $image.Dispose() }
}

$before = [System.Drawing.Bitmap]::FromFile($BeforeControllerCrop)
$after = New-ChromeCrop (Join-Path $AcceptanceDirectory 'controller-window-chrome-fixed.png')
$beforeAfter = New-Object 'System.Collections.Generic.List[System.Drawing.Bitmap]'
try {
    $beforeAfter.Add((New-Object System.Drawing.Bitmap($before)))
    $beforeAfter.Add($after)
    Write-LabeledStrip `
        $beforeAfter `
        @('Before - Controller', 'After - Controller') `
        (Join-Path $AcceptanceDirectory 'window-chrome-before-after.png')
}
finally {
    $before.Dispose()
    foreach ($image in $beforeAfter) { $image.Dispose() }
}
