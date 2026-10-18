# Tornado Image Converter

A Windows tool to browse, preview and convert the files of **Digital Integration's Tornado** (and *Operation Desert Storm*): cockpit panels, menu screens, digitized photos, fonts, night palettes, and the extra files shipped with the Steam release (manuals, soundtrack, map).

Point it at the game folder, see every file the way the game would draw it, and convert images between the game formats and ordinary PNG / JPEG files in either direction. It is meant for modders who want to redraw cockpits, menus and photos with modern image editors.

> This project is not affiliated with Digital Integration or the owners of Tornado. It contains no game data: you need your own copy of the game.

## Features

- **Folder explorer** with a file list (name, detected type, size, sortable columns) and a live preview.
- **Automatic file type detection** from content and extension, not from the extension alone.
- **Exact previews**: the preview uses the same decoder as the converter, so you see exactly what will be converted.
- **Batch conversion**: select any number of files (all, all of one type, inverted selection) and convert them in one go to a destination folder.
- **Per-type options tabs**: one tab per file extension in the selection, showing only the options that apply to the chosen source type and destination format.
- **Safe overwriting**: confirmation before overwriting existing files, and an optional `.bak` copy of any BT2 file that is replaced (an existing `.bak` is never overwritten).
- **Remembers** the last source and destination folders between sessions.

## Supported files

### Images

| Format | Where it is used | View | Convert from | Convert to |
| :--- | :--- | :---: | :---: | :---: |
| **BT2** | Cockpit panels, canopy, preview backdrop | yes | yes | yes |
| **GFX** | Menu and briefing screens (16 colors, 4 bit planes) | yes | yes | yes |
| **PT2** | Digitized aircraft photos and title screens (256 colors) | yes | yes | yes |
| **FNT** | Bitmap fonts (`FONT.FNT`, `BIGFONT.FNT`) | yes | yes (to PNG only) | no |
| **IFF / LBM** | Game screenshots (PBM and ILBM, compressed or not) | yes | yes | no |
| **PNG** | Editing format | yes | yes | yes |
| **JPEG** | Source images, and the map shipped with the Steam release | yes | yes | no |

### Other files

| Format | What you get |
| :--- | :--- |
| **RGB** | Night palettes (6-bit triplets) shown as a grid of color swatches with their palette index range |
| **FLAC** | Built-in audio player (play, stop, seek, volume) for the Steam soundtrack |
| **PDF** | Page viewer for the manuals (previous / next page, page number, zoom, fit width) |
| **TXT, LOG, INF, CFG, BAT, MIS, ...** | Text view, with a hex dump alternative (a file that is not valid UTF-8 is decoded as DOS code page 437) |
| **BIN, DTA, IN, SAV, EXE, COM** and unknown files | Hex dump |

### Conversions available

| From | To |
| :--- | :--- |
| BT2 | PNG, BT2, GFX, PT2 |
| GFX | PNG, BT2, PT2 |
| PT2 | PNG, BT2, GFX |
| IFF / LBM | PNG, BT2, GFX, PT2 |
| PNG | BT2, GFX, PT2 |
| JPEG | PNG, BT2, GFX, PT2 |
| FNT | PNG |

Output names follow the game's convention: upper case for the game formats (`PILOTPAN.BT2`), original name for PNG. The name of the source file also drives the automatic palette and size choices, so a PNG named `PILOTPAN.png` becomes `PILOTPAN.BT2` with the right settings.

## Format notes

### BT2

Raw 8-bit indexed pixels, row by row, with no header and no palette. The size is deduced from the file length:

| File size | Dimensions | Typical file |
| ---: | :--- | :--- |
| 480,000 bytes | 800 x 600 | SVGA mod panels (PILOTPAN, NAVIGPAN, PSIDEPAN, NSIDEPAN, FRAMEPAN, EXTRAPAN) |
| 64,000 bytes | 320 x 200 | NAVIGPAN, EXTRAPAN, PREVIEW (original game) |
| 33,280 bytes | 320 x 104 | PILOTPAN (original game) |
| 12,800 bytes | 320 x 40 | PSIDEPAN, NSIDEPAN (original game) |
| 5,120 bytes | 320 x 16 | FRAMEPAN (original game) |

Any other size that is a multiple of 800 bytes is read as 800 pixels wide, and any other multiple of 320 as 320 pixels wide.

Because a BT2 holds no palette, the colors depend on the palette you choose. Colors 0 to 175 are fixed; colors 176 to 255 depend on the view:

- **Standard panel**: the default for most panels.
- **Look down**: used for `EXTRAPAN`.
- **Preview**: used for `PREVIEW` (colors above 207 are unused).

With the **Auto** setting, the palette is chosen from the file name.

#### Converting to BT2

| Option | Purpose |
| :--- | :--- |
| **Palette** | Palette the image colors are matched against (Auto, Standard panel, Look down, Preview) |
| **Colors** | *Panel colors (night-safe)*: only indices used by the original panel art, which all have night variants. *Full palette*: all 256 colors, but some change with night, fog or HUD brightness |
| **Dithering** | Floyd-Steinberg error diffusion, for photos and renders |
| **Target size** | Auto (SVGA mod: 800 x 600, but 320 x 200 for PREVIEW), Auto (original game: the original size of each panel), 800 x 600, 320 x 200, or keep the image size |
| **If size differs** | Stretch, Fit (keep aspect ratio, bars use index 0), or skip the file |
| **Back up existing BT2** | Keep a `.bak` copy before overwriting |

Colors are matched with a perceptual ("redmean") distance. An indexed PNG whose palette entries match the Tornado palette in use keeps its exact indices, and transparent pixels become index 0. Exporting a BT2 to PNG produces an indexed PNG with the Tornado palette, so BT2 to PNG and back to BT2 is lossless as long as the PNG stays indexed and the same palette is selected.

#### BT2 to BT2

Resizes a panel without touching its palette indices (nearest neighbor), for example to move an original 320 x 200 panel to 800 x 600. Only the size options and the backup option apply, since no color is converted.

Panels that cover only part of the screen in the original game (PILOTPAN 320 x 104, PSIDEPAN 320 x 40, FRAMEPAN 320 x 16) are scaled as a whole: they are not repositioned inside the 800 x 600 canvas.

An optional **Index 0 transparent** setting shows or exports index 0 as transparent.

### GFX

A 4-byte header (width in bytes and height, 16 bits each) followed by 4 bit planes per line, 16 colors. GFX files contain no palette: the game executable (`AMP.EXE`) loads one of several 16-color sets depending on the screen. The tool offers those sets: *Menu (grayscale)*, *Debrief (blue-gray)*, *Warm menu (brown)*, *Warm menu (olive)* and *Gray-green*. **Auto** picks Debrief for `DBnn`, `DEBRIEF` and `DUALDEB`, and Menu otherwise. The same choice applies when producing a GFX (the image is reduced to the selected 16 colors; width is rounded up to a multiple of 8).

### PT2

A 256-color palette (6-bit RGB) followed by 320 x 200 pixels stored as 4-bit deltas. The palette is embedded, so no palette option is needed to view a PT2. When writing a PT2, the image is stretched to 320 x 200, reduced to 256 colors (median cut), sorted by luminance to keep the deltas small, and encoded.

### RGB night palettes

Triplets of 6-bit values (0 to 63) without header. A file covers the last colors of the palette: 144 colors for `PANEL_xx` (indices 112 to 255) and 80 for `KNEES_xx` (presumably indices 176 to 255, deduced from the size). The index range is always computed as 256 minus the number of colors in the file. The preview draws them in a 16-column grid where color *n* sits at row *n* / 16, column *n* % 16.

## Download

Get the latest build from the [Releases page](https://github.com/xan2622/Tornado-Image-Converter/releases). Two executables are provided for Windows x64:

- `TornadoImageConverter-<version>-win-x64.exe`: self-contained, nothing to install (about 80 MB).
- `TornadoImageConverter-<version>-win-x64-framework-dependent.exe`: smaller (about 25 MB), requires the .NET 10 Desktop Runtime.

Download one of them and run it. The executables are not code-signed, so your browser or Windows SmartScreen may show a warning the first time.

## Requirements

- Windows 10 (version 2004, build 19041) or later, Windows 11 included
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download) (or the .NET 10 SDK to build from source)

The PDF preview uses the Windows PDF API, the FLAC player uses the Windows media stack, and PNG / JPEG decoding uses the Windows imaging components (WIC), so no extra dependency is needed.

## Build and run

```powershell
git clone https://github.com/xan2622/Tornado-Image-Converter.git
cd Tornado-Image-Converter

# Debug and Release builds
dotnet build src/TornadoImageConverter -c Debug
dotnet build src/TornadoImageConverter -c Release

# Run
dotnet run --project src/TornadoImageConverter -c Release

# Publish
dotnet publish src/TornadoImageConverter -c Release -o publish
```

The built executable is `src/TornadoImageConverter/bin/Release/net10.0-windows10.0.19041.0/TornadoImageConverter.exe`.

## Usage

1. Click **Source folder** and choose a folder containing game files (for example `...\Tornado\TORNADO\TORNADO.CD\FLIGHT`).
2. Click a file to preview it. Use the option tab of its extension to change the palette or view mode if the colors look wrong.
3. Select the files to convert, using **Select all**, **Select all of same file type**, **Invert selection** or the usual Ctrl / Shift clicks.
4. Click **Destination folder** and choose where the converted files go.
5. Pick the destination format in **to:**, adjust the options shown in the tabs, then click **Convert**.

Tip: to redraw a cockpit, export it to PNG, edit the PNG in an editor that supports indexed color (GIMP, Aseprite, Photoshop), name it after the panel, and convert it back to BT2 with the same palette.

## Project layout

| Path | Content |
| :--- | :--- |
| `src/TornadoImageConverter/` | The application (Windows Forms + WPF imaging) |
| `src/TornadoImageConverter/Bt2*.cs` | BT2 reading, encoding and output (size, palette, backup) |
| `src/TornadoImageConverter/GfxImage.cs`, `Pt2Image.cs`, `IffImage.cs`, `FntImage.cs`, `RgbPalette.cs` | Decoders and encoders for the other formats |
| `src/TornadoImageConverter/TornadoPalettes*.cs` | The Tornado VGA palettes |
| `src/TornadoImageConverter/ExplorePanel.cs`, `ExtensionOptions.cs` | The Explore tab and its per-extension options |

## Links and credits

- Frankie Kam's Tornado Discord server: <https://discord.com/invite/MKzBAxe>
- Frankie Kam's Tornado: <https://github.com/TornadoGame/Tornado>
- Frankie Kam's Tools, Add-Ons and Patches: <https://github.com/TornadoGame/TornadoToolsAndPatches>
- amalahama's DI-Tornado-Desert-Storm-1.0e-Source-Code: <https://github.com/amalahama/DI-Tornado-Desert-Storm-1.0e-Source-Code>
- amalahama's SVGA-Tornado-Release: <https://github.com/amalahama/SVGA-Tornado-Release>

The format details in the code comments refer to the released Tornado source code (for example `VISUAL/PALETTES.ASM` and `LIB8086/VGA_DRVR.ASM`) and to the game executable `AMP.EXE`.

