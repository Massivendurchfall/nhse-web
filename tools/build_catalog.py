"""Builds src/NHSE.Web/wwwroot/data/catalog.json from the Norviah/animal-crossing data (MIT).

usage: python tools/build_catalog.py <Items.min.json> <Creatures.min.json>

The catalog maps game item IDs to a picture on acnhcdn.com and a category for the item picker.
Names are not stored here; the app takes them from NHSE in the player's language.
Furniture variants share one ID and keep their variant in the item's count field
(body | pattern << 5, as NHSE does), so they are stored as variant lists.
"""
import json
import os
import sys

BASE = "https://acnhcdn.com/latest/"

CATEGORIES = {
    "tools": ["Tools/Goods"],
    "materials": ["Other"],
    "furniture": ["Housewares", "Miscellaneous", "Wall-mounted", "Ceiling Decor"],
    "interior": ["Wallpaper", "Floors", "Rugs"],
    "clothing": ["Tops", "Bottoms", "Dress-Up", "Headwear", "Accessories", "Socks", "Shoes", "Bags",
                 "Umbrellas", "Clothing Other"],
    "critters": ["Fish", "Insects", "Sea Creatures"],
    "museum": ["Fossils", "Artwork"],
    "other": ["Posters", "Photos", "Gyroids", "Fencing", "Music", "Message Cards"],
}
SHEET_TO_CAT = {sheet: i for i, cat in enumerate(CATEGORIES) for sheet in CATEGORIES[cat]}

IMAGE_KEYS = ("image", "storageImage", "closetImage", "furnitureImage", "inventoryImage", "iconImage")
# critters look like their pocket icon, not like the furniture model in a tank or cage
CRITTER_KEYS = ("iconImage", "furnitureImage", "critterpediaImage")


def picture(entry, keys=IMAGE_KEYS):
    for key in keys:
        url = entry.get(key)
        if url and url.startswith(BASE) and url.endswith(".png"):
            return url[len(BASE):-4]
    return None


def variant_count(variant_id):
    """'1_2' -> body 1, pattern 2 -> 1 | 2 << 5"""
    if not variant_id:
        return 0
    body, _, pattern = str(variant_id).partition("_")
    try:
        return int(body) | (int(pattern or 0) << 5)
    except ValueError:
        return 0


def main(items_path, creatures_path, out_path):
    catalog = {}  # id -> [category, picture, {variant: picture}]

    def add(item_id, sheet, pic, variant=0):
        if item_id is None or pic is None or sheet not in SHEET_TO_CAT:
            return
        entry = catalog.setdefault(int(item_id), [SHEET_TO_CAT[sheet], pic, {}])
        if variant and variant not in entry[2]:
            entry[2][variant] = pic

    for item in json.load(open(items_path, encoding="utf-8")):
        sheet = item.get("sourceSheet")
        variations = item.get("variations") or []
        if not variations:
            add(item.get("internalId"), sheet, picture(item))
        for v in variations:
            add(v.get("internalId"), sheet, picture(v), variant_count(v.get("variantId")))

    for critter in json.load(open(creatures_path, encoding="utf-8")):
        add(critter.get("internalId"), critter.get("sourceSheet"), picture(critter, CRITTER_KEYS))

    rows = []
    for item_id in sorted(catalog):
        cat, pic, variants = catalog[item_id]
        row = [item_id, cat, pic]
        if variants:
            row.append([[k, variants[k]] for k in sorted(variants)])
        rows.append(row)

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump({"base": BASE, "categories": list(CATEGORIES), "items": rows}, f,
                  separators=(",", ":"), ensure_ascii=False)
    print(f"{len(rows)} items, {sum(len(r[3]) if len(r) > 3 else 0 for r in rows)} variants -> {out_path}")


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    main(sys.argv[1], sys.argv[2], os.path.join(here, "..", "src", "NHSE.Web", "wwwroot", "data", "catalog.json"))
