JITTER-FREE FIRST-PERSON CAMERA (Cinemachine 3)
================================================

REQUIRES: Cinemachine 3.x  (Window > Package Manager > Unity Registry > Cinemachine, version 3.0 or newer).
The menu finds the Cinemachine components by name, so it always compiles; if Cinemachine 3 is missing it tells you to install it.

QUICK START
1. Import the package, wait for the Console to have no red errors.
2. A dialog appears -> "Build demo scene + prefab".  Or any time, from the menu bar  Tools > First Person Camera:
     Build Demo Scene ........ makes Assets/FirstPersonCamera/Scenes/FirstPerson_Demo.unity (floor, boxes, ramp, pillar + the rig)
                               and also creates the prefab
     Create Prefab ........... makes Assets/FirstPersonCamera/Prefabs/FirstPersonRig.prefab  (drag it into ANY scene;
                               it has its own Main Camera + Cinemachine Brain, so delete the scene's old Main Camera)
     Add Rig To Open Scene ... builds the rig directly in the scene you have open (uses your existing Main Camera)
     Test Camera Shake ....... in Play mode (or just press G)
3. Press PLAY:  WASD move, Shift sprint, Space jump, mouse look, G test camera shake, Esc releases the mouse.

WHY IT WAS JITTERY, AND WHAT THIS DOES (the steps from the video)
The problem: movement runs in FixedUpdate (physics, fixed rate) while mouse look runs in Update (frame rate).
They drift out of sync, so the view stutters. Interpolating or LateUpdate hacks add input delay.
The fix is Cinemachine 3, which handles frame-rate-dependent mouse input and syncs to the target. The menu builds this:

  1. A Cinemachine Camera  ("CM FirstPerson Camera"), a separate object (NOT a child of the player)
  2. An empty game object "Head" (child of Player, at eye height) = the camera's Tracking Target
  3. Position Control = Hard Lock To Target   (camera sticks to the head, no lag)
  4. Rotation Control = Pan Tilt              (mouse look)
  5. Input Axis Controller (= "Add Input Controller"), Look axis Acceleration Time = 0 and Deceleration Time = 0
     (no input smoothing -> no sluggish / delayed feel)
  + The Main Camera has a Cinemachine Brain.
  + The Player is a Rigidbody (Interpolate ON, rotation frozen) moved in FixedUpdate by FirstPersonMotor.

DOING IT BY HAND ON YOUR OWN PLAYER
  - Add an empty child "Head" to your player at eye height.
  - GameObject > Cinemachine > Cinemachine Camera.  Drag Head into "Tracking Target".
  - Position Control = Hard Lock To Target.   Rotation Control = Pan Tilt.
  - Click "Add Input Controller" (Input Axis Controller). Set Look axis Acceleration Time and Deceleration Time to 0.
  - Rigidbody > Interpolate = Interpolate. Make sure your player script moves the Rigidbody in FixedUpdate and does NOT rotate the camera.

POLISH (already added by the menu)
  Noise (subtle handheld / head-bob feel):  Basic Multi Channel Perlin on the Cinemachine camera.
      Noise Profile = Handheld_normal_mild, Amplitude Gain 0.25 (set to 0 to switch it off, raise for stronger).
  Camera shake (explosions, landings):  Impulse Listener on the Cinemachine camera + Impulse Source on "Camera Shake Source".
      The listener's Secondary Noise is "6D Shake", so shakes are never repetitive.
      From code:    FPCam.FirstPersonShake.Instance.Shake(1.5f);     (bigger number = bigger shake)
      Or on a custom source:  GetComponent<CinemachineImpulseSource>().GenerateImpulse();
      Test it: press G in Play mode, or right-click the FirstPersonShake component > Test Shake.

TWEAKING
  Movement:      Player > First Person Motor  (walk / sprint speed, acceleration, jump height)
  Look speed:    CM FirstPerson Camera > Input Axis Controller > Look axes > Gain  (negative flips the axis)
  Up/down limit: CM FirstPerson Camera > Pan Tilt > Tilt Axis > Range
  Field of view: CM FirstPerson Camera > Lens > Field Of View (75 by default)
  Input:         works with the NEW Input System (FPInputSystemProvider) and the OLD Input Manager (FPLegacyInputProvider).

TROUBLESHOOTING
  - No "Tools > First Person Camera" menu: look in the Console for red errors (a compile error somewhere in the project hides all menus).
  - A dialog saying Cinemachine 3 was not found: install it from the Package Manager (version 3.x, not 2.x).
  - Console warning "could not find the Look axis Acceleration/Deceleration Time": select CM FirstPerson Camera >
    Input Axis Controller and set both to 0 by hand.
  - Mouse doesn't look around: click in the Game view (the cursor is locked on Play); check Active Input Handling in
    Project Settings > Player.
