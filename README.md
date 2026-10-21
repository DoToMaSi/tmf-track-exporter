# TMF Track Converter

Windows app that converts TrackMania Forever tracks (`*.Challenge.Gbx`) into textured FBX files.

It uses [GBX.NET](https://github.com/BigBang1112/gbx-net) to read maps and official NadeoPak archives from a local TrackMania United Forever / Nations Forever install.

## How it works

1. Point the app at `TmForever.exe` (once).
2. It decrypts `Packs\*.pak` into `%AppData%\TmfTrackConverter\cache\` — this runs once per install.
3. Open a track from `Documents\TrackMania\Tracks\Challenges` (or official campaign maps).
4. Preview a 2D schematic, then export FBX plus a `textures` folder.

## Build

```bat
dotnet build TmfTrackConverter.sln -c Release
```

Requires the .NET 8 SDK. The solution project-references the sibling `gbx-net` repo.

## License

This tool uses `GBX.NET.LZO` (GPL-3.0). If you distribute the app, treat it as GPL-3.0 as well.
