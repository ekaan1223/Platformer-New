using UnityEngine; // gives us access to Unity classes (Renderer, MaterialPropertyBlock, Shader, etc.)

// KEY COLOR TINT
// Paints models with a key's colour WITHOUT creating new materials: it uses a MaterialPropertyBlock (a per-object
// colour override). So 10 keys can share one material and still be 10 different colours.
// Works with the URP Lit shader (_BaseColor) and the Built-in Standard shader (_Color).
// If your Blender model already has the right colours, untick "Tint With Key Color" on the pickup / door.
// "static" = a toolbox: KeyPickup and LockedDoor call KeyColorTint.Apply, no object needed.

public static class KeyColorTint
{
    private static int BaseColorId = Shader.PropertyToID("_BaseColor"); // shader property names turned into numbers once
    private static int ColorId = Shader.PropertyToID("_Color");

    // Tints the chosen renderers; if none are chosen, every model renderer under 'root'.
    public static void Apply(Renderer[] chosen, GameObject root, Color color)
    {
        Renderer[] targets = chosen;                                                    // the ones dragged in...
        if (targets == null || targets.Length == 0) targets = root.GetComponentsInChildren<Renderer>(); // ...or all of them
        MaterialPropertyBlock block = new MaterialPropertyBlock();

        for (int i = 0; i < targets.Length; i++)
        {
            Renderer renderer = targets[i];

            // Only real models: skip particle systems, trails and line renderers.
            if (renderer == null) continue;
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;

            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }
    }
}
