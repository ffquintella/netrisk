# Packaging image assets

Every image here is derived from the single source icon
[`src/GUIClient/Assets/NetRisk.ico`](../../../src/GUIClient/Assets/NetRisk.ico) so that the
installers, the macOS bundle and the Linux desktop entry all show the same artwork. They are
checked in rather than generated during the build: the conversions need ImageMagick and
`iconutil`, neither of which a Linux or Windows packaging runner is guaranteed to have.

Regenerate them (macOS, with ImageMagick installed) with:

```bash
ICO=src/GUIClient/Assets/NetRisk.ico
for s in 512 256 128 64; do
  magick "$ICO" -background none -resize ${s}x${s} -gravity center -extent ${s}x${s} \
    build/installers/assets/netrisk-${s}.png
done

# macOS volume/bundle icon
SET=$(mktemp -d)/netrisk.iconset && mkdir -p "$SET"
while read -r size name; do
  magick "$ICO" -background none -resize ${size}x${size} -gravity center -extent ${size}x${size} "$SET/$name.png"
done <<'SIZES'
16 icon_16x16
32 icon_16x16@2x
32 icon_32x32
64 icon_32x32@2x
128 icon_128x128
256 icon_128x128@2x
256 icon_256x256
512 icon_256x256@2x
512 icon_512x512
1024 icon_512x512@2x
SIZES
iconutil -c icns "$SET" -o build/installers/assets/netrisk.icns

# DMG background (640x400; the drag layout in Build.cs assumes these coordinates)
magick -size 640x400 gradient:'#1c2733'-'#0d1219' \
  -font /System/Library/Fonts/Helvetica.ttc \
  -fill '#9fb3c8' -pointsize 20 -gravity north -annotate +0+36 'Install NetRisk' \
  -stroke '#3d5266' -strokewidth 3 -fill none -draw 'line 250,205 385,205' \
  -stroke none -fill '#3d5266' -draw 'polygon 385,195 410,205 385,215' \
  -fill '#6b8299' -pointsize 13 -gravity south -annotate +0+34 'Drag NetRisk onto the Applications folder' \
  build/installers/assets/dmg-background.png

# MSIX tile assets
OUT=build/installers/windows/msix/Assets
for pair in "50 StoreLogo" "150 Square150x150Logo" "44 Square44x44Logo" "71 Square71x71Logo"; do
  set -- $pair
  magick "$ICO" -background none -resize ${1}x${1} -gravity center -extent ${1}x${1} "$OUT/$2.png"
done
magick "$ICO" -background none -resize 150x150 -gravity center -extent 310x150 "$OUT/Wide310x150Logo.png"
```
