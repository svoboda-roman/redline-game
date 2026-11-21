namespace Redline.Game.Render3D.Integration
{
    /// <summary>
    /// Hook for player-build-only adjustments of a freshly created 3D rig. Currently empty: the depth-of-field
    /// workaround that lived here is gone because the 3D URP asset (Resources/Proto3D_URP) is now the render pipeline
    /// of the non-default "Cinematic3D" quality level, so builds keep its resources and shader variants.
    /// </summary>
    public static class PlayerBuildFixes
    {
        /// <summary>Applies the build-only adjustments to a freshly created rig (none at present).</summary>
        public static void Apply(Pixel3DCamera rig)
        {
        }
    }
}
