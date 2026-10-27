"""Builds src/NHSE.Web/wwwroot/data/catalog.json from the community data spreadsheet for ACNH.

usage: python tools/build_catalog.py <acnh.xlsx> [<out.json>]

The spreadsheet (https://tinyurl.com/acnh-sheet) is exported from Google Sheets as xlsx:
https://docs.google.com/spreadsheets/d/13d_LAJPlxMa_DubPTuirkIV4DERBMXbrWQsmSh8ReK4/export?format=xlsx
It covers the game up to version 3.0, its pictures are on nh-cdn.catalogue.ac.

The catalog maps game item IDs to a picture and a category for the item picker.
Names are not stored here; the app takes them from NHSE in the player's language.
Furniture variants share one ID and keep their variant in the item's count field
(body | pattern << 5, as NHSE does), so they are stored as variant lists.
Only the standard library is used, so the xlsx is read as the zip of XML it is.
"""
import json
import os
import re
import sys
import zipfile
import xml.etree.ElementTree as ET

BASE = "https://nh-cdn.catalogue.ac/"

CATEGORIES = {
    "tools": ["ToolsGoods"],
    "materials": ["Other", "Plants"],
    "furniture": ["Housewares", "Miscellaneous", "Wall-mounted", "Ceiling Decor", "Interior Structures"],
    "interior": ["Wallpaper", "Floors", "Rugs"],
    "clothing": ["Tops", "Bottoms", "Dress-Up", "Headwear", "Accessories", "Socks", "Shoes", "Bags",
                 "Umbrellas", "Clothing Other"],
    "critters": ["Fish", "Insects", "Sea Creatures"],
    "museum": ["Fossils", "Artwork"],
    "other": ["Posters", "Photos", "Gyroids", "Fencing", "Music"],
}
# Message Cards are left out: their "Internal ID" numbers the cards, not items, and would hit bags and shirts
SHEET_TO_CAT = {sheet: i for i, cat in enumerate(CATEGORIES) for sheet in CATEGORIES[cat]}

IMAGE_COLUMNS = ("Image", "Storage Image", "Closet Image", "Furniture Image", "Inventory Image", "Icon Image", "Album Image")
# critters look like their pocket icon, not like the furniture model in a tank or cage
CRITTER_COLUMNS = ("Icon Image", "Furniture Image", "Critterpedia Image")
CRITTER_SHEETS = ("Fish", "Insects", "Sea Creatures")

NS = {"m": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
REL = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id"
IMAGE = re.compile(r'IMAGE\(\s*"([^"]+)"', re.I)


def read_xlsx(path):
    """Every sheet as a list of rows; IMAGE("url") formulas become their url."""
    z = zipfile.ZipFile(path)
    shared = []
    if "xl/sharedStrings.xml" in z.namelist():
        for si in ET.fromstring(z.read("xl/sharedStrings.xml")).findall("m:si", NS):
            shared.append("".join(t.text or "" for t in si.iter("{%s}t" % NS["m"])))
    book = ET.fromstring(z.read("xl/workbook.xml"))
    targets = {r.get("Id"): r.get("Target") for r in ET.fromstring(z.read("xl/_rels/workbook.xml.rels"))}
    sheets = {}
    for s in book.find("m:sheets", NS):
        file = "xl/" + targets[s.get(REL)].lstrip("/").removeprefix("xl/")
        rows = []
        for row in ET.fromstring(z.read(file)).iter("{%s}row" % NS["m"]):
            values = {}
            for c in row.findall("m:c", NS):
                col = 0
                for ch in re.match(r"[A-Z]+", c.get("r")).group(0):
                    col = col * 26 + ord(ch) - 64
                f, v = c.find("m:f", NS), c.find("m:v", NS)
                if f is not None and f.text and IMAGE.search(f.text):
                    values[col - 1] = IMAGE.search(f.text).group(1)
                elif c.get("t") == "s" and v is not None:
                    values[col - 1] = shared[int(v.text)]
                elif v is not None:
                    values[col - 1] = v.text
            if values:
                rows.append([values.get(i) for i in range(max(values) + 1)])
        sheets[s.get("name")] = rows
    return sheets


def picture(get, columns):
    for column in columns:
        url = get(column)
        if url and url.startswith(BASE) and url.endswith(".png"):
            return url[len(BASE):-4]
    return None


def variant_count(variant_id):
    """'1_2' -> body 1, pattern 2 -> 1 | 2 << 5"""
    if not variant_id or variant_id == "NA":
        return 0
    body, _, pattern = str(variant_id).partition("_")
    try:
        return int(body) | (int(pattern or 0) << 5)
    except ValueError:
        return 0


def main(xlsx_path, out_path):
    catalog = {}  # id -> [category, picture, {variant: picture}]
    for sheet, rows in read_xlsx(xlsx_path).items():
        if sheet not in SHEET_TO_CAT or not rows or "Internal ID" not in rows[0]:
            continue
        index = {h: i for i, h in enumerate(rows[0]) if h}
        columns = CRITTER_COLUMNS if sheet in CRITTER_SHEETS else IMAGE_COLUMNS
        for row in rows[1:]:
            def get(h, row=row):
                return row[index[h]] if h in index and index[h] < len(row) else None
            try:
                item_id = int(float(get("Internal ID")))
            except (TypeError, ValueError):
                continue
            pic = picture(get, columns)
            if pic is None or str(get("Name")).startswith("Hazure"):  # the game's placeholder songs
                continue
            entry = catalog.setdefault(item_id, [SHEET_TO_CAT[sheet], pic, {}])
            variant = variant_count(get("Variant ID"))
            if variant and variant not in entry[2]:
                entry[2][variant] = pic

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
    out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(here, "..", "src", "NHSE.Web", "wwwroot", "data", "catalog.json")
    main(sys.argv[1], out)
