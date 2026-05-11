# Armor Regression Checklist

Use this checklist after any armor, inventory, or economy changes.

## Setup

1. Open Edit Info and select a rogue (or bard) and a non-rogue character.
2. Ensure equipment library has armor entries with ArmorClassValue and RogueArmorProfile.
3. Note baseline values shown in character summary:
   - Armor Class
   - Rogue Armor Profile
   - Coin totals

## Equipment Editor Validation

1. Open an existing armor item in the equipment editor.
2. Confirm these fields load correctly:
   - This item is armor
   - Armor Class (descending AC value)
   - Rogue armor profile
3. Toggle This item is armor off:
   - Armor Class and Rogue profile fields should disable.
4. Toggle it on again:
   - Armor Class and Rogue profile fields should enable.
5. Save changes, refresh equipment, and reopen item:
   - Values should persist exactly.

## Buy Flow Validation

1. Buy armor with a known cost for a selected character.
2. Confirm:
   - Coins are reduced by the item cost x quantity.
   - Equipment list gains the item/quantity.
   - Armor Class updates from best equipped armor.
   - Rogue Armor Profile updates for rogue/bard.
3. Buy a non-armor item:
   - Armor Class and Rogue Armor Profile should remain unchanged.

## Sell and Unequip Flow Validation

1. Open Sell / Unequip / Add Funds.
2. Remove armor quantity only (set funds to 0):
   - Equipment quantity decreases or item is removed.
   - Armor Class recalculates immediately.
   - Rogue Armor Profile recalculates immediately.
3. Add funds only (remove quantity = 0):
   - Coins increase.
   - Equipment and armor state remain unchanged.
4. Remove quantity and add funds together:
   - Both inventory and coins update in one save.
5. Submit with remove quantity = 0 and funds = 0:
   - UI should reject with no-change message.

## Character Generation and Review Validation

1. In CharGen equipment step, add an armor item.
2. Finalize in review.
3. Confirm resulting character sheet has:
   - EquipmentSelection armor metadata (IsArmor, ArmorClassValue, RogueArmorProfile, WeightEach)
   - Correct Armor Class after finalize
   - Correct Rogue Armor Profile for rogue/bard

## Load/Migration Validation

1. Load a character created before armor metadata changes.
2. Confirm equipment selections are normalized and preserved.
3. Confirm Armor Class and Rogue Armor Profile are recalculated on load.

## Weapons-Ready Follow-up

Before weapon targeting work, verify these are stable:

1. Armor AC changes only when armor inventory changes.
2. Economy updates do not regress armor/profile calculations.
3. Equipment selection metadata survives save/load/update flows.
4. No diagnostics errors in edited files.
