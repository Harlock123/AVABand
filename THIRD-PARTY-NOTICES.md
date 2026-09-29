# Third-party notices

AVABand is Copyright (c) 2026 Lonnie Watson and is licensed under the GNU General Public License,
version 2 (see `LICENSE`), as Angband is. It builds on, and ships, material from other projects
under their own licences, listed here.

## Angband 4.2.5

AVABand is a clone of [Angband](https://github.com/angband/angband). Its game data
(`src/Angband.Data/data/*.json` — monsters, objects, egos, artifacts, spells, classes, races,
stores, vaults, room templates and the rest) is converted from Angband 4.2.5's `lib/gamedata`
files, and much of its game logic follows Angband's C source closely. That material is
Copyright (c) 1997 Ben Harrison, James E. Wilson, Robert A. Koeneke and the Angband contributors,
and is available under either of the following (AVABand uses the first):

- the GNU General Public License, version 2 (https://www.gnu.org/licenses/old-licenses/gpl-2.0.html), or
- the "Angband licence": *This software may be copied and distributed for educational, research,
  and not for profit purposes provided that this copyright and statement are included in all
  such copies. Other copyrights may also apply.*

## Tilesets (`src/Angband.Avalonia/tilesets`)

| Tileset | Author | Licence | Source |
|---|---|---|---|
| Adam Bolt (16×16) | Adam Bolt | May be redistributed and used for any purpose, with or without modification | https://github.com/angband/angband/tree/master/lib/tiles/adam-bolt |
| David Gervais (32×32) | David Gervais | Creative Commons Attribution 3.0 (https://creativecommons.org/licenses/by/3.0/) | https://github.com/angband/angband/tree/master/lib/tiles/gervais |
| Dungeon Crawl Stone Soup (32×32) | Dungeon Crawl Stone Soup tile artists | CC0 1.0 (https://creativecommons.org/publicdomain/zero/1.0/) | https://opengameart.org/content/dungeon-crawl-32x32-tiles |
| Original Tiles (8×8) | Lars Haugseth, Dawnmist and the Angband contributors | GNU GPL v2, as Angband | https://github.com/angband/angband/tree/4.2.5/lib/tiles/old |
| Nomad (8×16) | Nomad, from Adam Bolt's tiles | GNU GPL v2, as Angband | https://github.com/angband/angband/tree/4.2.5/lib/tiles/nomad |
| Tangaria (32×32) | Tangaria (http://tangaria.com); David Gervais (most of the artwork); Tangar Igroglaz (tileset designer); Shtukensia; Powerwyrm (PWMAngband); Terter; Repne; Ayenes; Ivan Voirol; Refuzzle; Nikkita31; Kenney — full list in its `LICENSE.txt` | Creative Commons Attribution 3.0 (https://creativecommons.org/licenses/by/3.0/) | https://tangaria.com/tileset/ |
| DawnLike (16×16) | DragonDePlatino (art); DawnBringer (palette); sprite names from Tommy Ettinger's DawnLikeAtlas | CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/) | https://opengameart.org/content/dawnlike-16x16-universal-rogue-like-tileset-v181 |
| RLTiles (32×32) | Denzi, Alex Korol, Edger, Wan-ichi, So-Miya, Haruko Numata, Tatsuya, Kelly Youngblood, Paul Pliska, John Harris, Dainokata, Zmy | Public domain; "Part of (or All) the graphic tiles used in this program is the public domain roguelike tileset "RLTiles". Some of the tiles have been modified by Lonnie Watson (AVABand). You can find the original tileset at: http://rltiles.sf.net" | https://rltiles.sourceforge.net/ |
| Hexany's Roguelike Tiles (16×16) | Hexany Ives | CC0 1.0 (https://creativecommons.org/publicdomain/zero/1.0/) | https://hexany-ives.itch.io/hexanys-roguelike-tiles |

Each tileset folder carries its own `LICENSE.txt`. The spider-web tile in the Adam Bolt and Dungeon Crawl sets (`web.png`, `avaband_web.png`) was drawn for
AVABand, as those sets have none, and is CC0; so are the Original and Nomad sets' `web.png` and
`tree.png`. Tangaria, DawnLike, RLTiles and Hexany's sets were cropped and repacked (and Hexany's
and some RLTiles flavours recoloured) by AVABand's tools.

## Sound packs (`src/Angband.Avalonia/soundpacks`)

| Pack | Author | Licence | Source |
|---|---|---|---|
| Angband sounds | Dubtrain | Creative Commons Attribution 4.0 (https://creativecommons.org/licenses/by/4.0/) | https://github.com/angband/angband/tree/master/lib/sounds |
| Dungeon music | RandomMind, JaggedStone, yd, Paul Wortmann, HaelDB, TinyWorlds, Spring Spring, Eponasoft, The Oracle, Juhani Junkala, cynicmusic, AR (via OpenGameArt.org) | CC0 1.0 | https://opengameart.org |

See `LICENSE.txt` / `CREDITS.txt` in each pack's folder.

## Scene pictures (`src/Angband.Avalonia/art`)

Eleven public-domain paintings and engravings, from scans on Wikimedia Commons: Giovanni Battista
Piranesi (*Carceri d'invenzione*, plates XII, XIV and XVI, trimmed to the printed area), Gustave
Doré (*Inferno*), John Martin (*Pandemonium*, *The Great Day of His Wrath*), Caspar David Friedrich
(*Meadows near Greifswald*, *The Abbey in the Oakwood*) and Joseph Wright of Derby (*Cavern, near
Naples*, *A Grotto in the Gulf of Salerno*). Each file's title, artist, source page and the exact
licence tag on it are in `art/CREDITS.md`.

## Ambience loops (`src/Angband.Avalonia/ambience`)

Six loops released under CC0 on OpenGameArt.org (by Kresiek The Furry, Wolfgang_, JaggedStone,
gmason, bretbernhoft and Luke.RUSTLTD); titles, source pages and licence fields in
`ambience/CREDITS.md`. The night crickets loop was repeated three times end to end to lengthen it,
and some were converted to Ogg Vorbis.

## Font (`src/Angband.Avalonia/Assets/Fonts`)

DejaVu Sans Mono (regular and bold), built into AVABand for the map's letters and every column of
text. Copyright (c) 2003 Bitstream, Inc. (Bitstream Vera), with DejaVu changes in the public
domain; free to use, copy and redistribute under the Bitstream Vera / DejaVu licence in
`Assets/Fonts/LICENSE.txt`. Source: https://dejavu-fonts.github.io/

## Libraries (bundled in the release builds)

| Library | Licence |
|---|---|
| .NET runtime and libraries | MIT |
| Avalonia UI | MIT |
| SkiaSharp / HarfBuzzSharp (and native Skia, HarfBuzz) | MIT (Skia: BSD-3-Clause; HarfBuzz: "Old MIT") |
| CommunityToolkit.Mvvm | MIT |
| Silk.NET (OpenAL, SDL bindings) | MIT |
| OpenAL Soft (native, via Silk.NET.OpenAL.Soft.Native) | LGPL-2.0-or-later |
| SDL2 (native, via Ultz.Native.SDL) | zlib |
| NVorbis | MIT |
| NLayer | MIT |

OpenAL Soft is not compiled into AVABand: it is a separate shared library, loaded at run time (the
single-file builds carry it and unpack it before loading it). Its source code is available at
https://github.com/kcat/openal-soft.
