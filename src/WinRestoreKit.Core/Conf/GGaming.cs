namespace Conf
{
    public class GGaming : MultiKeyRegistryModule
    {
        public GGaming()
        {
            Title = "Gaming settings";
            Info = "This will export settings related to Windows Game Bar DVR (Game Recorder).";

            Keys.Add(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\GameBar");
            Keys.Add(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR");
        }

        // True for both keys, because both are REMOVABLE without anything being wrong: GameBar and
        // GameDVR can be disabled by policy or stripped by debloat scripts, and an absent key then
        // means nothing is configured rather than that something broke. Both were probed on the
        // development machine and found PRESENT - the flag covers the machines where they are not,
        // it does not assert that absence is the common case.
        protected override bool AbsenceIsNormal(string key) => true;
    }
}
