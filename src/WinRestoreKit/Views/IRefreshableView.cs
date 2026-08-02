namespace Views
{
    /// <summary>
    /// Why a view is being shown.
    /// </summary>
    /// <remarks>
    /// A view that carries state across visits needs to tell "the user came back to what they were
    /// doing" from "the user started this again". Returning from About should not lose the backup
    /// they had picked; entering Restore afresh should not silently pre-pick the one they chose the
    /// last time, because a folder selected on a previous journey is not a choice made for this one.
    /// </remarks>
    internal enum ViewEntry
    {
        /// <summary>Navigated to as a new destination - a rail entry, or a step deeper.</summary>
        Fresh,

        /// <summary>Returned to by going back, with the previous context still meant to apply.</summary>
        Back
    }

    /// <summary>
    /// A view that reads its content from disk and must re-read it every time it is shown.
    /// </summary>
    /// <remarks>
    /// Home summarises the backup folders and their manifests. Those change while the app is running -
    /// running a backup is the single most likely thing a user does between two visits to Home - so a
    /// view built once at construction would show a stale answer to the one question the screen exists
    /// to answer.
    ///
    /// An interface rather than NavigationService knowing about HomePageView: navigation should not
    /// grow a list of view types it has to special-case as more screens land in PRs 6-8.
    /// </remarks>
    internal interface IRefreshableView
    {
        void RefreshView(ViewEntry entry);
    }
}
