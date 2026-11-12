THE REALITY DEFENDER
AR target shooter | Unity 6.3 LTS, URP, AR Foundation + ARKit | Tested on iPhone

HOW IT WORKS
Scan the floor, tap to place a glowing base, then shoot the three energy targets floating
above it. Destroy all three (score 30) to clear the wave.

KEY DECISIONS

1. Clear code structure
   The game is split into three managers: GameManager (game flow, score, shooting),
   ARManager (everything AR) and UIManager (everything on screen). Each prefab has its own
   small script. The game moves through three simple states: Scanning, Playing, Wave Cleared.

2. Reliable AR placement
   A reticle shows exactly where the base will land. Once placed, the base is anchored to the
   real floor so it stays in place as you walk around it. "Move Base" lets the player
   reposition it without losing the targets or score.

3. Guided experience
   On-screen prompts guide the player step by step (scan, aim, tap) and warn about tracking
   problems such as low light or moving too fast. The scene lighting adjusts to match the room.

4. Smooth performance
   Projectiles are reused instead of created and destroyed on every shot, which avoids
   stutters on mobile. Collision detection is tuned so fast shots never pass through targets.
   The game runs at a steady 60 fps.

5. Custom shaders
   Built in Shader Graph. The targets combine flowing energy, a glowing rim and a gentle
   wobble. The base has pulsing rings and a glowing edge. Only these effects glow; the camera
   image is left untouched.
