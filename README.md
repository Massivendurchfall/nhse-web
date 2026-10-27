# NHSE Web

A save editor for Animal Crossing: New Horizons that runs entirely in your browser. It is built on [NHSE](https://github.com/kwsch/NHSE) by kwsch and Blazor WebAssembly. The save is decrypted, edited and encrypted again on your device, nothing is uploaded.

**Open it: https://apps.dtrx.duckdns.org/nhse/**

## Features

- **Player:** name, island name, wallet, bank and Nook Miles, with the passport photo; the raw player event flags with their names, also as `.nhfl` files
- **Pockets:** pick any of NHSE's items (also everything from version 3.0, songs and bell bags, with NHSE's icon where no picture exists) from a picture grid with categories (construction, moving and ladder kits in a category of their own), furniture variants and stack size; 20, 30 or 40 slots, or any count like NHSE
- **Item details:** wrapping (paper, present, box, colour), flower genes and watering, the recipe on a DIY card, the fossil in an unknown fossil, uses and the raw values, in every item picker and on the map
- **Home storage:** the same picture picker for every storage slot, add to the first free slot, expand to the largest size
- **Unlock:** all DIY recipes (also marked as crafted) and all reactions, the catalog (everything, furniture and clothing, fish, bugs, sea creatures or art, with all colour variants) and the Nook Miles achievements, all at once or one by one
- **Profile:** birthday, native fruit in the passport, hotel tickets and Poki from version 3.0, both pocket upgrades
- **Mail:** every letter and parcel in the mailbox; write new ones with an attachment, from a player, a villager or an NPC, with stationery and wrapping; change, delete, save and load `.nhpb` letters
- **Island:** donate everything to the museum or piece by piece with pictures, museum as `.nhm` file; remove weeds, fill holes, pick up branches, water all flowers; clear all trees, bushes, flowers, fences, rocks, shells, dropped items or everything at once; the bulletin board notes; this week's turnip prices, hemisphere, native and sister fruit and flower, airport colour, the plaza visitors of the week, the campsite guest, the recycle box and the raw island flags
- **Map:** an overview of the whole island and a detail view with pictures; place items and nature (trees, flowers, bushes, vegetables, rocks, fences, weeds) by tapping or painting with a brush, remove with a brush, clear or fill a rectangle, drop many items at once (all recipes, a catalog search, the storage or pockets), lay and remove paths, raise and lower cliffs and dig or fill rivers with their edge pieces picked from the neighbours, on the ground or on top of furniture, with undo; change the amount of a stack lying on the ground, replace or remove every identical item on the island at once; build bridges over a river (straight, 3 to 5 tiles, up to 8) and inclines over a cliff, change their style or tear them down; move buildings, raw building values and the plaza like NHSE; save and load items (`.nhl`, `.nhlf`), terrain (`.nht`), buildings (`.nhb`) and the island shape (`.nha`) like NHSE; save the whole map as a picture
- **House:** every room of each player's house, item by item on the floor, on furniture, on the walls, rugs and ceiling; place, rotate, replace and remove furniture; wallpaper, flooring and accent wall; level, roof, walls, door and door decoration; houses (`.nhph`) and rooms (`.nhpr`) as files; the raw house flags
- **Designs:** normal and PRO designs, the Able Sisters' displays and the island flag as pictures; turn any picture into a design (32 x 32, 15 colours and transparency), load and save `.nhd`/`.nhpd` files, save designs as PNG; designs placed on the ground: clear, save and load (`.nhmd`)
- **Residents:** swap a villager for any other from a picture grid (house, furniture and clothes included), edit catchphrases, personality, friendship, nickname and greetings per player, wardrobe and owned furniture, their house from outside and inside, their design, the DIY timer and the raw flags; save and load villagers (`.nhv`), their houses (`.nhvh`) and memories (`.nhvpm`); mark one or all villagers as moving out, max friendship
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
