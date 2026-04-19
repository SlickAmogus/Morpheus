Add-Type -AssemblyName System.Drawing

function Make-Png {
    param([string]$Path, [int]$R, [int]$G, [int]$B, [string]$Label)
    $bmp = New-Object System.Drawing.Bitmap 256, 256
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $col = [System.Drawing.Color]::FromArgb(255, $R, $G, $B)
    $bg = New-Object System.Drawing.SolidBrush $col
    $gfx.FillRectangle($bg, 0, 0, 256, 256)
    $font = New-Object System.Drawing.Font 'Consolas', 24, ([System.Drawing.FontStyle]::Bold)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF 0, 0, 256, 256
    $gfx.DrawString($Label, $font, [System.Drawing.Brushes]::White, $rect, $fmt)
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose()
    $bmp.Dispose()
}

$root = Join-Path $PSScriptRoot '..\avatars\default'
New-Item -ItemType Directory -Force -Path $root | Out-Null
Make-Png -Path (Join-Path $root 'idle_closed.png') -R 40 -G 40 -B 60 -Label "idle`nclosed"
Make-Png -Path (Join-Path $root 'idle_open.png')   -R 60 -G 60 -B 90 -Label "idle`nopen"
Make-Png -Path (Join-Path $root 'generic.png')     -R 50 -G 50 -B 75 -Label "generic"
Write-Host 'placeholder avatars written to' $root
