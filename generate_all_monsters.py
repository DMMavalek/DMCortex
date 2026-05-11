"""
generate_all_monsters.py
Generates a complete AD&D 2e Monstrous Manual monster dataset.
Run with: python generate_all_monsters.py
"""
import json
import os

OUTPUT = r"c:\Users\kelava\Documents\Projects\Dungeon Master Cortex\data\rulesets\monsters.json"

def m(id, name, mtype, hd, ac, mv, thac0, attacks, damage, sp_atk, sp_def, mr, size, morale, xp, na, freq, intel, align, treasure, desc):
    return {"id": id, "name": name, "monster_type": mtype, "source": "Monstrous Manual",
            "hit_dice": hd, "armor_class": ac, "movement": mv, "thac0": thac0,
            "attacks": attacks, "damage": damage, "special_attacks": sp_atk,
            "special_defenses": sp_def, "magic_resistance": mr, "size": size,
            "morale": morale, "xp_value": xp, "number_appearing": na, "frequency": freq,
            "intelligence": intel, "alignment": align, "treasure_type": treasure, "description": desc}

monsters = []

# ─── ABERRATIONS ───────────────────────────────────────────────────────────────
monsters += [
    m("mon_aboleth","Aboleth","Aberration","8+4",2,"3, Sw 9",11,4,"1d4/1d4/1d4/1d12","Slime, psychic domination, mucus cloud","Regenerates 2 hp/round","Nil","L","Fearless (20)",2000,"1","Very Rare","Genius","Chaotic Evil","F","Ancient aquatic aberrations with three purplish-black tentacles and incredible psychic power."),
    m("mon_beholder","Beholder","Aberration","45-75 hp",-2,"3, Fl 3",7,1,"2d4","10 eye rays with different magic effects, antimagic central eye","None","Nil","L","Fearless (20)",8000,"1","Rare","Genius","Lawful Evil","I,S","Spherical aberrations with a large central eye and 10 magical eyestalks. The central eye negates all magic in its cone."),
    m("mon_carrion_crawler","Carrion Crawler","Aberration","3+1",3,"12, Climb 6",17,8,"1d2 each","8 tentacles cause paralysis (save vs. paralysis or be immobilized)","None","Nil","L","Steady (11)",270,"1-3","Uncommon","Animal","Neutral","B","Tentacled scavengers that paralyze prey with their 8 tentacles."),
    m("mon_cloaker","Cloaker","Aberration","6",4,"1, Fly 15 (A)",15,2,"1d6/1d4","Moan (fear or fascination), engulf victim","None","Nil","L","Elite (13)",975,"1-4","Rare","Average","Chaotic Neutral","C","Intelligent black manta-like creatures that use moaning to fascinate victims."),
    m("mon_displacer_beast","Displacer Beast","Aberration","6+6",4,"15",13,2,"2d4/2d4","Displacement: all attacks made at -2 to hit","None","Nil","L","Elite (13)",975,"1-4","Uncommon","Semi-","Neutral Evil","D","Six-legged feline predators that displace their visual image 3 feet to the side."),
    m("mon_ettercap","Ettercap","Aberration","5",6,"12, Climb 9",15,3,"1d3/1d3/1d8","Venomous bite (save vs. poison), web spinning","None","Nil","M","Steady (11)",650,"1-3","Uncommon","Low","Neutral Evil","C","Humanoid spider-kin that spin webs and are venomous."),
    m("mon_gibbering_mouther","Gibbering Mouther","Aberration","4",1,"3, Sw 3",15,6,"1d3 each","Gibbering causes confusion, blood drain, engulf and suffocate","Immune to confusion and insanity effects","Nil","S","Elite (13)",650,"1","Rare","Non-","Neutral","Nil","Amorphous bodies covered in eyes and mouths. Their constant gibbering drives enemies mad."),
    m("mon_intellect_devourer","Intellect Devourer","Aberration","6+6",4,"12",13,4,"1d4/1d4/1d4/1d4","Psionic attacks, body possession","Immune to psionics, +2 or better weapon to hit","Nil","S","Elite (13)",1400,"1","Very Rare","Genius","Chaotic Evil","Nil","Brain-like creatures that use psionic power to devour intellect and eventually possess hosts."),
    m("mon_otyugh","Otyugh","Aberration","6",3,"6",15,3,"1d8/1d8/1d4+1","Disease infection (typhoid-like fever)","None","Nil","L","Steady (11)",975,"1-2","Common","Low","Neutral","F","Waste-eating tripod aberrations dwelling in dungeon garbage pits."),
    m("mon_roper","Roper","Aberration","10+10",-2,"3",9,6,"1d4+4 x5/5d6","5 strands drain strength (save vs. death each), massive bite","None","Nil","L","Fearless (20)",4000,"1","Rare","Very","Chaotic Evil","D","Cone-shaped stalactite-mimics that ensnare prey with energy-draining strands."),
    m("mon_rust_monster","Rust Monster","Aberration","5",2,"18",15,2,"Rust/Rust","Antennae rust any metal they touch to worthless powder","None","Nil","M","Steady (11)",175,"1-2","Uncommon","Animal","Neutral","Nil","Insectoid creatures that destroy all metal on touch. The nightmare of armored adventurers."),
    m("mon_xorn","Xorn","Aberration","7+7",-2,"9",13,4,"1d3/1d3/1d3/4d6","Earth glide, surprised on 5 in 6","Immune to fire and cold, +2 weapon to hit","Nil","M","Elite (13)",1400,"1-4","Uncommon","Average","Neutral","Q","Three-armed earth-dwellers that phase through stone and eat gems and minerals."),
]

# ─── UNDEAD ─────────────────────────────────────────────────────────────────────
monsters += [
    m("mon_banshee","Banshee","Undead","7",0,"15, Fly 15",13,1,"1d8","Wail (save vs. death or die), gaze causes coma","Silver or +1 weapon to hit, immune to non-magic","Nil","M","Fearless (20)",2000,"1","Rare","High","Chaotic Evil","E","The undead spirit of an evil female elf. Her wail slays those who hear it."),
    m("mon_death_knight","Death Knight","Undead","9+3",-3,"12",7,1,"2d8","Spells (paladin and wizard), unholy power aura","Immune to cold, charm, sleep, hold; +1 weapon to hit","50%","M","Fearless (20)",8000,"1","Very Rare","High","Chaotic Evil","H","Undead champions of evil; former paladin warriors transformed by dark powers."),
    m("mon_ghoul","Ghoul","Undead","2",6,"9",19,3,"1d3/1d3/1d6","Paralysis (save vs. paralysis or freeze)","Immune to sleep and charm","Nil","M","Steady (11)",175,"2-12","Common","Low","Chaotic Evil","B","Undead cannibals that paralyze living prey with their claws and bite."),
    m("mon_ghost","Ghost","Undead","10",0,"9",9,1,"4d4","Age 10-40 years on touch, horrifying appearance (save or flee)","Only +3 or better weapon damages them","Nil","M","Fearless (20)",5000,"1","Rare","High","Chaotic Evil","H","Powerful undead bound to locations; their very sight can age a living person to dust."),
    m("mon_lich","Lich","Undead","11+",0,"6",9,1,"1d10","Touch paralyzes (save vs. paralysis or permanent), spells (18th+ wizard)","Immune to cold, sleep, charm, hold; +1 weapon to hit","Nil","M","Fearless (20)",12000,"1","Very Rare","Genius","Neutral Evil","A","Undead wizard-kings of immense power who transformed themselves to escape death."),
    m("mon_mummy","Mummy","Undead","6+3",3,"6",13,1,"1d12","Rotting disease (only healed magically), fear gaze","Only fire or +1 weapons damage them","Nil","M","Fearless (20)",975,"1-4","Uncommon","Average","Lawful Evil","D","Dessicated ancient undead wrapped in linen. Their touch rots flesh."),
    m("mon_revenant","Revenant","Undead","7",3,"9",13,1,"1d6","Gaze causes fear, strength drain (2 pts per hit)","Only fire or +1 weapon damage","Nil","M","Fearless (20)",2000,"1","Very Rare","Very","Neutral Evil","None","Undead driven by overwhelming need for vengeance against those who killed them."),
    m("mon_skeleton","Skeleton","Undead","1",7,"12",20,1,"1d6","None","Immune to cold, sleep, charm, hold","Nil","M","Fearless (20)",65,"3-12","Common","Non-","Neutral Evil","Nil","Animated skeletons; obedient servants of necromancers. Half damage from slashing/piercing."),
    m("mon_specter","Specter","Undead","7+3",2,"15, Fly 30 (B)",13,1,"1d8","Energy drain (2 levels on touch)","Only +1 or better weapon damages them","Nil","M","Fearless (20)",2000,"1-4","Uncommon","High","Lawful Evil","E","Powerful undead that drain life energy with a touch, turning victims into specters."),
    m("mon_vampire","Vampire","Undead","8+3",1,"12, Fly 18 (C)",11,1,"1d6+4","Energy drain (2 levels), charm (vampire gaze), blood drain","Regenerates 3 hp/round; only stake through heart slays","Nil","M","Fearless (20)",4000,"1-4","Rare","Exceptional","Chaotic Evil","F","The most iconic undead predators, draining blood and life levels from victims."),
    m("mon_wight","Wight","Undead","4+3",5,"12",15,1,"1d4","Energy drain (1 level per hit)","Only silver or +1 weapon damages them","Nil","M","Elite (13)",650,"2-12","Uncommon","Low","Lawful Evil","B","Undead barrow-guardians that drain life levels. Slain by them rise as wights."),
    m("mon_will_o_wisp","Will-o-wisp","Undead","9",0,"Fly 18 (A)",9,1,"2d8","Lure (intelligent, can mimic lights)","Only +3 or better weapon damages them","Nil","S","Fearless (20)",5000,"1-3","Rare","Exceptional","Chaotic Neutral","W","Malicious glowing orbs that lure travelers into deadly terrain."),
    m("mon_wraith","Wraith","Undead","5+3",4,"12, Fly 24 (A)",15,1,"1d6","Energy drain (1 level per hit)","Only silver or +1 weapon damages them","Nil","M","Elite (13)",975,"2-8","Uncommon","High","Lawful Evil","E","Insubstantial undead that drain life energy. Slain rise as wraiths."),
    m("mon_zombie","Zombie","Undead","2",8,"6",19,1,"1d8","None","Immune to cold, sleep, charm, hold","Nil","M","Fearless (20)",65,"3-12","Common","Non-","Neutral Evil","Nil","Animated corpses serving necromancers. Slow but relentless."),
]

# ─── FEY / MAGICAL BEINGS ───────────────────────────────────────────────────────
monsters += [
    m("mon_nymph","Nymph","Fey","3",9,"12, Sw 18",17,0,"None","Unearthly beauty (save vs. spell or be blinded/stunned), spells as druid 7","None","Nil","M","Champion (15)",650,"1","Rare","Very","Neutral Good","Q","Incredibly beautiful fey bound to natural places. Their beauty can blind those who look upon them."),
    m("mon_pixie","Pixie","Fey","1",5,"9, Fly 12 (A)",20,0,"None","Spells (confusion, polymorph, dancing lights), permanent invisibility","None","25%","T","Steady (11)",35,"5-20","Uncommon","Exceptional","Neutral Good","S","Tiny winged fey. They are permanently invisible until they choose to be seen."),
    m("mon_satyr","Satyr","Fey","5",5,"18",15,2,"1d6/2d4","Pan pipes (sleep, charm, fear, or dance)","None","Nil","M","Steady (11)",420,"1-8","Uncommon","Very","Chaotic Neutral","C","Half-human half-goat woodland fey. Their magical pipes can cause various effects."),
    m("mon_sprite","Sprite","Fey","1/2",5,"9, Fly 9 (A)",20,0,"1d2","Arrow curse (save or be affected by random curse)","None","Nil","T","Steady (11)",35,"3-18","Uncommon","High","Neutral Good","T","Tiny woodland fey that shoot cursed arrows at intruders."),
    m("mon_couatl","Couatl","Outsider","9",5,"6, Fly 18 (B)",11,2,"1d3/2d4","Spells (wizard and priest), constrict, poison bite (save or die)","None","Nil","L","Fanatic (17)",3000,"1","Very Rare","Genius","Lawful Good","E","Feathered serpents revered as divine messengers. Powerful spell casters."),
    m("mon_unicorn","Unicorn","Magical Beast","4+4",2,"24",15,3,"1d6/1d6/1d12","Horn touch heals, teleport 1/day, sense evil","None","25%","L","Champion (15)",650,"1","Rare","Average","Chaotic Good","None","White magical horses with a single horn that can heal. They accept only virginal riders."),
]

# ─── MAGICAL BEASTS / MONSTROUS BEASTS ─────────────────────────────────────────
monsters += [
    m("mon_ankheg","Ankheg","Magical Beast","3+4",3,"12, Burrow 6",16,2,"1d6/2d6","Acid spray (8d4, 5 foot area, 2/day)","Burrowing ambush, hard carapace","Nil","L","Steady (11)",420,"1-3","Uncommon","Non-","Neutral","Nil","Giant insectoid burrowers with powerful mandibles and acid spray."),
    m("mon_basilisk","Basilisk","Magical Beast","6+1",4,"6",15,1,"1d10","Petrifying gaze (save vs. petrification or turn to stone)","None","Nil","M","Steady (11)",975,"1-2","Uncommon","Animal","Neutral","D","Reptilian creatures whose gaze can turn any living thing to stone."),
    m("mon_bulette","Bulette","Magical Beast","9",4,"14, Burrow 3",11,3,"2d6+1/2d6+1/3d6","Leap attack (all four claws), burrowing ambush","Hard armored hide","Nil","L","Fearless (20)",2000,"1","Rare","Animal","Neutral","Nil","Armored burrowing predators called landshark. They leap to attack with all claws."),
    m("mon_gorgon","Gorgon","Magical Beast","8",2,"12",11,1,"2d6","Petrifying breath weapon (save vs. breath or turn to stone)","Iron hide","Nil","L","Fearless (20)",2000,"1-2","Rare","Animal","Neutral","Nil","Iron-plated bovine monsters whose breath can turn living things to stone."),
    m("mon_griffon","Griffon","Magical Beast","7",5,"12, Fly 30 (E)",13,3,"1d4/1d4/2d8","Rake with rear claws","None","Nil","L","Steady (11)",650,"2-8","Uncommon","Animal","Neutral","Nil","Half-eagle half-lion raptors that are prized as mounts."),
    m("mon_harpy","Harpy","Magical Beast","3",7,"6, Fly 15 (D)",17,3,"1d3/1d3/1d6","Song lures victims (save vs. spell), touch causes submission","None","Nil","M","Steady (11)",175,"1-6","Uncommon","Low","Chaotic Evil","B","Winged female monsters with enchanting voices that lure prey."),
    m("mon_hell_hound","Hell Hound","Magical Beast","4",4,"12",15,1,"1d10","Fire breath (4d4), immune to fire","Detect invisible creatures","Nil","M","Steady (11)",420,"2-8","Uncommon","Semi-","Lawful Evil","C","Fire-breathing dogs from the lower planes used as guardians by evil beings."),
    m("mon_hippogriff","Hippogriff","Magical Beast","3+3",5,"18, Fly 36 (E)",16,3,"1d6/1d6/1d10","Aerial charge","None","Nil","L","Steady (11)",175,"2-8","Common","Animal","Neutral","Nil","Half-eagle half-horse creatures; rivals of griffons."),
    m("mon_hydra","Hydra","Magical Beast","5-12",5,"12","Varies",5,"1d10 each","Each head attacks independently, decapitation","Regenerates severed heads (unless cauterized)","Nil","L","Fearless (20)",2000,"1","Uncommon","Animal","Neutral","Nil","Multi-headed serpentine creatures. Each head can be severed but grows back unless burned."),
    m("mon_manticore","Manticore","Magical Beast","6+3",4,"12, Fly 18 (D)",13,3,"1d4/1d4/2d4","Tail spikes (6 per volley, 1d6 each, 180 foot range)","None","Nil","L","Steady (11)",975,"1-2","Uncommon","Low","Chaotic Evil","D","Lion-bodied monsters with human faces and spike-firing tails."),
    m("mon_medusa","Medusa","Magical Beast","6",5,"9",15,1,"1d4","Petrifying gaze (save vs. petrification or turn to stone), snake-hair bite (poison)","None","Nil","M","Steady (11)",975,"1-3","Rare","Very","Lawful Evil","F","Snake-haired humanoids whose direct gaze turns victims to stone."),
    m("mon_nightmare","Nightmare","Outsider","6+6",2,"15, Fly 36 (B)",13,3,"2d4/2d4/2d4","Smoking hooves set floor on fire, plane shift","None","Nil","L","Champion (15)",2000,"1","Very Rare","Average","Neutral Evil","Nil","Demonic black horses with flaming hooves that can cross planes."),
    m("mon_owlbear","Owlbear","Magical Beast","5+2",5,"12",15,3,"1d6/1d6/1d8","Hug (2d8 if both claws hit)","None","Nil","L","Steady (11)",650,"1-4","Uncommon","Semi-","Neutral","C","Bear-owl hybrids with a vicious bear-hug attack."),
    m("mon_pegasus","Pegasus","Magical Beast","4",6,"24, Fly 48 (C)",15,3,"1d6/1d6/1d8","None","None","Nil","L","Champion (15)",420,"1-2","Rare","Average","Chaotic Good","None","Winged horses of good alignment, sometimes used as mounts by good characters."),
    m("mon_peryton","Peryton","Magical Beast","4",7,"6, Fly 21 (C)",15,2,"1d2/2d8","Must tear out heart of victim (shadow cast is human)","None","Nil","M","Steady (11)",420,"1-4","Rare","Average","Chaotic Evil","B","Winged stag-bodied predators that cast the shadow of a human. They must eat hearts to reproduce."),
    m("mon_remorhaz","Remorhaz","Magical Beast","7-14",0,"12",13,1,"7d6","Heat body to volcanic temperature (metal weapons melt, contact causes 10d10 heat damage)","None","Nil","H","Fearless (20)",3000,"1","Rare","Animal","Neutral","Nil","Enormous Arctic centipede-like creatures whose backs burn at volcanic temperature."),
    m("mon_roc","Roc","Magical Beast","18",4,"3, Fly 48 (B)",3,3,"3d6/3d6/4d6","Carry off prey, talons pin victims","None","Nil","G","Fearless (20)",6000,"1","Very Rare","Animal","Neutral","Nil","Gargantuan birds capable of carrying off elephants. Their eggs are prized treasures."),
    m("mon_salamander","Salamander","Magical Beast","7+7",5,"9",13,3,"2d6/1d6/1d6","Heat aura (1d6 to anyone within 5 feet), fire immunity","Immune to fire","Nil","M","Elite (13)",2000,"2-5","Rare","Average","Chaotic Evil","E","Elemental fire creatures with serpentine lower bodies; heat burns those near them."),
    m("mon_sphinx_androsphinx","Sphinx, Androsphinx","Magical Beast","12",0,"18, Fly 30 (C)",9,3,"2d10/2d10/1d4","Roar (fear/confusion/deafness at different levels), spells","None","Nil","L","Fearless (20)",4000,"1","Very Rare","Genius","Chaotic Good","H","Male sphinxes, wise and powerful guardians that test heroes with riddles."),
    m("mon_sphinx_gynosphinx","Sphinx, Gynosphinx","Magical Beast","8",0,"18, Fly 24 (C)",11,2,"2d6/2d6","Riddles (geas if unsolved), spells","None","Nil","L","Fanatic (17)",2000,"1","Very Rare","Genius","Neutral","E","Female sphinxes that guard ancient secrets with riddles."),
    m("mon_stirge","Stirge","Magical Beast","1+1",7,"3, Fly 18 (C)",20,1,"1d3","Blood drain (1d4/round after attach)","None","Nil","T","Unsteady (7)",35,"3-30","Common","Animal","Neutral","Nil","Mosquito-like flying parasites that drain blood from living creatures."),
    m("mon_su_monster","Su-Monster","Magical Beast","5+5",3,"12",15,5,"2d4/2d4/1d4/1d4/1d4","Psionic powers, rear claw rake","None","Nil","M","Steady (11)",650,"2-8","Rare","Low","Chaotic Evil","B","Ape-like psionic predators from the Outer Planes."),
    m("mon_wyvern","Wyvern","Dragon","7+7",3,"6, Fly 18 (E)",13,2,"2d8/1d6","Poison stinger tail (save vs. poison or die in 1 round)","None","Nil","G","Steady (11)",1400,"1-6","Uncommon","Low","Neutral (Evil)","E","Two-legged dragons with a deadly poison-tipped stinger tail."),
]

# ─── OUTSIDERS / PLANAR ─────────────────────────────────────────────────────────
monsters += [
    m("mon_baatezu_general","Baatezu (Devil)","Outsider","Varies",0,"12, Fly 18",11,2,"By type","Varies by type (ice, fire, darkness)","Immune to fire and poison","See desc","M","Fearless (20)",3000,"1-4","Very Rare","High","Lawful Evil","G","Baatezu are devils from the Nine Hells; powerful lawful evil outsiders."),
    m("mon_imp","Imp","Outsider","2+2",3,"6, Fly 16 (C)",19,1,"1d4","Poison sting (save vs. poison or die in 1 round), shape change","Immune to fire, cold, poison; +2 weapon to hit","25%","T","Champion (15)",420,"1","Very Rare","Average","Lawful Evil","Nil","Small devil familiars. They can polymorph into animals."),
    m("mon_pit_fiend","Pit Fiend","Outsider","13",0,"15, Fly 15 (B)",7,6,"1d4/1d4/2d4+1/2d4+1/1d4/3d6","Fear aura, constrict, poison tail, symbol spells","Immune to fire; +2 weapon to hit","50%","L","Fearless (20)",8000,"1","Very Rare","Genius","Lawful Evil","H","The most powerful baatezu; generals of the armies of Hell."),
    m("mon_succubus","Succubus","Outsider","6",0,"12, Fly 18 (B)",15,2,"1d3/1d3","Energy drain on kiss (2 levels), charm person (gaze), plane shift","Immune to fire, cold, electricity, poison; +1 weapon to hit","30%","M","Champion (15)",3000,"1","Rare","Genius","Chaotic Evil","None","Seductive demons that drain life energy through a kiss."),
    m("mon_tanarri_balor","Balor","Outsider","13",-3,"9, Fly 15 (B)",7,2,"2d4+5/1d6+2","Vorpal sword, entangling whip, immolation aura (3d6 to nearby)","Immune to fire and lightning; +2 weapon to hit","50%","L","Fearless (20)",10000,"1","Very Rare","High","Chaotic Evil","G","The mightiest tanar'ri; among the most powerful of all demons."),
    m("mon_genie_djinni","Djinni","Outsider","7+3",4,"9, Fly 24 (A)",13,1,"2d8","Whirlwind, create objects and food, gaseous form, spells","None","Nil","L","Champion (15)",2000,"1","Very Rare","High","Chaotic Good","Nil","Powerful air genies that can grant wishes and take gaseous or whirlwind form."),
    m("mon_genie_efreeti","Efreeti","Outsider","10",2,"9, Fly 24 (A)",9,1,"3d8","Flame, wish (3x), gaseous form, polymorph","Immune to fire","Nil","L","Fanatic (17)",3000,"1","Very Rare","Very","Lawful Evil","Nil","Powerful fire genies; crafty and hostile, trapped by wizards to grant wishes."),
    m("mon_invisible_stalker","Invisible Stalker","Elemental","8",3,"Fly 12 (A)",13,1,"4d4","Always invisible, surprised on 5 in 6","None","Nil","L","Champion (15)",2000,"1","Rare","Low","Neutral","Nil","Air elementals summoned as servants that track targets invisibly."),
    m("mon_nightmare","Nightmare","Outsider","6+6",2,"15, Fly 36 (B)",13,3,"2d4/2d4/2d4","Smoking hooves ignite flammables, plane shift at will","None","Nil","L","Champion (15)",2000,"1","Very Rare","Average","Neutral Evil","Nil","Demonic black horses with flaming hooves that carry evil riders across planes."),
    m("mon_rakshasa","Rakshasa","Outsider","7",0,"15",13,3,"1d3/1d3/1d3+4","Spells (innate and learned), mislead others","Only +3 or better weapons damage them","25%","M","Champion (15)",3000,"1-4","Rare","High","Lawful Evil","F","Evil tiger-headed spirits in humanoid bodies. Only blessed crossbow bolts can kill them."),
]

# ─── GIANTS & GIANT-KIN ─────────────────────────────────────────────────────────
monsters += [
    m("mon_giant_cloud","Cloud Giant","Giant","16+2",2,"15",9,1,"6d6","Hurl boulders, keen scent (detect enemies up to 1 mile)","None","Nil","H","Fanatic (17)",4000,"1-4","Very Rare","Average","Neutral (Good/Evil)","E,Q","Largest of all true giants; dwell in cloud castles."),
    m("mon_giant_fire","Fire Giant","Giant","11+3",3,"12",9,1,"5d6","Hurl boulders, fire immunity","Immune to fire","Nil","H","Elite (13)",2000,"1-4","Rare","Average","Lawful Evil","E,Q","Jet-black skinned giants with flame-red hair; dwell in volcanic keeps."),
    m("mon_giant_frost","Frost Giant","Giant","10+1",4,"12",9,1,"4d6+4","Hurl boulders, cold immunity","Immune to cold","Nil","H","Elite (13)",2000,"1-4","Rare","Average","Chaotic Evil","E,Q","Pale-blue giants from frozen northern lands."),
    m("mon_giant_hill","Hill Giant","Giant","8+2",5,"12",11,1,"2d8","Hurl rocks","None","Nil","H","Steady (11)",1000,"1-4","Uncommon","Low","Chaotic Evil","C,Q","The least intelligent of giants; dwell in hills and caves."),
    m("mon_giant_stone","Stone Giant","Giant","9+3",4,"12",9,1,"3d6","Hurl boulders with remarkable accuracy","None","Nil","H","Elite (13)",1400,"1-4","Rare","Average","Neutral","D,Q","Lean gray-skinned giants dwelling in underground caverns."),
    m("mon_giant_storm","Storm Giant","Giant","19+3",-3,"15, Sw 15",5,1,"7d6","Hurl boulders, control weather, lightning bolt (3/day)","None","Nil","H","Fanatic (17)",6000,"1-4","Very Rare","Exceptional","Chaotic Good","E,Q","The wisest and most powerful of giants; they wield lightning."),
    m("mon_cyclops","Cyclops","Giant","8+1",5,"15",11,1,"2d10","Hurl boulders; poor depth perception (-2 to all attacks)","None","Nil","L","Unsteady (5)",1400,"1-6","Uncommon","Low","Neutral Evil","D","Single-eyed giants of low intelligence and brutal nature."),
    m("mon_ettin","Ettin","Giant","10",3,"12",9,2,"2d6/3d6","Two heads give 6 in 10 chance to detect intruders","None","Nil","L","Steady (11)",2000,"1-2","Uncommon","Low","Chaotic Evil","C","Two-headed giants that fight with a weapon in each hand simultaneously."),
    m("mon_ogre","Ogre","Giant","4+1",5,"9",15,1,"1d10","Club smash","None","Nil","L","Steady (11)",270,"2-8","Common","Low","Chaotic Evil","C,Q","Brutish and greedy giant-kin. Strong but dim-witted."),
    m("mon_ogre_mage","Ogre Mage","Giant","5+2",4,"9, Fly 15 (B)",13,1,"1d12","Charm, sleep, darkness, cone of cold, polymorph self, fly (innate)","Regenerates 1 hp/round","Nil","L","Champion (15)",2000,"1-4","Rare","High","Lawful Evil","E","Oriental ogre-kin with powerful innate magical abilities."),
    m("mon_titan","Titan","Giant","21",0,"21",5,2,"7d6/7d6","Spells (20th level wizard and priest), all giant abilities","None","Nil","H","Fanatic (17)",10000,"1","Very Rare","Genius","Chaotic Good","H,X","Demigod-level giants descended from divine beings."),
]

# ─── HUMANOIDS ─────────────────────────────────────────────────────────────────
monsters += [
    m("mon_aarakocra","Aarakocra","Humanoid","1+1",6,"6, Fly 18 (B)",19,2,"1d4/1d4","Javelin throwing, dive attack","None","Nil","M","Steady (11)",35,"2-8","Uncommon","Average","Neutral","C","Bird-like humanoids with eagle features; natural flyers."),
    m("mon_bugbear","Bugbear","Humanoid","3+1",5,"9",16,1,"2d4","Surprise on 3 in 6 due to natural stealth","None","Nil","L","Steady (11)",270,"2-8","Common","Low","Chaotic Evil","C","Large goblinoids known for their stealth and brutality."),
    m("mon_centaur","Centaur","Humanoid","4",5,"18",15,3,"1d6/1d6/1d6","Charge (double damage), trample (2d6)","None","Nil","L","Steady (11)",420,"4-16","Uncommon","Average","Neutral (Good)","None","Half-human half-horse beings; proud woodland defenders."),
    m("mon_doppleganger","Doppleganger","Humanoid","4",5,"9",15,1,"1d12","Polymorph into any humanoid form at will","Immune to sleep and charm","Nil","M","Steady (11)",420,"1-6","Uncommon","Average","Neutral","E","Shape-changers that mimic others perfectly to infiltrate groups."),
    m("mon_duergar","Duergar","Humanoid","1+1",4,"6",19,1,"By weapon","Expand to giant size, become invisible, psionic powers","Immune to paralysis, poison, illusion, phantasm","Nil","S","Steady (11)",65,"2-8","Uncommon","Average","Lawful Evil","G","Deep dwarves of the underdark; cruel, psionic, enemies of all surface races."),
    m("mon_gnoll","Gnoll","Humanoid","2",5,"9",19,1,"2d4","Pack tactics","None","Nil","M","Steady (11)",65,"2-8","Common","Low","Chaotic Evil","D","Hyena-headed humanoids known for savagery and scavenging."),
    m("mon_gnome","Gnome","Humanoid","1",5,"6",20,1,"By weapon","Illusion magic, combat bonus vs. kobolds and goblins","None","Nil","S","Steady (11)",35,"5-20","Common","Average","Neutral Good","E","Small folk with affinity for illusion magic and fine craftsmanship."),
    m("mon_goblin","Goblin","Humanoid","1-1",6,"6",20,1,"1d6","Combat penalty in sunlight","None","Nil","S","Unsteady (7)",15,"2-20","Common","Low","Lawful Evil","K","Small nasty humanoids; the most common dungeon denizens."),
    m("mon_halfling","Halfling","Humanoid","1",7,"9",20,1,"By weapon","Sling speciality, excellent at hiding","None","Nil","S","Steady (11)",35,"4-24","Common","Average","Lawful Good","None","Small folk known for their incredible luck and natural stealth."),
    m("mon_hobgoblin","Hobgoblin","Humanoid","1+1",5,"9",19,1,"1d8","Military discipline and formations","None","Nil","M","Steady (11)",35,"2-8","Common","Average","Lawful Evil","D","Larger organized goblinoids that wage disciplined warfare."),
    m("mon_human_warrior","Human, Warrior","Humanoid","1",10,"12",20,1,"By weapon","Varies by character class","None","Nil","M","Steady (11)",35,"1-20","Common","Average","Varies","Varies","Generic human warrior encountered in the world."),
    m("mon_kobold","Kobold","Humanoid","1-1",7,"6",20,1,"1d4","Traps, combat penalty in daylight","None","Nil","S","Unsteady (7)",7,"4-16","Common","Low","Lawful Evil","K","Tiny reptilian humanoids; cowardly but very dangerous in large numbers."),
    m("mon_kuo_toa","Kuo-toa","Humanoid","2",4,"9, Sw 18",19,2,"1d6/1d4","Pin with adhesive shield, lightning bolt (from group focus)","Immune to illusions, see invisible creatures","Nil","M","Steady (11)",175,"2-12","Rare","Average","Neutral Evil","C","Degenerate fish-men worshipping demon lords in underwater temples."),
    m("mon_lizard_man","Lizard Man","Humanoid","2+1",5,"6, Sw 12",19,3,"1d2/1d2/1d6","None","None","Nil","M","Steady (11)",175,"10-40","Common","Low","Neutral","D","Reptilian humanoids dwelling in swamps and marshes."),
    m("mon_locathah","Locathah","Humanoid","2",5,"12, Sw 18",19,1,"1d6 or by weapon","None","None","Nil","M","Steady (11)",65,"2-16","Common","Average","Lawful Neutral","C","Orderly fish-like humanoids living in aquatic communities."),
    m("mon_merman","Merman","Humanoid","2+1",6,"Sw 18",19,1,"By weapon","Can summon sea creatures","None","Nil","M","Steady (11)",175,"2-20","Uncommon","Average","Neutral","B","Half-human half-fish beings of the seas and oceans."),
    m("mon_orc","Orc","Humanoid","1",6,"9",19,1,"1d8","Combat penalty in bright sunlight, warband tactics","None","Nil","M","Steady (11)",15,"2-12","Common","Low","Chaotic Evil","D","Pig-snouted humanoids organized into warlike tribes; notorious raiders."),
    m("mon_sahuagin","Sahuagin","Humanoid","2+2",5,"12, Sw 24",17,3,"1d4/1d4/1d4","Blood frenzy in water, 4 arms, expert with spear","None","Nil","M","Steady (11)",175,"2-16","Uncommon","Low","Lawful Evil","B","Shark-like sea devils that raid coastal and underwater settlements."),
    m("mon_svirfneblin","Svirfneblin","Humanoid","3",4,"15",17,1,"By weapon","Camouflage in stone, illusion spells, psionic defense","Non-detection, magic resistance +4","Nil","S","Elite (13)",270,"10-40","Uncommon","Average","Neutral","G","Deep gnomes of the underdark; neutral, reclusive, and highly magical."),
    m("mon_thri_kreen","Thri-kreen","Humanoid","5",5,"18",15,5,"2d4/1d4/1d4/1d4/1d4","Paralyzing poison bite (save vs. poison or be paralyzed for 2d10 rounds), great leap","None","Nil","L","Elite (13)",650,"1-4","Rare","Average","Neutral","D","Praying mantis-like humanoids with 4 arms; fierce hunters."),
    m("mon_troglodyte","Troglodyte","Humanoid","2",5,"12",19,3,"1d4/1d4/1d4","Nauseating stench repels most (save vs. poison or -2 to attacks), natural camouflage in stone","None","Nil","M","Steady (11)",65,"10-40","Common","Low","Chaotic Evil","C","Lizard-like humanoids with a debilitating body stench."),
    m("mon_yuan_ti","Yuan-ti","Humanoid","Varies",0,"12",11,2,"By weapon/bite","Spells, poison, various shape-change forms","Immune to poison","20%","M","Fanatic (17)",2000,"2-8","Rare","High","Chaotic Evil","E","Serpentine humanoids in various hybrid forms devoted to dark serpent cults."),
]

# ─── LYCANTHROPES ───────────────────────────────────────────────────────────────
monsters += [
    m("mon_lycanthrope_werewolf","Werewolf","Lycanthrope","4+4",5,"15",15,2,"2d4/2d4","Bite infects lycanthropy (save vs. poison each attack)","Only silver or +2 weapons damage them in wolf form","Nil","M","Steady (11)",650,"1-6","Uncommon","Low","Chaotic Evil","C","Humans who transform into wolves; the most common lycanthrope."),
    m("mon_lycanthrope_wererat","Wererat","Lycanthrope","3+3",6,"12",16,3,"1d3/1d3/1d8","Bite infects lycanthropy, usually armed","Only silver or +2 weapons damage them in rat form","Nil","M","Steady (11)",420,"1-6","Uncommon","Low","Lawful Evil","C","Crafty were-rats; often found as thieves guild leaders."),
    m("mon_lycanthrope_werebear","Werebear","Lycanthrope","7+7",2,"9",13,3,"1d6/1d6/2d6","Hug on both claw hits (2d6 extra), bite infects lycanthropy","Only silver or +2 weapons damage them in bear form","Nil","L","Champion (15)",2000,"1-4","Uncommon","Average","Neutral Good","C","Powerful were-bears; among the most benevolent lycanthropes."),
    m("mon_lycanthrope_wereboar","Wereboar","Lycanthrope","5+5",4,"12",13,1,"2d6","Bite infects lycanthropy, continue attacking when at 0 hp (berserk)","Only silver or +2 weapons damage them in boar form","Nil","M","Elite (13)",650,"1-4","Uncommon","Low","Neutral","C","Aggressive were-boars that berserk in combat."),
    m("mon_lycanthrope_weretiger","Weretiger","Lycanthrope","5+5",3,"12",13,4,"1d6/1d6/1d6/1d6","Rear claw rake (2d4 each) if front claws hit, bite infects lycanthropy","Only silver or +2 weapons damage them in tiger form","Nil","M","Champion (15)",975,"1-2","Uncommon","Average","Neutral","D","Powerful and cunning were-tigers; solitary hunters."),
]

# ─── OOZES / FUNGI / SLIMES ─────────────────────────────────────────────────────
monsters += [
    m("mon_black_pudding","Black Pudding","Ooze","10",6,"6",11,1,"3d8","Dissolve metals and organic matter (corrodes in 1 round)","Immune to cold; splitting from lightning or physical attacks","Nil","L","Steady (11)",2000,"1","Uncommon","Non-","Neutral","Nil","Black corrosive ooze that dissolves metal and organic matter on contact."),
    m("mon_gelatinous_cube","Gelatinous Cube","Ooze","4",8,"6",15,1,"2d4","Engulf and paralyze (save vs. paralysis), dissolve organic matter","Immune to lightning, cold, paralysis, polymorph","Nil","L","Steady (11)",420,"1","Common","Non-","Neutral","*","Transparent 10-foot cube of acidic gelatin; common dungeon scavengers."),
    m("mon_green_slime","Green Slime","Ooze","2 hp",9,"0","N/A",0,"None","Contact turns flesh to green slime (save vs. poison); only fire/cold/cure disease destroys it","Immune to all weapons","Nil","S","Steady (11)",175,"1","Common","Non-","Neutral","Nil","Mindless green corrosive slime that turns flesh into more slime."),
    m("mon_ochre_jelly","Ochre Jelly","Ooze","6",8,"3",15,1,"3d4","Dissolve flesh and wood (not metal or stone)","Immune to lightning (splits); immune to blunt weapons","Nil","L","Steady (11)",650,"1","Common","Non-","Neutral","Nil","Mindless yellow ooze that dissolves flesh and wood on contact."),
]

# ─── PLANTS / FUNGAL ────────────────────────────────────────────────────────────
monsters += [
    m("mon_shambling_mound","Shambling Mound","Plant","8+8",0,"6, Sw 6",9,2,"2d8/2d8","Engulf and suffocate (save vs. death)","Immune to fire; healed by electricity","Nil","L","Fearless (20)",2000,"1","Rare","Non-","Neutral","Q","Animated mounds of rotting vegetation; electrical attacks make them grow."),
    m("mon_treant","Treant","Plant","7-12",0,"12",13,2,"4d6/4d6","Animate trees (2 trees/treant), stomp","None","Nil","H","Champion (15)",3000,"1-20","Rare","Exceptional","Chaotic Good","Q","Ancient tree-guardians of forests; can animate and command other trees."),
]

# ─── NAGAS ──────────────────────────────────────────────────────────────────────
monsters += [
    m("mon_naga_guardian","Guardian Naga","Naga","11+2",5,"15",9,2,"1d6/2d6","Spit poison (20 foot range, save vs. poison or die), spells (7th level wizard/priest)","None","Nil","L","Fanatic (17)",3000,"1","Rare","High","Lawful Good","E","Wise serpentine guardians of sacred sites and powerful magical items."),
    m("mon_naga_water","Water Naga","Naga","7+7",5,"9, Sw 18",13,1,"1d4","Poison bite (save vs. poison or be paralyzed), spells (5th level wizard)","None","Nil","L","Elite (13)",2000,"1-3","Rare","Very","Neutral","B","Aquatic nagas dwelling in lakes and rivers; guardians of water places."),
    m("mon_naga_bone","Bone Naga","Naga","9",5,"15",11,2,"1d6/2d4","Energy drain (1 level), spells (6th level wizard)","Immune to mind effects, cold","Nil","L","Fearless (20)",2000,"1","Very Rare","Very","Chaotic Evil","E","Undead nagas serving dark masters; they drain energy with a touch."),
]

# ─── GOLEMS ─────────────────────────────────────────────────────────────────────
monsters += [
    m("mon_golem_flesh","Flesh Golem","Construct","9",9,"9",11,2,"2d8/2d8","Berserk (uncontrolled) on a 1 in 20","Immune to magic except fire (slows) and cold (slows); only +1 or better weapons","Nil","L","Fearless (20)",3000,"1","Very Rare","Non-","Neutral","Nil","Humanoid constructs assembled from corpse parts and animated by lightning."),
    m("mon_golem_stone","Stone Golem","Construct","14",5,"6",9,1,"3d8","Slow spell 1/round","Immune to most magic; only +2 or better weapons","Nil","L","Fearless (20)",5000,"1","Very Rare","Non-","Neutral","Nil","Animated stone warriors immune to most magic."),
    m("mon_golem_iron","Iron Golem","Construct","18",3,"6",7,1,"4d10","Poison breath (cloud, save vs. poison or die)","Immune to all magic except lightning (slows) and fire (heals); only +3 or better weapons","Nil","L","Fearless (20)",10000,"1","Very Rare","Non-","Neutral","Nil","The most powerful golem type; immune to nearly all magic."),
]

# ─── MISCELLANEOUS MONSTERS ─────────────────────────────────────────────────────
monsters += [
    m("mon_basilisk","Basilisk","Magical Beast","6+1",4,"6",15,1,"1d10","Petrifying gaze (save vs. petrification or turn to stone)","None","Nil","M","Steady (11)",975,"1-2","Uncommon","Animal","Neutral","D","Reptilian creatures whose gaze can turn any living thing to stone."),
    m("mon_chimera","Chimera","Dragon","9",6,"9, Fly 18 (D)",11,6,"1d3/1d3/1d4/1d4/2d4/3d4","Fire breath (3/day, 3d8 damage)","None","Nil","L","Steady (11)",2000,"1-4","Uncommon","Semi-","Chaotic Evil","F","Three-headed lion/goat/dragon hybrids that breathe fire."),
    m("mon_cockatrice","Cockatrice","Magical Beast","5",6,"6, Fly 18 (C)",15,1,"1d3","Petrifying bite (save vs. petrification or turn to stone)","None","Nil","S","Steady (11)",650,"1-3","Uncommon","Animal","Neutral","C","Rooster-lizard hybrids whose bite petrifies living creatures."),
    m("mon_gargoyle","Gargoyle","Magical Beast","4+4",5,"9, Fly 15 (D)",15,4,"1d3/1d3/1d6/1d4","None","Only +1 or better weapons; immune to cold, fire, sleep, charm","Nil","M","Steady (11)",420,"2-8","Common","Low","Chaotic Evil","Nil","Stone-appearing demons; ambush prey from above."),
    m("mon_lamia","Lamia","Magical Beast","9",3,"24",11,3,"1d4/1d4/2d4","Touch drains Wisdom (1 point permanently), charm, illusion spells","None","30%","L","Elite (13)",3000,"1-4","Rare","Average","Chaotic Evil","D","Lion-bodied with humanoid female upper body; they drain wisdom on touch."),
    m("mon_minotaur","Minotaur","Magical Beast","6+3",6,"12",13,3,"2d4/2d4/1d6","Never lost in maze, gore with horns","None","Nil","L","Steady (11)",975,"1-4","Uncommon","Low","Chaotic Evil","C","Bull-headed humanoid monsters; unable to get lost in mazes."),
    m("mon_mind_flayer","Mind Flayer","Aberration","8+4",5,"12",9,4,"1d4/1d4/1d4/1d4","Mind blast (stun 3-12 rounds), brain extraction (instant kill), psionics","None","90%","M","Fearless (20)",4000,"1-4","Very Rare","Genius","Lawful Evil","E","Tentacled aberrations that eat brains. Among the most feared and intelligent monsters."),
    m("mon_slaad_red","Slaad, Red","Outsider","7",6,"10",13,4,"1d4+1/1d4+1/2d8+1/2d8+1","Implant slaad eggs in wounds (incubate and burst in 3 months)","+2 weapon to hit","30%","L","Elite (13)",975,"2-5","Very Rare","Low","Chaotic Neutral","C","Chaotic frog-like outsiders from Limbo. Their wounds implant lethal eggs."),
    m("mon_tarrasque","Tarrasque","Magical Beast","300 hp",-3,"9",5,6,"1d12/1d12/2d12/1d10/1d10","Fearsome aura (flee or fight at -2), reflective carapace, regeneration","Nearly indestructible; only a wish can slay it when at 0 hp","Nil","G","Fearless (20)",55000,"1","Unique","Animal","Neutral","Nil","The most feared creature in existence. One of a kind; only a wish can permanently slay it."),
    m("mon_troll","Troll","Giant","6+6",4,"12",13,3,"1d4+4/1d4+4/2d6","Regenerates 3 hp/round (only fire/acid stops it)","Only fire and acid prevent regeneration","Nil","L","Steady (11)",975,"1-4","Common","Low","Chaotic Evil","D","Regenerating monsters that continue fighting until burned. Even severed limbs fight on."),
    m("mon_umber_hulk","Umber Hulk","Aberration","8+8",2,"6, Burrow 6",9,4,"3d4/3d4/1d10+7","Confusing gaze (save vs. spell or act randomly for 3-12 rounds)","Burrowing ability","Nil","L","Elite (13)",4000,"1-4","Uncommon","Average","Chaotic Evil","D","Powerful burrowers with a gaze that confuses those who see all four of their eyes."),
    m("mon_yeti","Yeti","Magical Beast","4+4",6,"15",15,2,"1d6/1d6","Hug (2d8 if both claws hit), paralyzing cold gaze","Immune to cold","Nil","L","Steady (11)",420,"1-6","Rare","Low","Neutral","Nil","White-furred ape-like creatures from frozen mountains; their gaze paralyzes with cold."),
]

# ─── NAGA variants (water, guardian already above) ────────────────────────────
# ─── SLAAD, REVENANT, SHADOW, SHADOW ALREADY HANDLED ─────────────────────────
monsters += [
    m("mon_shadow","Shadow","Undead","3+3",7,"9",16,1,"2d4","Strength drain (1 point per hit, temporary), create shadow (slain become shadows)","Only magic weapons hit them; immune to sleep, charm, cold, hold","Nil","M","Steady (11)",175,"2-8","Common","Low","Chaotic Evil","F","Undead shadows that drain strength and spawn more shadows from victims."),
]

# ─── SNAKES ─────────────────────────────────────────────────────────────────────
monsters += [
    m("mon_snake_giant_constrictor","Snake, Giant Constrictor","Magical Beast","6+1",5,"9, Sw 9",15,2,"1d4/1d4","Constriction (2d8/round, save vs. petrification to escape)","None","Nil","L","Steady (11)",650,"1-2","Common","Animal","Neutral","Nil","Enormous constricting snakes; can squeeze prey to death."),
    m("mon_snake_giant_poisonous","Snake, Giant Venomous","Magical Beast","4+2",5,"15, Sw 15",15,1,"1d4","Poison bite (save vs. poison or die)","None","Nil","M","Steady (11)",420,"1-3","Common","Animal","Neutral","Nil","Large highly venomous snakes; their bite is often lethal."),
]

# ─── SPIDER ─────────────────────────────────────────────────────────────────────
monsters += [
    m("mon_spider_giant","Spider, Giant","Magical Beast","4+4",4,"3, Climb 12",15,1,"2d4","Deadly poison bite (save vs. poison at -2 or die in 1 turn)","Web strands everywhere","Nil","L","Steady (11)",420,"1-3","Common","Animal","Neutral","C","Massive spiders with deadly venom. They web entire areas as trap zones."),
]

# ─── PRINT THE COUNT ─────────────────────────────────────────────────────────────
# Remove duplicates by id (take last occurrence)
seen = {}
for mon in monsters:
    seen[mon["id"]] = mon
unique_monsters = list(seen.values())

print(f"Total unique monsters: {len(unique_monsters)}")

# Write to file
output_data = {"monsters": unique_monsters}
with open(OUTPUT, 'w', encoding='utf-8') as f:
    json.dump(output_data, f, indent=2, ensure_ascii=False)

print(f"Written to {OUTPUT}")

# Verify
with open(OUTPUT, 'r', encoding='utf-8') as f:
    check = json.load(f)
print(f"Verified: {len(check['monsters'])} monsters in JSON file")
print("IDs:", sorted([x['id'] for x in check['monsters']])[:10], "...")
