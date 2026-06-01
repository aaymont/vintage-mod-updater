namespace VintageModUpdater.Core;

public sealed record ModDbModReference(
    int AssetId,
    IReadOnlyDictionary<string, string> ReleaseGameVersionsByModVersion);
