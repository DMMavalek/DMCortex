import re, json, os

mm_dir = r"c:\Users\kelava\Documents\Projects\Dungeon Master Cortex\Assets\Core Rules\WEBHELP\MM"
index_file = os.path.join(mm_dir, "DD03789.HTM")

with open(index_file, encoding='latin-1') as f:
    content = f.read()

# Extract all links to individual monster pages with their names
matches = re.findall(r'<A HREF="(DD\d+\.htm[^"]*)"[^>]*>([^<]+)</A>', content, re.IGNORECASE)

seen_names = set()
monsters = []
for href, name in matches:
    name = name.strip()
    # Filter out navigation/non-monster links
    if name and len(name) > 1 and name not in seen_names:
        seen_names.add(name)
        monsters.append(name)

monsters.sort()
for m in monsters:
    print(m)

print(f"\nTotal unique entries: {len(monsters)}")
