UNITY URP AMMO SUPPLY
1. Copy this folder into Assets. Remove the old R reload LateUpdate from GameManager.
2. Create an empty GameObject at floor level, scale (1,1,1), named Ammo Supply. Add AmmoSupply.cs.
3. Assign AmmoSupply.shader and AmmoIcon.shader to the matching shader fields (important for builds). Manager auto-finds if empty.
4. Optional: create a dedicated legacy UI Text on the HUD, initially empty, and assign Prompt Text. Do not reuse MissionUI's interact text. For multiple stations, use separate prompt texts or leave unassigned. Assign reload sound if desired.
5. Make a prefab and place copies at floor level. Radius defaults to 1 metre. Enter and press R when ammo is below maximum. Station fades and is destroyed. Full ammo does not consume it.
Uses GameManager.Player, PlayerAlive, CurrentAmmo, MaxAmmo and SetAmmo. No enemy changes. Assumes upright, unit-scale station and player pivot within 2m vertically. Not tested in Unity.
