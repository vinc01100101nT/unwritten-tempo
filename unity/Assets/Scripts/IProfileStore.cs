/// <summary>
/// The storage seam for the player's save. Today the only implementation is
/// <see cref="LocalJsonProfileStore"/> (a JSON file on disk via <see cref="SaveSystem"/>); a future
/// authoritative server backend implements this same interface, so going online is a one-line swap of
/// <see cref="ProfileStore.Current"/> with zero gameplay changes. <see cref="SaveManager"/> talks only
/// to this interface — never to a concrete store — which is what keeps "local now, server later" a swap
/// rather than a rewrite.
/// </summary>
public interface IProfileStore
{
    bool Exists();
    SaveData Read();
    void Write(SaveData data);
    void Delete();
}

/// <summary>Local-disk implementation: delegates to <see cref="SaveSystem"/>'s JSON file IO.</summary>
public sealed class LocalJsonProfileStore : IProfileStore
{
    public bool Exists()          => SaveSystem.Exists();
    public SaveData Read()        => SaveSystem.Read();
    public void Write(SaveData d) => SaveSystem.Write(d);
    public void Delete()          => SaveSystem.Delete();
}

/// <summary>
/// Holds the active profile store (single source of truth for where saves go). Defaults to local disk;
/// assign a server-backed <see cref="IProfileStore"/> here at startup to move the source of truth online.
/// </summary>
public static class ProfileStore
{
    public static IProfileStore Current = new LocalJsonProfileStore();
}
