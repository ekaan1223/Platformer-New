using UnityEngine;

/// <summary>
/// Paints models with a key's colour WITHOUT creating new materials: it uses a MaterialPropertyBlock
/// (a per-object colour override). So 10 keys can share one material and still be 10 different colours.
/// Works with the URP/HDRP Lit shader (_BaseColor) and the Built-in Standard shader (_Color).
/// If your Blender model already has the right colours, tick off "Tint With Key Color" on the pickup / door.
/// </summary>
public static class KeyColorTint
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>Tints the chosen renderers; if none are chosen, every model renderer under 'root'.</summary>
    public static void Apply(Renderer[] chosen, GameObject root, Color color)
    {
        Renderer[] targets = chosen != null && chosen.Length > 0 ? chosen : root.GetComponentsInChildren<Renderer>();
        MaterialPropertyBlock block = new MaterialPropertyBlock();

        foreach (Renderer renderer in targets)
        {
            // Only real models: skip particle systems, trails and line renderers.
            if (renderer == null || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;

            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }
    }
}
