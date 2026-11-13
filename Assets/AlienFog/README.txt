ALIEN LOW FOG  -  cheap, good-looking ground fog for Unity (Built-in + URP)
===========================================================================

LOOKS LIKE: the thick, slow, cold smoke crawling along the floor in the Alien egg chamber.

WHY IT IS CHEAP
- ~25-130 big soft smoke sprites in 2 layers (flat sheets on the floor + small puffs for height).
- ONE 512x512 texture (about 170 KB in memory), ONE material, no render textures, no volumetrics, no post-processing.
- Quality dropdown: Low / Medium / High (changes how many sprites are used).
- The shader does the "good looking" work for free: fades by height (no flat-plane look), colours by height,
  lets nearby lights make the fog glow, and fades when the camera is close.

SETUP (1 minute)
1. Import the package. Wait for the Console to have no red errors.
2. A dialog appears: "Add fog to current scene" or "Build demo scene". (Or any time:)
     GameObject > Effects > Alien Low Fog          (adds fog to your open scene)
     Tools > Alien Fog > Build Demo Scene          (dark floor, pillars, coloured lights, fog)
3. The fog sits at the Y position of the "Alien Low Fog" object  ->  put that object on your floor.
   Scale the area with  Size  (X and Z, in metres).  Press PLAY (in edit mode it is pre-simulated so you can place it).

TWEAK IT (select the "Alien Low Fog" object)
   Density, Height, Colour Low / High, Puff Size ... all apply live.
   Swirl / Wind Angle / Wind Speed ....... how it moves (keep it slow for the Alien feel)
   Light Glow ............................ fog near point/spot lights picks up their colour (auto uses the 4 closest;
                                           or drag your own lights into "Lights"). Great with coloured lasers/lamps.
   Soft Particles ........................ smoother where smoke touches walls/props. In URP first run:
                                           Tools > Alien Fog > Enable URP Depth Texture
   Right-click the component header for presets:  Alien Egg Chamber / Light Mist / Heavy Smoke

TIPS FOR THE ALIEN LOOK
- Keep the scene dark; use a low, coloured point light or two (teal / green / amber) near the floor.
- Colour Low = darker cold grey-teal, Colour High = lighter. Density 0.7-1.0, Height about 1.0-1.4 m.
- A thin Fog height + slow wind reads as "heavy", cold air. Add a faint blue ambient light.

IF THE FOG LOOKS LIKE PLAIN SQUARES
- That means the smoke texture was not reaching the material. This version sets the texture on the fog directly
  (field "Smoke Texture") and falls back to a texture generated in code, so it should not happen.
- For fog objects made with an older version: Tools > Alien Fog > Repair Fog In Open Scene.

NOTES
- Not for HDRP (HDRP already has proper volumetric fog: add a Local Volumetric Fog volume instead).
- If the fog is invisible: check the Console for shader errors, and that "Alien Low Fog" > Material is assigned
  (Assets/AlienFog/Materials/AlienLowFog.mat). Re-run Tools > Alien Fog > Add Low Fog To Scene to re-create it.
