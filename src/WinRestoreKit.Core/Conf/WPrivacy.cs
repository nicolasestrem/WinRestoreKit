using WinRestoreKit;

namespace Conf
{
    public class WPrivacy : RegistryModule
    {
        protected override string Key => @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Privacy";

        // Unverified judgement: expected on mainstream Win11, plausibly absent on LTSC or debloated images.
        protected override bool AbsenceIsNormal => true;

        public WPrivacy()
        {
            Title = "Privacy";
            Info = "This will export settings related to Privacy and Tailored experiences/Windows diagnostic which offers you personalized tips, ads, and recommendations to enhance Microsoft experiences.";
        }
    }
}
