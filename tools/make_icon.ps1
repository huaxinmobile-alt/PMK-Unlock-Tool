# PMK Unlock Tool — app icon generator (WinFormsApp1\app.ico)
#   · "PMK" ကို ဘောင်အတွင်း အလိုအလျောက် fit ဖြစ်အောင် တွက်ပြီး ဆွဲတယ် (ဖြတ်မထွက်ရ — စစ်ပေးတယ်)
#   · 48px အထက်: "PMK" + "UNLOCK TOOL"၊ 16/32px: "PMK" ပဲ (အသေးမှာ စာလုံးမပေါ်တာကို ရှောင်)
#   · 256/128/64/48/32/16 PNG-in-ICO (Windows Vista+)
param(
    [string]$OutIcon = "$PSScriptRoot\..\WinFormsApp1\app.ico"
)
Add-Type -AssemblyName System.Drawing

function New-FittedFont([System.Drawing.Graphics]$g, [string]$text, [System.Drawing.FontStyle]$style,
                       [single]$maxW, [single]$maxH) {
    # maxW/maxH အတွင်း ဝင်အောင် font size (px) ကို အလိုအလျောက် ချိန်
    $size = [single]$maxH
    while ($size -gt 5) {
        $f = New-Object System.Drawing.Font("Segoe UI", $size, $style, [System.Drawing.GraphicsUnit]::Pixel)
        $m = $g.MeasureString($text, $f)
        if ($m.Width -le $maxW -and $m.Height -le $maxH) { return $f }
        $f.Dispose()
        $size -= 1
    }
    return New-Object System.Drawing.Font("Segoe UI", 5, $style, [System.Drawing.GraphicsUnit]::Pixel)
}

function New-PmkBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # ---- နောက်ခံ ----
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, [System.Drawing.Color]::FromArgb(255, 13, 25, 38), [System.Drawing.Color]::FromArgb(255, 26, 66, 102), 45.0)
    $g.FillRectangle($bg, $rect)

    $accentH = [Math]::Max(2, [int]($size * 0.055))
    $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 200, 120))
    $g.FillRectangle($accent, 0, 0, $size, $accentH)

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $green = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0, 230, 118))

    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center

    $small = $size -lt 48
    $pad = [single]($size * $(if ($small) { 0.05 } else { 0.10 }))   # အသေးမှာ ဘေး margin လျှော့
    $innerW = $size - ($pad * 2)

    if ($small) {
        # အသေး — "PMK" ပဲ၊ ကြီးကြီး (ဖတ်လို့ရအောင်)
        $f = New-FittedFont $g "PMK" ([System.Drawing.FontStyle]::Bold) $innerW ([single]($size * 0.62))
        $box = New-Object System.Drawing.RectangleF($pad, [single]($size * 0.20), $innerW, [single]($size * 0.72))
        $g.DrawString("PMK", $f, $white, $box, $fmt)
        $f.Dispose()
    }
    else {
        # အကြီး — "PMK" (အပေါ်) + "UNLOCK TOOL" (အောက်)
        $f1 = New-FittedFont $g "PMK" ([System.Drawing.FontStyle]::Bold) $innerW ([single]($size * 0.42))
        $box1 = New-Object System.Drawing.RectangleF($pad, [single]($size * 0.14), $innerW, [single]($size * 0.44))
        $g.DrawString("PMK", $f1, $white, $box1, $fmt)
        $f1.Dispose()

        $f2 = New-FittedFont $g "UNLOCK TOOL" ([System.Drawing.FontStyle]::Bold) ($innerW * 0.96) ([single]($size * 0.17))
        $box2 = New-Object System.Drawing.RectangleF($pad, [single]($size * 0.60), $innerW, [single]($size * 0.22))
        $g.DrawString("UNLOCK TOOL", $f2, $green, $box2, $fmt)
        $f2.Dispose()
    }

    $g.Dispose(); $bg.Dispose(); $accent.Dispose(); $white.Dispose(); $green.Dispose()
    return $bmp
}

# ---- ဆွဲ + စစ် (အနားဘောင်ကို မထိအောင်) ----
$sizes = @(256, 128, 64, 48, 32, 16)
$pngs = @()
$fail = 0
foreach ($s in $sizes) {
    $bmp = New-PmkBitmap $s
    # ink bounding box — နောက်ခံနဲ့ ကွဲတဲ့ pixel တွေ ဘယ်နေရာအထိ ရောက်လဲ
    $minX = $s; $maxX = -1; $minY = $s; $maxY = -1
    $accentTop = [Math]::Max(3, [int]($s * 0.08))   # accent bar ကို ချန် — စာသား ink ကိုပဲ တွက်
    for ($y = $accentTop; $y -lt $s; $y++) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            # နောက်ခံ (အနက်ပြာ) ထက် သိသိသာသာ လင်းတဲ့ pixel = စာသား ink (antialias ပါ မိအောင်)
            $bright = ($c.R + $c.G + $c.B) / 3
            $isInk = ($bright -gt 105) -or ($c.G -gt 150 -and $c.R -lt 120)
            if ($isInk) {
                if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { Write-Host "  [FAIL] ${s}px — စာသား မတွေ့ပါ"; $fail++; $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $pngs += , $ms.ToArray(); $ms.Dispose(); $bmp.Dispose(); continue }

    $mL = $minX; $mR = ($s - 1) - $maxX; $mT = $minY; $mB = ($s - 1) - $maxY
    $ok = ($mL -ge 0 -and $mR -ge 0 -and $mT -ge 0 -and $mB -ge 0)
    "  {0,3}px  ink x[{1}..{2}] y[{3}..{4}]  margin L{5} R{6} T{7} B{8}  {9}" -f $s, $minX, $maxX, $minY, $maxY, $mL, $mR, $mT, $mB, $(if ($ok) { "OK" } else { "CLIPPED!" })
    if (-not $ok) { $fail++ }

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $ms.Dispose(); $bmp.Dispose()
}

# ---- ICO container ----
$fs = [System.IO.File]::Create($OutIcon)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

if ($fail -gt 0) { Write-Host "ICON: $fail size(s) FAILED clipping check" -ForegroundColor Red; exit 1 }
Write-Host "ICON OK: $OutIcon ($((Get-Item $OutIcon).Length) bytes, $($sizes.Count) sizes, clipping check passed)" -ForegroundColor Green
