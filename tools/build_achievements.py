"""Builds src/NHSE.Web/wwwroot/data/achievements.json from the Norviah/animal-crossing data (MIT).

usage: python tools/build_achievements.py <Achievements.json>

Maps NHSE's internal achievement names (LifeSupportAchievement.Name, e.g. "CatchFish") to the
in-game title of the Nook Miles achievement. The data only has English titles.
"""
import json
import os
import sys


def main(source, out_path):
    titles = {}
    for entry in json.load(open(source, encoding="utf-8")):
        internal = entry.get("internalName")
        name = entry.get("name")
        if internal and name:
            titles[internal] = name

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(dict(sorted(titles.items())), f, separators=(",", ":"), ensure_ascii=False)
    print(f"{len(titles)} achievements -> {out_path}")


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    main(sys.argv[1], os.path.join(here, "..", "src", "NHSE.Web", "wwwroot", "data", "achievements.json"))
