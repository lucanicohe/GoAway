IG-88 DROID - UNITY PROTOTYPE
==============================

WHAT YOU GET
- ONE mesh (IG88.fbx) with a clean 38-bone skeleton, ~6.5k triangles, original UVs/texture look.
- TWO materials: IG88_Metal (scratched armor/brass/rubber atlas: albedo + normal + metal/smoothness)
                 IG88_Glow  (red emissive lens parts)
- Walking checkbox, Aim-At-Target checkbox, Target slot - all in the Inspector.

SETUP (2 minutes)
1. Import the package (double-click the .unitypackage, or Assets > Import Package > Custom Package) -> Import.
2. A dialog asks "Build the demo scene now?" -> click Build Demo Scene.
   (If it doesn't appear: menu  Tools > IG88 > Build Demo Scene.)
   The builder auto-detects Built-in / URP / HDRP and makes the correct materials.
3. Press PLAY.
4. In the Hierarchy click "IG88". In the Inspector (IG88 Controller):
      [x] Walking          -> droid walks
      [x] Aim At Target    -> upper body, head and gun arm follow the Target
      Target               -> the AimTarget sphere (already assigned)
5. Click "AimTarget" in the Hierarchy and drag it with the Move tool (W) in the Scene view
   while the game is running. The droid follows it.

USING THE DROID IN YOUR OWN SCENE
- Drag Assets/IG88/Prefabs/IG88_Droid.prefab into the scene.
- Make an empty GameObject (or any object) as the target, drag it into the "Target" slot.
- Tick / untick Walking and Aim At Target (or call SetWalking(bool) / SetAiming(bool) from your own scripts).
- "Move While Walking" makes the droid actually travel forward instead of walking in place.

NOTES
- The droid faces +Z. Gun arm = its right arm.
- Walking + aiming are done in code (IG88Controller.cs) on the FBX bones: no Animator needed.
- The metal map is deliberately not 100% metallic (65%) so the droid never renders pitch black
  in a plain scene. Tweak IG88_MetalSmooth.png if you want more shine.
- For a stronger red glow add Bloom in your post-processing volume (URP/HDRP).
