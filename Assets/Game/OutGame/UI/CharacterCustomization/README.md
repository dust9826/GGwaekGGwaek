# Character Customization

`CharacterCustomization.unity` is the first-build scene for choosing a Creative Characters outfit before entering `MainMenu`.

- Runtime UI: UI Toolkit at a 1600×900 reference scale through `StartScreenPanelSettings.asset`.
- Categories: Body, Face, Hair, Top, Coat, Pants, Shoes, Hat.
- The preview swaps `SkinnedMeshRenderer.sharedMesh` on the pack's `Base_Mesh` rig, so every selection keeps one skeleton and Animator.
- Hat, Hair and Coat include a `NONE` choice. The preview can be rotated by dragging or with the arrow controls.
- `READY FOR DUTY` stores indices in `PlayerPrefs` under `PPack.CreativeCharacter.*`, then opens `MainMenu`.
- `SKIP` opens `MainMenu` without overwriting the saved combination.
- Runtime uses no raster UI texture. All panels and decorations are UXML/USS shapes, so scaling remains sharp.

Use `PPack > OutGame > Rebuild Character Customization Scene` after changing the catalog or scene layout. The builder reads the imported Creative Characters prefabs and regenerates the scene without editing Unity YAML by hand.
