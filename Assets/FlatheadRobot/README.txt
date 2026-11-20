FLATHEAD ROBOT - Unity prototype (rig + procedural animation)
=============================================================
ONE skinned mesh (Flathead.fbx), 21-bone world-aligned skeleton, 5 flat materials (colours from the Blender file).

SETUP
1. Import the package, wait for the Console to have no red errors.
2. Dialog -> "Build test scene + prefab" (or Tools > Flathead Robot > Build Test Scene / Build Prefab).
3. Press PLAY, click "FlatheadRobot" in the Hierarchy. Flathead Controller checkboxes:
     [x] Walking        walks in ANY direction: set Walk Direction 0-360 (0 forward, 90 right, 180 back, 270 left).
                        The robot never has to turn: the feet just step toward the direction of travel (spider-style trot gait).
                        "Move While Walking" also moves the robot (feet matched to speed, no sliding).
     [x] Aim At Target  turret pans (360) and tilts to point the gun at the Target (move AimTarget in the Scene view,
                        or tick Orbit Target to watch it track).
     [x] Jump           crouch, launch, legs tuck, land - repeats while ticked.
   All three can be ticked together. Everything is tunable in the Inspector (stride, step height, turret speed, jump height...).

PREFAB: Assets/FlatheadRobot/Prefabs/FlatheadRobot.prefab    TEST SCENE: Assets/FlatheadRobot/Scenes/Flathead_Test.unity
SCRIPTS: GetComponent<FlatheadRobotKit.FlatheadController>().SetWalking(true) / SetAiming(true) / SetJump(true)
The muzzle tip bone is "Gun_Muzzle" (put a muzzle flash / projectile spawn there).
