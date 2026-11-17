$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assetFolder = Join-Path $root 'src\Leitor\Assets'
New-Item -ItemType Directory -Force -Path $assetFolder | Out-Null
$bitmap = New-Object Drawing.Bitmap 256,256
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.Color]::Transparent)
$outline = New-Object Drawing.Drawing2D.GraphicsPath
foreach ($arc in @(@(0,0,64,64,180,90),@(191,0,64,64,270,90),@(191,191,64,64,0,90),@(0,191,64,64,90,90))) { $outline.AddArc($arc[0],$arc[1],$arc[2],$arc[3],$arc[4],$arc[5]) }
$outline.CloseFigure()
$brush = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#79583D'))
$pen = New-Object Drawing.Pen ([Drawing.Color]::White),10
$pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
$graphics.FillPath($brush,$outline)
$book = New-Object Drawing.Drawing2D.GraphicsPath
$book.AddBezier(45,66,75,52,97,58,128,76)
$book.AddBezier(128,76,159,58,181,52,211,66)
$book.AddLine(211,66,211,193)
$book.AddBezier(211,193,181,179,159,185,128,203)
$book.AddBezier(128,203,97,185,75,179,45,193)
$book.CloseFigure()
$graphics.DrawPath($pen,$book)
$graphics.DrawLine($pen,128,76,128,203)
$png = New-Object IO.MemoryStream
$bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)
$ico = [IO.File]::Create((Join-Path $assetFolder 'Leitor.ico'))
$writer = New-Object IO.BinaryWriter $ico
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
$writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22); $writer.Write($png.ToArray())
$writer.Dispose(); $png.Dispose(); $book.Dispose(); $pen.Dispose(); $brush.Dispose(); $outline.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
