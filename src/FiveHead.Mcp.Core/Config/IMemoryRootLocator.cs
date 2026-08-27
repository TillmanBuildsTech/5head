namespace FiveHead.Mcp.Core.Config;

/// <summary>
/// Locates the active <c>.agent-memory/</c> directory by walking up from a starting path.
/// Prefers repo-local memory roots and only falls back to a user-level global path when
/// no repo boundary or existing memory root can be found.
/// </summary>
public interface IMemoryRootLocator
{
    /// <summary>
    /// Walk up from <paramref name="startingPath"/> until <c>.agent-memory/</c> or
    /// <c>.git/</c> is found. Returns the repo-local <c>.agent-memory/</c> path when a
    /// repo boundary is discovered, and falls back to <c>~/.agent-memory/global/</c>
    /// only when no repo-local root can be inferred.
    /// </summary>
    string Locate(string startingPath);
}
