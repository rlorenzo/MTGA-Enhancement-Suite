namespace MTGAEnhancementSuite
{
    /// <summary>
    /// All reflected type, method, and field names in one place.
    /// When MTGA patches break something, fix the strings here.
    /// </summary>
    internal static class GameRefs
    {
        // NavBarController fields
        public const string HomeButton = "HomeButton";
        public const string AchievementsButton = "AchievementsButton";

        // Animator parameter hashes
        public const string AnimParamSelected = "Selected";

        // Scene names
        public const string NavBarScene = "NavBar";

        // Our injected objects
        public const string EnhancementSuiteTabName = "EnhancementSuiteTab";

        // Challenge system types (for reflection-based patching)
        public const string PVPChallengeController = "Core.Meta.MainNavigation.Challenge.PVPChallengeController";
        public const string PVPChallengeData = "SharedClientCore.SharedClientCore.Code.PVPChallenge.Models.PVPChallengeData";

        // Challenge UI
        public const string FormatSpinnerName = "MTGAES_FormatSpinner";

        // ---- Local decks: deck-builder save-intercept (most patch-fragile) ----
        // WrapperDeckBuilder is MTGA's deck-builder scene controller. These
        // private members are reflected by DeckBuilderSavePatch / LocalDeckEditor.
        // If a future MTGA patch renames any of them, fix here.
        public const string WrapperDeckBuilder_SaveCoroutine = "Coroutine_SaveDeck";
        public const string WrapperDeckBuilder_IsSavingField = "_isSavingDeck";
        public const string WrapperDeckBuilder_SaveSuccessField = "_isDeckSaveSuccess";
        public const string DeckManager_EditGoToBuilder = "Edit_GoToDeckBuilder";
    }
}
