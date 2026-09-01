# Penguin Chaos Playground

`Penguin_ChaosPlayground_Test.unity` is a physics-only test scene for the deliberately clumsy penguin controller. It does not replace the production `CharacterController` locomotion prefab and is not included in Build Settings.

## Controls

- `WASD`: camera-relative wobble walk
- `Space`: jump plus two mid-air jumps
- `Left Shift`: belly slide
- `Q`: throw the body into a temporary flop; press again to recover
- `Left Mouse`: headbutt and launch dynamic props
- `Right Mouse + WASD`: air-trick torque
- `R`: reset to the starting point

## Design notes

The player keeps directional control, but movement force is applied above the center of mass and a damped pose spring only partially corrects the body. Sliding and flopping relax that correction so collisions can create unscripted comedy. The independent follow camera keeps a stable horizon while the penguin rolls.

Reference direction:

- Goat Simulator 3 official feature page: https://www.goatsimulator3.com/
- Coffee Stain's Goat Simulator 3 overview on PlayStation Blog: https://blog.playstation.com/?p=368366
- Unity `Rigidbody.AddForce`: https://docs.unity3d.com/ScriptReference/Rigidbody.AddForce.html
- Unity Rigidbody interpolation: https://docs.unity3d.com/ScriptReference/Rigidbody-interpolation.html

