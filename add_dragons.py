"""add_dragons.py - Adds all dragon entries to monsters.json"""
import json

OUTPUT = r"c:\Users\kelava\Documents\Projects\Dungeon Master Cortex\data\rulesets\monsters.json"

def m(id, name, mtype, hd, ac, mv, thac0, attacks, damage, sp_atk, sp_def, mr, size, morale, xp, na, freq, intel, align, treasure, desc):
    return {"id": id, "name": name, "monster_type": mtype, "source": "Monstrous Manual",
            "hit_dice": hd, "armor_class": ac, "movement": mv, "thac0": thac0,
            "attacks": attacks, "damage": damage, "special_attacks": sp_atk,
            "special_defenses": sp_def, "magic_resistance": mr, "size": size,
            "morale": morale, "xp_value": xp, "number_appearing": na, "frequency": freq,
            "intelligence": intel, "alignment": align, "treasure_type": treasure, "description": desc}

new_entries = [
    # ── CHROMATIC DRAGONS ──────────────────────────────────────────────────────
    m("mon_dragon_red","Dragon, Red","Dragon","20",-2,"9, Fly 30 (E)",5,3,
      "1d8+7/1d8+7/8d6",
      "Breath weapon (20d6 fire cone), spells up to 9th level",
      "Immune to fire","Nil","G","Fearless (20)",12000,"1","Very Rare",
      "Genius","Chaotic Evil","H,X",
      "The mightiest chromatic dragon; supremely covetous and arrogant. Hoards treasure in volcanic mountain lairs."),
    m("mon_dragon_blue","Dragon, Blue","Dragon","18",-1,"9, Fly 30 (E)",7,3,
      "1d8+6/1d8+6/7d6",
      "Breath weapon (18d6 lightning bolt), spells",
      "Immune to electricity","Nil","G","Fearless (20)",10000,"1","Very Rare",
      "Genius","Chaotic Evil","H,X",
      "Blue dragons rule deserts and badlands, commanding lightning storms. Proud and territorial."),
    m("mon_dragon_green","Dragon, Green","Dragon","14",1,"9, Fly 30 (E)",11,3,
      "1d8+3/1d8+3/5d6",
      "Breath weapon (14d6 chlorine gas cloud), psionics, spells",
      "Immune to acid and gas","Nil","L","Fearless (20)",7000,"1","Rare",
      "Very","Chaotic Evil","H,X",
      "Green dragons dwell in temperate forests; cunning, treacherous, and fond of riddles."),
    m("mon_dragon_black","Dragon, Black","Dragon","10",3,"9, Fly 24 (E), Sw 12",13,3,
      "1d6+1/1d6+1/3d6",
      "Breath weapon (10d6 acid stream)",
      "Immune to acid","Nil","L","Steady (12)",3000,"1","Uncommon",
      "Very","Chaotic Evil","G,X",
      "Black dragons lurk in fetid swamps and bogs, the most vicious of the smaller chromatic dragons."),
    m("mon_dragon_white","Dragon, White","Dragon","6",5,"9, Fly 24 (E), Sw 12",15,3,
      "1d4+1/1d4+1/2d6",
      "Breath weapon (6d6 frost cone)",
      "Immune to cold","Nil","M","Unsteady (7)",900,"1-3","Uncommon",
      "Low","Chaotic Evil","F,X",
      "White dragons are the most primitive of the chromatic dragons, dwelling in arctic wastes."),
    # ── METALLIC DRAGONS ───────────────────────────────────────────────────────
    m("mon_dragon_gold","Dragon, Gold","Dragon","18",-1,"9, Fly 30 (E), Sw 12",7,3,
      "1d8+6/1d8+6/7d6",
      "Breath weapon (18d6 fire cone OR weakening gas cone), spells",
      "Immune to fire","Nil","G","Fearless (20)",10000,"1","Very Rare",
      "Genius","Lawful Good","H,X",
      "The greatest of metallic dragons; wise, majestic, and powerful guardians of good."),
    m("mon_dragon_silver","Dragon, Silver","Dragon","16",0,"9, Fly 30 (E), Sw 12",9,3,
      "1d8+5/1d8+5/6d6",
      "Breath weapon (16d6 cold cone OR paralyzing gas cone), spells",
      "Immune to cold","Nil","G","Champion (15)",8000,"1","Rare",
      "Genius","Lawful Good","H,X",
      "Noble and compassionate metallic dragons; they often befriend good mortals."),
    m("mon_dragon_bronze","Dragon, Bronze","Dragon","14",1,"9, Fly 30 (E), Sw 12",11,3,
      "1d8+3/1d8+3/5d6",
      "Breath weapon (14d6 lightning bolt OR repulsion gas cone), spells",
      "Immune to electricity","Nil","L","Champion (15)",7000,"1","Rare",
      "Very","Lawful Good","H,X",
      "Bronze dragons dwell near the sea; noble protectors of coastal regions."),
    m("mon_dragon_brass","Dragon, Brass","Dragon","12",2,"9, Fly 30 (E)",13,3,
      "1d8+2/1d8+2/4d6",
      "Breath weapon (12d6 fire line OR sleep gas cone), spells",
      "Immune to fire","Nil","L","Elite (13)",5000,"1","Uncommon",
      "Very","Chaotic Good","G,X",
      "Brass dragons love conversation above all else; the most garrulous of all dragons."),
    m("mon_dragon_copper","Dragon, Copper","Dragon","10",3,"9, Fly 30 (E)",13,3,
      "1d6+1/1d6+1/3d6",
      "Breath weapon (10d6 acid line OR slow gas cone), spells",
      "Immune to acid","Nil","L","Elite (13)",3000,"1","Uncommon",
      "Very","Chaotic Good","G,X",
      "Copper dragons are incorrigible tricksters; known for pranks, humor, and riddles."),
    # ── GEM DRAGONS ────────────────────────────────────────────────────────────
    m("mon_dragon_amethyst","Dragon, Amethyst","Dragon","9",2,"9, Fly 30 (E)",11,3,
      "1d8/1d8/5d6",
      "Breath weapon (lozenge of violet force energy), psionics, spells",
      "Immune to electricity","Nil","L","Elite (13)",4000,"1","Rare",
      "Very","Neutral","H",
      "Amethyst gem dragons with lavender scales; neutral and philosophical."),
    m("mon_dragon_crystal","Dragon, Crystal","Dragon","8",2,"9, Fly 30 (E)",13,3,
      "1d8/1d8/4d6",
      "Breath weapon (prismatic light burst that blinds), spells",
      "Immune to fire and cold","Nil","L","Elite (13)",3000,"1","Rare",
      "Very","Chaotic Neutral","H",
      "Crystal gem dragons with transparent scales; friendly, curious, and social."),
    m("mon_dragon_emerald","Dragon, Emerald","Dragon","10",1,"9, Fly 30 (E), Sw 12",9,3,
      "1d8+1/1d8+1/5d6",
      "Breath weapon (sonic shriek that deafens), psionics, spells",
      "Immune to acid and electricity","Nil","L","Elite (13)",5000,"1","Very Rare",
      "Very","Lawful Neutral","H",
      "Emerald gem dragons with deep green scales; the most paranoid of the gem dragons."),
    m("mon_dragon_sapphire","Dragon, Sapphire","Dragon","10",1,"9, Fly 30 (E)",9,3,
      "1d8+1/1d8+1/5d6",
      "Breath weapon (electric spark shower), psionics, spells",
      "Immune to electricity","Nil","L","Elite (13)",5000,"1","Very Rare",
      "Very","Lawful Neutral","H",
      "Sapphire gem dragons with deep blue scales; military-minded and territorial."),
    m("mon_dragon_topaz","Dragon, Topaz","Dragon","9",2,"9, Fly 30 (E), Sw 12",11,3,
      "1d8/1d8/4d6",
      "Breath weapon (bolt of dehydration that weakens), spells",
      "Immune to electricity","Nil","L","Elite (13)",4000,"1","Rare",
      "Very","Chaotic Neutral","H",
      "Topaz gem dragons with amber scales; solitary and bad-tempered."),
    # ── SPECIAL DRAGONS ────────────────────────────────────────────────────────
    m("mon_dragon_turtle","Dragon Turtle","Dragon","12",0,"3, Sw 9",9,3,
      "2d6/2d6/4d8",
      "Steam breath (12d6 steam, 60-foot cone), capsize ships",
      "Hard shell armor (AC 0)","Nil","G","Fearless (20)",5000,"1","Rare",
      "Average","Neutral","H",
      "Massive sea-dwelling dragons with turtle shells. Their steam breath capsizes ships."),
    m("mon_dracolich","Dracolich","Dragon","Variable",0,"By base dragon type",9,3,
      "By dragon type",
      "Fear aura, paralyzing touch, breath weapon, spells",
      "Immune to cold, sleep, charm, hold, death magic","Special","G","Fearless (20)",
      6000,"1","Very Rare","Genius","Chaotic Evil","H",
      "Undead dragon liches, created through dark necromantic rituals. Near-indestructible."),
    m("mon_pseudodragon","Pseudodragon","Dragon","2",2,"6, Fly 12 (B)",19,2,
      "1/1d3",
      "Venomous stinger (save vs. poison or sleep; fail by 5+ = paralyzed), telepathy",
      "None","35%","T","Steady (11)",175,"1","Rare",
      "High","Neutral Good","None",
      "Tiny cat-sized good-natured dragons; prized as wizard familiars. Communicate telepathically."),
]

# Load, merge, save
with open(OUTPUT, "r", encoding="utf-8") as f:
    data = json.load(f)

existing = {mon["id"]: mon for mon in data["monsters"]}
print(f"Before: {len(existing)} monsters")

for entry in new_entries:
    existing[entry["id"]] = entry

final = list(existing.values())
print(f"After:  {len(final)} monsters")

with open(OUTPUT, "w", encoding="utf-8") as f:
    json.dump({"monsters": final}, f, indent=2, ensure_ascii=False)

# Verify
with open(OUTPUT, "r", encoding="utf-8") as f:
    check = json.load(f)

dragons = sorted([mon["name"] for mon in check["monsters"] if "Dragon" in mon["name"] or mon["monster_type"] == "Dragon"])
print(f"\nDragons ({len(dragons)}):")
for d in dragons:
    print(f"  {d}")
print(f"\nTotal verified: {len(check['monsters'])} monsters")
