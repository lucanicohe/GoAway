VISOR HUD  (Republic-Commando style, metal visor + compass)
====================================================

WHAT IT IS
  - top visor: solid metal band with 16 vents that follow the curve of the visor, a metal edge line,
    and a COMPASS on the dashed line:  N  E  S  W  slide along it while you turn
  - left and right visor arcs with scale ticks (transparent, so the game shows through)
  - left and right bottom corner panels: metal, vents lined up with the slanted edge line, metal edge line
  - crosshair ring in the centre
  - two striped bars attached to the inner edge of the bottom panels:  LEFT = Health (green)    RIGHT = Ammo (red)
    They drain from the top downwards, like a glass emptying, and have a HEALTH / AMMO text label underneath.
  No numbers, no icons, no grenade symbols.

SETUP (1 minute)
1. Import the package. Wait until the Console has no red errors.
2. A dialog appears. First import -> "Build test scene + prefab".  Update of an older version -> "Rebuild prefab".
   Any time from the menu bar:  Tools > Visor HUD
     Build Test Scene ....... Assets/VisorHud/Scenes/VisorHud_Test.unity  (floor, boxes + the HUD; it also adds your
                              first-person player rig automatically if the FirstPersonCamera package is in the project)
     Create HUD Prefab ...... Assets/VisorHud/Prefabs/VisorHUD.prefab   (drag it into ANY scene)
     Add HUD To Open Scene .. puts the HUD into the scene you have open
3. Press PLAY. Click "Visor HUD" in the Hierarchy and drag the sliders in the Inspector:
     Health  -> left green bar          Ammo -> right red bar          (0 = empty, 1 = full, live while playing)

INSPECTOR SETTINGS (Visor HUD component)
  Bar look .................... Health / Ammo colour, Health / Ammo Bar Opacity, Empty Bar Opacity, Smooth Time
  HUD parts ................... tick to show or hide: Top Visor (metal only), Compass, Side Arcs, Bottom Panels, Bars, Crosshair
  Hud Opacity ................. master opacity of everything
  Visor metal ................. Top Visor Metal / Edge Line, Left + Right Panel Metal / Edge Line
                                each one has its own Color (tints the metal) and its own Opacity
  White HUD elements .......... Left Arc, Right Arc, Compass Line (dashes), Compass Ticks, Compass Letters
                                each one has its own Color and its own Opacity
  Crosshair ................... Color, Opacity, Size
  Compass (top line) .......... Compass Target (empty = Main Camera), North Offset, Compass Visible Degrees, Letter Size,
                                Use Test Heading + Test Heading (fake a heading to preview the letters without moving)
  Labels ...................... Show Labels, Label Color, Health / Ammo Label Opacity, Label Size

COMPASS
  The letters follow the Main Camera by default (works with the Cinemachine first-person rig). To follow something else
  (e.g. the player body) drag it into "Compass Target".  North is the world +Z direction; if your North is somewhere else,
  set "North Offset" (rotation around Y in degrees).  Turning right moves the letters to the left, like a real compass tape:
  looking north you see N in the middle with W on the left and E on the right.

FROM YOUR OWN SCRIPTS
  VisorHud hud = FindFirstObjectByType<VisorHud>();   // or drag a reference
  hud.SetHealth(0.5f);      hud.SetAmmo(0.25f);       // values 0 - 1
  float bearing = hud.Heading;                         // 0 = N, 90 = E, 180 = S, 270 = W

WORKS WITH THE FIRST-PERSON PLAYER
  The HUD is a Screen Space - Overlay canvas, so it is independent of the camera: it works with the Cinemachine first-person rig
  (FirstPersonRig prefab) without any setup. It never blocks mouse clicks.

NOTES
  - The art is made of small PNG sprites in Assets/VisorHud/Textures (the visor metal, edge lines, compass dashes and ticks are
    separate layers so each can have its own colour and opacity).
  - The canvas scales with the screen (reference 1920x1080).
  - Swap any picture by replacing the PNG with your own of the same name (keep the transparent background).

UPDATING FROM AN OLDER VERSION
  After importing, accept "Rebuild prefab" in the dialog (or run Tools > Visor HUD > Create HUD Prefab once) so the prefab gets
  the new layers. If a scene still shows the old HUD, delete the HUD object in that scene and drag in the new prefab.
  To rename the labels, edit the Text on the 'Label' child of 'Health Bar' / 'Ammo Bar'.

TROUBLESHOOTING
  HUD shows plain white blocks / Console says "sprite not found":
    The PNGs were imported as normal textures instead of Sprites. Run  Tools > Visor HUD > Fix Texture Import,
    then  Tools > Visor HUD > Build Test Scene  again (this rebuilds the prefab with the sprites).
    (Build Test Scene / Create HUD Prefab now repair the textures automatically as well.)
  Compass letters do not move:
    "Use Test Heading" is ticked, or there is no camera tagged MainCamera. Untick it / assign a Compass Target.
  "Objects are trying to be loaded during a domain backup ... GraphObject:OnBeforeSerialize":
    Comes from Unity's Shader Graph package during a script reload. It is harmless and not caused by this package.
