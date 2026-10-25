# NHSE Web

A save editor for Animal Crossing: New Horizons that runs entirely in your browser. It is built on [NHSE](https://github.com/kwsch/NHSE) by kwsch and Blazor WebAssembly. The save is decrypted, edited and encrypted again on your device, nothing is uploaded.

**Open it: https://apps.dtrx.duckdns.org/nhse/**

## Features

- **Player:** name, island name, wallet, bank and Nook Miles, with the passport photo
- **Pockets:** pick items from a picture grid with categories, furniture variants and stack size
- **Home storage:** the same picture picker for every storage slot, add to the first free slot, expand to the largest size
- **Unlock:** all DIY recipes (also marked as crafted) and all reactions, the catalog (everything, furniture and clothing, fish, bugs, sea creatures or art, with all colour variants) and the Nook Miles achievements, all at once or one by one
- **Profile:** birthday, native fruit in the passport, hotel tickets and Poki from version 3.0, both pocket upgrades
- **Island:** donate everything to the museum or piece by piece with pictures, remove weeds, fill holes, pick up branches, water all flowers, set this week's turnip prices, hemisphere, native and sister fruit and flower, airport colour, the plaza visitors of the week, the campsite guest and the recycle box
- **Map:** an overview of the whole island and a detail view with pictures; place items and nature (trees, flowers, bushes, vegetables, rocks, fences, weeds) by tapping or painting with a brush, remove with a brush, clear or fill a rectangle, lay and remove paths whose edge pieces are picked from their neighbours, on the ground or on top of furniture, with undo; save the whole map as a picture
- **Residents:** swap a villager for any other from a picture grid (house, furniture and clothes included), edit catchphrases, personality, friendship and nickname per player, wardrobe and owned furniture, mark villagers as moving out, max friendship
- **Open a save folder** in Chrome or Edge and save straight back into it, or **open a ZIP** in any browser and get a new ZIP
- A backup ZIP of the original files is downloaded before the first write
- German and English, following the browser language

## Where is my save?

- **Eden, yuzu, Citron:** right click the game and open the save data location. It is the folder `01006F8002326000`.
- **Switch:** back up the save with JKSV or Checkpoint and open that folder or ZIP.

Close the game before you save, and always keep a backup.

## Build

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
git clone --recursive https://github.com/Massivendurchfall/nhse-web
cd nhse-web
dotnet run --project src/NHSE.Web
```

`dotnet publish src/NHSE.Web -c Release -o publish` writes the static site to `publish/wwwroot`. To host it in a subfolder, change `<base href="/" />` in `index.html` to that folder, for example `/nhse/`.

## How it works

- `external/NHSE` is the upstream NHSE repository as a submodule. `src/NHSE.Core.Web` compiles its NHSE.Core sources for WebAssembly without changes, except for three structures (see below) and the AES-CTR code: `System.Security.Cryptography.Aes` is not available in the browser, so `src/NHSE.Core.Web/Crypto/AesCtr.cs` is a managed AES-128 with the same API.
- The villager swap is NHSE.Villagers' `VillagerSwap`, also unchanged. Its 37 MB of villager templates are not embedded: the build serves them from `villagers/` and the app fetches the two files of a villager when it moves in (`src/NHSE.Core.Web/Villagers/VillagerResources.cs`).
- `src/NHSE.Web` is the Blazor WebAssembly app. Folder access uses the File System Access API, with a folder upload and ZIP fallback for other browsers.
- The three NHSE structures with bool arrays (achievements, reactions, visitors) are compiled from patched copies in `src/NHSE.Core.Web/Patched`: the WebAssembly marshaller ignores `ArraySubType = UnmanagedType.I1` and writes 4 bytes per flag, which overflowed into the data behind them. The copies store the flags as bytes. The struct converter is patched too: on WebAssembly a `ByValTStr` string is only written up to its terminator, so the buffer is cleared first instead of keeping random bytes after short names.
- `tools/build_catalog.py` builds `wwwroot/data/catalog.json` from the Norviah/animal-crossing data. It maps item IDs to pictures and categories; item names come from NHSE in the player's language.
- `tools/SaveCheck` checks the managed AES against .NET's AES, loads a save folder, verifies every hash and runs a save round trip. Use it on a copy of a save only.

## Credits and license

- [NHSE](https://github.com/kwsch/NHSE) by kwsch and contributors, GPL-3.0. The menu icons and villager pictures come from NHSE.Sprites, the villager templates from NHSE.Villagers.
- Item data from [Norviah/animal-crossing](https://github.com/Norviah/animal-crossing), MIT, see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- Item pictures are loaded from [acnhcdn.com](https://acnhcdn.com) and are not part of this repository.
- Animal Crossing is a trademark of Nintendo. This project is not affiliated with or endorsed by Nintendo.

This project is licensed under the [GNU General Public License v3.0](LICENSE).
