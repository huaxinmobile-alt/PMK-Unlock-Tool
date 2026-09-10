# PMK Unlock Tool — app icon generator (WinFormsApp1\app.ico)
# 256/128/64/48/32/16 px PNG ပုံများ ဆွဲပြီး ICO ဖိုင်တစ်ခုတည်းအဖြစ် စုစည်းတယ် (Windows Vista+ PNG-in-ICO)
param(
    [string]$OutIcon = "$PSScriptRoot\..\WinFormsApp1\app.ico"
)
Add-Type -AssemblyName System.Drawing

function New-PmkBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # နောက်ခံ — အနက်ပြာ gradient + အစိမ်း accent
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 13, 25, 38),
        [System.Drawing.Color]::FromArgb(255, 24, 62, 96),
        45.0)
    $g.FillRectangle($brush, $rect)

    # အပေါ်ဘက် accent လိုင်း
    $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 200, 120))
    $g.FillRectangle($accent, 0, 0, $size, [Math]::Max(2, [int]($size * 0.06)))

    # PMK — အဖြူ စာလုံးကြီး
    $pmkSize = [single]($size * 0.40)
    $fPmk = New-Object System.Drawing.Font("Segoe UI", $pmkSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString("PMK", $fPmk, $white, (New-Object System.Drawing.RectangleF(0, [single]($size * 0.16), $size, [single]($size * 0.42))), $fmt)

    # UNLOCK TOOL — အစိမ်း စာလုံးငယ်
    $subSize = [single]($size * 0.145)
    if ($subSize -lt 5) { $subSize = 5 }
    $fSub = New-Object System.Drawing.Font("Segoe UI", $subSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $green = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 230, 118))
    $g.DrawString("UNLOCK TOOL", $fSub, $green, (New-Object System.Drawing.RectangleF(0, [single]($size * 0.60), $size, [single]($size * 0.26))), $fmt)

    $g.Dispose(); $brush.Dispose(); $accent.Dispose(); $white.Dispose(); $green.Dispose(); $fPmk.Dispose(); $fSub.Dispose()
    return $bmp
}

$sizes = @(256, 128, 64, 48, 32, 16)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-PmkBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
}

# ICO container — ICONDIR + ICONDIRENTRY*n + PNG data
$fs = [System.IO.File]::Create($OutIcon)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)               # reserved
$bw.Write([UInt16]1)               # type = icon
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))   # width (0 = 256)
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))   # height
    $bw.Write([Byte]0)             # colors
    $bw.Write([Byte]0)             # reserved
    $bw.Write([UInt16]1)           # planes
    $bw.Write([UInt16]32)          # bpp
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Host "ICON WRITTEN: $OutIcon ($((Get-Item $OutIcon).Length) bytes, $($sizes.Count) sizes)"
