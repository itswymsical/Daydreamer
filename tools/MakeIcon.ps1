# Draws Daydreamer's app icon (the logo from the title bar: a white cloud on a purple-to-blue rounded
# square) at every size Windows uses, and packs them into src/Daydreamer.App/Daydreamer.ico.
# Run it again if the logo changes:   powershell -ExecutionPolicy Bypass -File tools\MakeIcon.ps1
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$output = Join-Path $PSScriptRoot "..\src\Daydreamer.App\Daydreamer.ico"
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

# The same colors and glyph as the logo in MainWindow.xaml (Segoe Fluent Icons' cloud, E753).
$purple = [Windows.Media.Color]::FromRgb(0x97, 0x75, 0xFA)
$blue = [Windows.Media.Color]::FromRgb(0x5C, 0x7C, 0xFA)
$font = New-Object Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")
$cloud = [string][char]0xE753

function Draw-Logo([int]$size) {
    # Big icons get a little breathing room around the square, like other Windows 11 app icons.
    $inset = if ($size -le 24) { 0 } else { [Math]::Round($size / 32) }
    $square = $size - 2 * $inset
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()

    $gradient = New-Object Windows.Media.LinearGradientBrush($purple, $blue, (New-Object Windows.Point(0, 0)), (New-Object Windows.Point(1, 1)))
    $radius = $square * 9 / 32
    $dc.DrawRoundedRectangle($gradient, $null, (New-Object Windows.Rect($inset, $inset, $square, $square)), $radius, $radius)

    # The cloud, centred by its actual outline (not the font's line box). Small icons get a bigger cloud so it reads.
    $scale = if ($size -le 24) { 0.66 } else { 0.56 }
    $typeface = [Windows.Media.Typeface]::new($font, [Windows.FontStyles]::Normal, [Windows.FontWeights]::Normal, [Windows.FontStretches]::Normal)
    $text = [Windows.Media.FormattedText]::new($cloud, [Globalization.CultureInfo]::InvariantCulture,
        [Windows.FlowDirection]::LeftToRight, $typeface, [double]($square * $scale), [Windows.Media.Brushes]::White, [double]1.0)
    $shape = $text.BuildGeometry((New-Object Windows.Point(0, 0)))
    $b = $shape.Bounds
    $dc.PushTransform((New-Object Windows.Media.TranslateTransform((($size - $b.Width) / 2 - $b.X), (($size - $b.Height) / 2 - $b.Y))))
    # At taskbar sizes the cloud's line is thinner than a pixel, so trace it with a slightly thicker pen.
    $pen = if ($size -le 32) { [Windows.Media.Pen]::new([Windows.Media.Brushes]::White, [double]($size / 40)) } else { $null }
    $dc.DrawGeometry([Windows.Media.Brushes]::White, $pen, $shape)
    $dc.Pop()
    $dc.Close()

    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    return , $stream.ToArray()
}

# An .ico file is a short table of contents followed by the pictures (PNGs are allowed since Windows Vista).
$images = foreach ($size in $sizes) { , (Draw-Logo $size) }
$file = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter($file)
$writer.Write([UInt16]0)              # reserved
$writer.Write([UInt16]1)              # 1 = icon
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $side = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] } # 0 means 256
    $writer.Write([byte]$side); $writer.Write([byte]$side)
    $writer.Write([byte]0); $writer.Write([byte]0)          # no palette
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)     # 1 plane, 32 bits per pixel
    $writer.Write([UInt32]$images[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($output), $file.ToArray())
"Wrote $([IO.Path]::GetFullPath($output)) ($($file.Length) bytes, sizes $($sizes -join ', '))"
