using UnityEditor;
using UnityEngine;

namespace Craftwar.EditorTools
{
    /// <summary>
    /// Ensures the shared material for the <c>Craftwar/UnitTeamColor</c>
    /// shader exists. Contains no Warcraft II data (just engine config), so
    /// unlike everything under Assets/GameData/Extracted this is a normal,
    /// committed asset — under Assets/Resources so UnitViewPool can
    /// Resources.Load it in a Player build without an AssetDatabase
    /// reference. Idempotent, same pattern as <see cref="ProjectBootstrap"/>'s
    /// "Ensure ..." menu items.
    /// </summary>
    public static class TeamColorMaterialSetup
    {
        public const string ResourcePath = "Materials/UnitTeamColor";
        public const string MaterialPath = "Assets/Resources/Materials/UnitTeamColor.mat";

        [MenuItem("Craftwar/Setup/Ensure Team Color Material")]
        public static Material EnsureMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null)
                return existing;

            var shader = Shader.Find("Craftwar/UnitTeamColor");
            if (shader == null)
            {
                Debug.LogError("[Craftwar] Shader 'Craftwar/UnitTeamColor' not found.");
                return null;
            }

            BakeUtil.EnsureFolder("Assets/Resources/Materials");
            var mat = new Material(shader) { name = "UnitTeamColor" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
            Debug.Log($"[Craftwar] Created {MaterialPath}");
            return mat;
        }
    }
}
