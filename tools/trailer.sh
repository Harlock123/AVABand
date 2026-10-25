#!/usr/bin/env bash
# Makes the promotional trailer: AVABand-trailer.mp4 (1080p, 30 fps, H.264 + AAC) and a short looping
# AVABand-trailer.webp and .gif for the README. The game's frames are rendered headlessly from the real
# windows (tests/Angband.Avalonia.Tests/TrailerFrames.cs: fixed seeds, effects and scenes stepped a frame
# at a time), so no desktop is captured; the title cards are drawn here with ImageMagick in the game's
# own fonts and colours, and the music is the game's own title theme (CC0, tools/title_theme.py).
#
#   tools/trailer.sh               render the frames, then cut the trailer
#   tools/trailer.sh --no-render   cut it again from the frames already rendered
#
# Everything goes to $AVABAND_TRAILER_OUT (default: trailer-out/ in the repository, which git ignores).
# Needs ffmpeg (libx264, aac, libwebp) and ImageMagick 7.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$(mkdir -p "${AVABAND_TRAILER_OUT:-$root/trailer-out}" && cd "${AVABAND_TRAILER_OUT:-$root/trailer-out}" && pwd)"
frames="$out/frames"
work="$out/work"
app="$root/src/Angband.Avalonia"
title_font="$app/Assets/Fonts/Cinzel-SemiBold.ttf"
text_font="$app/Assets/Fonts/DejaVuSansMono.ttf"
music="$app/soundpacks/cc0-dungeon-music/title_theme.ogg"
# The title screen's colours (Views/MainWindow.axaml).
ink="#f0d8a8"; dim="#a08a68"; ground="#050305"

if [[ "${1:-}" != "--no-render" ]]; then
  rm -rf "$frames"
  AVABAND_TRAILER_FRAMES="$frames" dotnet test "$root/tests/Angband.Avalonia.Tests" \
    --filter "FullyQualifiedName~TrailerFrames" --nologo -v quiet
fi
rm -rf "$work"
mkdir -p "$work"
minus() { awk -v a="$1" -v b="$2" 'BEGIN { printf "%.3f", a - b }'; }
ff() { ffmpeg -hide_banner -loglevel error -y "$@"; }
# Every piece is encoded alike (so they join without re-encoding): 1080p, 30 fps, yuv420p.
enc=(-c:v libx264 -preset medium -crf 14 -pix_fmt yuv420p -r 30 -an)
fade=0.25   # each section dips through black
pieces=()

# The cards' backdrop: the title picture, dark and soft.
magick "$app/art/title.jpg" -resize 1920x1080^ -gravity center -extent 1920x1080 -blur 0x6 \
  -fill "$ground" -colorize 72% "$work/backdrop.png"

# card NAME SECONDS LINE [SMALLER LINE]
card() {
  local name="$1" seconds="$2" line="$3" sub="${4:-}"
  local args=("$work/backdrop.png" -gravity center -font "$title_font" -fill "$ink" -pointsize 76)
  if [[ -n "$sub" ]]; then
    args+=(-annotate +0-50 "$line" -font "$title_font" -fill "$dim" -pointsize 46 -annotate +0+50 "$sub")
  else
    args+=(-annotate +0+0 "$line")
  fi
  # A rule under the words, as on the title screen.
  args+=(-stroke "$dim" -strokewidth 2 -draw "line 760,$([[ -n $sub ]] && echo 640 || echo 600) 1160,$([[ -n $sub ]] && echo 640 || echo 600)")
  magick "${args[@]}" "$work/$name.png"
  ff -loop 1 -framerate 30 -t "$seconds" -i "$work/$name.png" \
    -vf "fade=t=in:st=0:d=$fade,fade=t=out:st=$(minus "$seconds" "$fade"):d=$fade" "${enc[@]}" "$work/$name.mp4"
  pieces+=("$work/$name.mp4")
}

# shot NAME [CAPTION FROM TO]: a reel of rendered frames, scaled to 1080p, dipping in and out of black,
# with a caption along the bottom between FROM and TO seconds, if given.
shot() {
  local name="$1" caption="${2:-}" from="${3:-0}" to="${4:-0}" list="$frames/$1/list.ffconcat"
  local seconds; seconds=$(awk '/^duration/ { s += $2 } END { printf "%.3f", s }' "$list")
  local vf="[0:v]scale=1920:1080:flags=lanczos,fps=30"
  local inputs=(-f concat -safe 0 -i "$list")
  if [[ -n "$caption" ]]; then
    magick -size 1920x1080 xc:none -fill "#050305c8" -draw "rectangle 0,900 1920,1000" -gravity south \
      -font "$title_font" -pointsize 40 -fill "$ink" -annotate +0+100 "$caption" "$work/caption.png"
    inputs+=(-loop 1 -i "$work/caption.png")
    vf+="[v];[1:v]format=rgba,fade=t=in:st=$from:d=0.3:alpha=1,fade=t=out:st=$(minus "$to" 0.3):d=0.3:alpha=1[c];[v][c]overlay=0:0:shortest=0"
  fi
  vf+=",fade=t=in:st=0:d=$fade,fade=t=out:st=$(minus "$seconds" "$fade"):d=$fade"
  ff "${inputs[@]}" -filter_complex "$vf" -t "$seconds" "${enc[@]}" "$work/$name.mp4"
  pieces+=("$work/$name.mp4")
}

# label TEXT: a caption in the bottom-left corner, for the tile set cuts.
label() {
  magick -size 1920x1080 xc:none -gravity southwest -font "$title_font" -pointsize 40 \
    -fill "#000000b0" -annotate +62+58 "$1" -fill "$ink" -annotate +60+60 "$1" "$work/label.png"
}

# --- the cut ---------------------------------------------------------------------------------------

# Opening: the title screen, pushing in slowly.
ff -loop 1 -framerate 30 -t 4 -i "$frames/title/f00000.png" \
  -vf "scale=3840:2160,zoompan=z='1+0.05*on/120':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=1:s=1920x1080:fps=30,fade=t=in:st=0:d=0.6,fade=t=out:st=3.75:d=$fade" \
  "${enc[@]}" "$work/00-title.mp4"
pieces+=("$work/00-title.mp4")

card 01-card 2.2 "Angband 4.2, rebuilt" "for Windows, macOS and Linux"

# Character creation: the dialog, on the dark backdrop.
mkdir -p "$work/creation"
for f in "$frames/creation/"*.png; do
  magick "$work/backdrop.png" \( "$f" -resize x1000 \) -gravity center -composite "$work/creation/$(basename "$f")"
done
cp "$frames/creation/list.ffconcat" "$work/creation/"
frames_saved="$frames"; frames="$work"; shot creation; frames="$frames_saved"

shot stairs

card 02-card 2.0 "Bolts, balls and dragon's breath"
shot magic

card 03-card 2.0 "Hire a sword at the Prancing Pony"
shot inn
shot hireling "Frerin, a dwarf warrior hired at the inn, fights at your side" 0.3 3.2

card 04-card 2.0 "Your fallen heroes walk again"
shot ghost "\"Grip, Farmer Maggot's Dog ended me here. You will not leave so easily!\"" 0.8 2.5

card 05-card 2.4 "Merchant caravans," "quests and the daily dungeon"
shot caravan

card 06-card 2.0 "Nine tile sets" "— or classic letters"
# Quick cuts, each named; one dip into the first and out of the last.
mapfile -t sets < "$frames/tilesets.tsv"
for i in "${!sets[@]}"; do
  reel="${sets[$i]%%$'\t'*}"; name="${sets[$i]#*$'\t'}"
  label "$name"
  list="$frames/$reel/list.ffconcat"
  seconds=$(awk '/^duration/ { s += $2 } END { printf "%.3f", s }' "$list")
  vf="[0:v]scale=1920:1080:flags=lanczos,fps=30[v];[v][1:v]overlay=0:0"
  (( i == 0 )) && vf+=",fade=t=in:st=0:d=$fade"
  (( i == ${#sets[@]} - 1 )) && vf+=",fade=t=out:st=$(minus "$seconds" "$fade"):d=$fade"
  ff -f concat -safe 0 -i "$list" -loop 1 -i "$work/label.png" -filter_complex "$vf" -t "$seconds" "${enc[@]}" "$work/$reel.mp4"
  pieces+=("$work/$reel.mp4")
done

# Closing: the logo, where to get it, and credits for the music and the tiles shown.
magick "$work/backdrop.png" \( "$app/art/title-logo.png" -resize 900x \) -gravity north -geometry +0+190 -composite \
  -gravity center -font "$title_font" -fill "$ink" -pointsize 58 -annotate +0+120 "Free, open source" \
  -font "$text_font" -fill "$ink" -pointsize 40 -annotate +0+205 "github.com/Harlock123/AVABand" \
  -font "$text_font" -fill "$dim" -pointsize 19 \
  -annotate +0+400 "Music: \"The Pits of Angband\", AVABand's title theme (CC0).  Based on Angband 4.2.5 (GPL 2)." \
  -annotate +0+430 "Tiles: David Gervais (CC BY 3.0), Dungeon Crawl Stone Soup (CC0), Tangaria (CC BY 3.0), RLTiles (public domain)," \
  -annotate +0+458 "Adam Bolt, DawnLike by DragonDePlatino (CC BY 4.0), Hexany Ives (CC0), Angband's Original and Nomad tiles (GPL 2)." \
  "$work/99-end.png"
ff -loop 1 -framerate 30 -t 5 -i "$work/99-end.png" -vf "fade=t=in:st=0:d=0.4,fade=t=out:st=4.2:d=0.8" "${enc[@]}" "$work/99-end.mp4"
pieces+=("$work/99-end.mp4")

# Join, then lay the title theme under it, fading out with the picture.
: > "$work/pieces.txt"
for p in "${pieces[@]}"; do echo "file '$p'" >> "$work/pieces.txt"; done
ff -f concat -safe 0 -i "$work/pieces.txt" -c copy "$work/joined.mp4"
length=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$work/joined.mp4")
ff -i "$work/joined.mp4" -i "$music" \
  -filter_complex "[1:a]atrim=0:$length,afade=t=in:st=0:d=0.5,afade=t=out:st=$(minus "$length" 3):d=3[a]" \
  -map 0:v -map "[a]" -c:v libx264 -preset slow -crf 21 -pix_fmt yuv420p -profile:v high -r 30 \
  -c:a aac -b:a 160k -movflags +faststart -t "$length" "$out/AVABand-trailer.mp4"

# The README's loop (about 11 s): the spells and the tile set cuts, at 960x540 and 15 fps (12 for the GIF).
# (the spells cut short after the dragon's flame: the loop dips to black only where it starts again)
ff -i "$work/magic.mp4" -t 5 "${enc[@]}" "$work/magic-cut.mp4"
loop_parts=("$work/magic-cut.mp4")
while IFS=$'\t' read -r reel _; do loop_parts+=("$work/$reel.mp4"); done < "$frames/tilesets.tsv"
: > "$work/loop.txt"
for p in "${loop_parts[@]}"; do echo "file '$p'" >> "$work/loop.txt"; done
ff -f concat -safe 0 -i "$work/loop.txt" -c copy "$work/loop.mp4"
ff -i "$work/loop.mp4" -vf "fps=15,scale=960:540:flags=lanczos" -c:v libwebp_anim -lossless 0 -q:v 70 \
  -compression_level 6 -loop 0 -an "$out/AVABand-trailer.webp"
ff -i "$work/loop.mp4" -vf "fps=12,scale=800:450:flags=lanczos,split[a][b];[a]palettegen=max_colors=160:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle" \
  -loop 0 "$out/AVABand-trailer.gif"

ls -l "$out"/AVABand-trailer.*
ffprobe -v error -show_entries format=duration -of csv=p=0 "$out/AVABand-trailer.mp4"
