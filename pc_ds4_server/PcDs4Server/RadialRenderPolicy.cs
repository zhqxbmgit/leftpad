namespace PcDs4Server;

internal enum RadialRenderPolicy
{
    Legacy,
    UniversalInitial
}

internal static class RadialRenderPolicyAuthority
{
    public static RadialRenderPolicy ProductionDefault => RadialRenderPolicy.UniversalInitial;

    public static RuntimeRenderBundleTargetBuilder CreateBundleBuilder(
        RadialRenderPolicy policy,
        string? selectedEmphasisDiscoveryRoot = null)
    {
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        return (plan, settings, targetSize, dpi) => Build(
            policy,
            plan,
            settings,
            targetSize,
            dpi,
            selectedEmphasisDiscoveryRoot);
    }

    public static RuntimeRenderBundle Build(
        RadialRenderPolicy policy,
        NormalizedRenderPlan plan,
        RadialMenuSettings settings,
        int legacyTargetSize,
        int dpi,
        string? selectedEmphasisDiscoveryRoot = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        return policy switch
        {
            RadialRenderPolicy.Legacy => RuntimeRenderBundle.Build(
                plan,
                settings,
                legacyTargetSize,
                dpi),
            RadialRenderPolicy.UniversalInitial => RuntimeRenderBundle.BuildUniversal(
                UniversalRadialRenderPlan.Create(
                    plan,
                    ProductionUniversalRadialParametersAdapter.Adapt(settings, plan)),
                settings,
                dpi,
                selectedEmphasisDiscoveryRoot),
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };
    }
}
