MONSTER - UNITY PROTOTYPE (rig + bear-style animation)
=======================================================

WHAT YOU GET
- ONE skinned mesh (Monster.fbx), clean 22-bone skeleton, 2,799 polygons, no texture (plain dark carapace material).
- Two checkboxes in the Inspector (Monster Controller component):
      [ ] Run On All Fours     -> bear-style gallop
      [ ] Attack On Two Legs   -> rears up on its hind legs, roars, then alternating claw swipes
  Neither ticked = idles on all fours (breathing + head sway).
  Both ticked    = attack wins (it stands up). Untick attack and it drops back onto all fours.

SETUP (2 minutes)
1. Import the package (double-click the .unitypackage, or Assets > Import Package > Custom Package) -> Import.
2. Wait until the Console has no red errors. A dialog asks "Build the demo scene now?" -> Build Demo Scene.
   (If it doesn't appear: menu  Tools > Monster > Build Demo Scene.)
3. Press PLAY.
4. Click "Monster" in the Hierarchy and tick the checkboxes in the Inspector.

USING IT IN YOUR OWN SCENE
- Drag Assets/Monster/Prefabs/Monster.prefab into your scene (it already has the Monster Controller).
- From your own scripts: GetComponent<MonsterController>().SetRunning(true) / .SetAttacking(true)
- "Move While Running" makes it travel; "Circle Radius" makes it run in a circle instead of a straight line.
- The floor is the monster's own Y position. "Keep Above Ground" stops paws/feet sinking below it.

HOW THE ANIMATION WAS DESIGNED (real bear behaviour)
- Bears are plantigrade quadrupeds: they walk, "running walk", canter, then gallop (a rotary bound).
  The gallop here: hind pair drives (right then left), forelegs reach (left then right), spine flexes, head stays low.
  Paws are placed on the floor with IK: each paw plants flat and slides back, then lifts and swings forward
  (hind paws land under/behind the hips). With Move While Running ticked the ground speed matches the stride (no sliding).
- Bears rear up onto the hind legs, hold with the head/neck raised, and strike with alternating forepaw swats.
  The attack here: rise (1.1 s) -> roar (chest out, arms wide) -> left / right / left / right paw swipes -> repeat.
  Only the front arms attack; the head and neck stay still.
- Tune speeds in the Inspector (Strides Per Second, Rise Seconds, Swipe Seconds ...).

NOTES
- The monster is large (about 5.8 m tall when upright, 4 m wide) - it is the size of the source file.
- Rig is rotation-only and world-aligned, so it works in Unity with no extra setup (no Animator, no clips).
- Works with Built-in and URP (HDRP: material is best-effort).
