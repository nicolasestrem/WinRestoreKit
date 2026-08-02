using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;

namespace DataHelper
{
    internal class Data
    {
        internal static string RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        internal static string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        internal static string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        internal static string WindowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        // %USERPROFILE%. Added in Phase 3b for .ssh, which lives at the profile root rather than
        // under AppData. GetFolderPath rather than ExpandEnvironmentVariables to match every other
        // root here, and because the environment variable is user-writable.
        internal static string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Application.StartupPath has no trailing separator on .NET Framework but does on .NET 5+,
        // so plain concatenation would yield "...\\app\". Path.Combine normalizes both cases.
        // The trailing separator is part of this field's contract - callers concatenate onto it.
        //
        // AppContext.BaseDirectory replaced Application.StartupPath in Phase 4 PR 2, when this file
        // moved to WinRestoreKit.Core and lost its WinForms reference. This is the root path of every
        // backup the app has ever written, and the two do NOT necessarily agree under
        // PublishSingleFile with IncludeNativeLibrariesForSelfExtract - which is exactly how this
        // app ships and is a mode neither `dotnet build` nor `dotnet test` ever exercises. So it was
        // measured rather than reasoned about, on 2026-07-21, with a probe carrying identical csproj
        // properties published under the exact /release flags and run from an unrelated working
        // directory:
        //
        //   Application.StartupPath       = ...\publish\
        //   AppContext.BaseDirectory      = ...\publish\
        //   GetDirectoryName(ProcessPath) = ...\publish        (no trailing separator)
        //   composed DataRootDir, all three = ...\publish\app\
        //   SHIPPED expression below equals the StartupPath composition (ordinal) = True
        //
        // Environment.ProcessPath, NOT AppContext.BaseDirectory, and the difference only shows up in
        // a mode nothing here tests. Both measured identical above, and BaseDirectory was the first
        // choice for being non-nullable - but BaseDirectory means "where the app's content was
        // extracted to", which equals the exe directory only while IncludeAllContentForSelfExtract
        // is off. Turn that on for some unrelated content-file reason and BaseDirectory silently
        // becomes a temp directory: the build succeeds, every test passes because none of them
        // publishes single-file, and the release writes every backup under
        // %TEMP%\.net\WinRestoreKit\<hash>\app\ - where the next temp clean deletes them and History
        // shows an empty list, so the user believes they have backups they do not have. ProcessPath
        // is the path of the running executable and cannot move like that.
        //
        // The fallback exists because ProcessPath is documented as nullable, not because it is
        // expected: it is null only when the OS cannot report the process path at all.
        public static string DataRootDir = Path.Combine(
                                                Path.GetDirectoryName(Environment.ProcessPath) ??
                                                    AppContext.BaseDirectory,
                                                "app") +
                                            @"\";

        // winget. Same App Execution Alias directory Windows Terminal lives in.
        //
        // Deliberately winget.exe and not wt.exe: the app used to run winget THROUGH Windows
        // Terminal, and wt is a launcher that exits as soon as it has handed the command over, so
        // waiting on it told us nothing about winget. See Utils.RunWingetAsync for the measurement.
        public static string ShellWinget = LocalAppData +
                                        @"\Microsoft\WindowsApps\winget.exe";

        // Obtain current date and time
        internal static string Now = $"{DateTime.Now:G}";

        internal static string NowShort = $"{DateTime.Now:yyyy-MM-dd - HH.mm}";

        /// <summary>
        /// The HTTP User-Agent every GitHub request sends. api.github.com answers 403 to a request
        /// without one, so this is not decoration. One constant, because the update check and the
        /// stargazer count are two separate callers and a drifting pair is invisible until one of
        /// them starts returning 403 in the field.
        /// </summary>
        public const string UserAgent = "WinRestoreKit";

        public static class Uri
        {
            /// <summary>
            /// The person who maintains WinRestoreKit now. The About page's credit link points
            /// here; it deliberately no longer points at the original author, who does not
            /// maintain this project and should not field its bug reports.
            /// </summary>
            public const string URL_MAINTAINER = "https://github.com/nicolasestrem";

            /// <summary>
            /// Appcopier by Builtbybel, the project WinRestoreKit began as a fork of.
            /// Acknowledgement only. Nothing operational reads this: it is not the update source,
            /// not the download page, and not where issues go.
            /// </summary>
            public const string URL_ORIGINAL_PROJECT = "https://github.com/builtbybel/Appcopier";

            public const string URL_GITREPO = "https://github.com/nicolasestrem/WinRestoreKit";

            /// <summary>
            /// The raw AssemblyInfo.cs on main, string-parsed by <see cref="ParseLatestVersion"/>.
            /// The path is load-bearing: if this file ever moves in the repository, every deployed
            /// copy loses its update fallback silently, because the fetch simply 404s.
            /// </summary>
            public const string URL_ASSEMBLY = "https://raw.githubusercontent.com/nicolasestrem/WinRestoreKit/main/src/WinRestoreKit/Properties/AssemblyInfo.cs";

            /// <summary>
            /// Kept while the current icon is still in use: the licence it ships under requires
            /// the attribution, and the icon did not change during the rebrand.
            /// </summary>
            public const string URL_ICONATTRIBUTION = "https://www.flaticon.com/free-icon/backup_10426480";

            public const string URL_GITLATEST = URL_GITREPO + "/releases/latest";

            /// <summary>
            /// The GitHub Releases API for the newest published release. Primary source for the
            /// update check; URL_ASSEMBLY stays as the fallback.
            /// </summary>
            public const string URL_GITAPI_LATEST = "https://api.github.com/repos/nicolasestrem/WinRestoreKit/releases/latest";
        }

        /// <summary>
        /// Extracts the AssemblyFileVersion value out of the raw text of a Properties/AssemblyInfo.cs.
        /// </summary>
        /// <remarks>
        /// Pulled verbatim out of the update check so the parse can be unit tested without network
        /// I/O or MessageBoxes. Its one caller is <c>UpdateCheck.ReadLatestVersionAsync</c> in the
        /// WinRestoreKit assembly, named here in plain text rather than as a cref: Core is the lower
        /// layer and does not reference the application, so the symbol is not resolvable from here.
        /// The logic is inherited unchanged from Appcopier, quirks included: only lines containing
        /// the literal "[assembly: AssemblyFileVersion" are considered (so AssemblyVersion is
        /// correctly ignored), the LAST such line wins, no match yields an empty string, and
        /// malformed lines throw. WinRestoreKit starts at 0.0.1 and has no deployed clients of its
        /// own, so the quirks are no longer a compatibility obligation. They are kept because the
        /// behaviour is pinned by tests and the remote file format is the contract: this parser and
        /// the AssemblyInfo.cs it reads have to agree, and changing one side alone breaks the check
        /// silently, in the field, for everyone.
        /// </remarks>
        internal static string ParseLatestVersion(string assemblyInfoText)
        {
            var readVersion = assemblyInfoText.Split('\n');
            var infoVersion = readVersion.Where(t => t.Contains("[assembly: AssemblyFileVersion"));
            var latestVersion = "";
            foreach (var item in infoVersion)
            {
                latestVersion = item.Substring(item.IndexOf('(') + 2, item.LastIndexOf(')') - item.IndexOf('(') - 3);
            }

            return latestVersion;
        }

        /// <summary>
        /// The tag of the newest GitHub release, with one optional leading "v" stripped.
        /// </summary>
        /// <remarks>
        /// Mirrors <see cref="ParseLatestVersion"/>'s no-match contract: a missing or empty
        /// <c>tag_name</c> yields an empty string, which the caller renders as "could not read the
        /// latest version number" rather than comparing against and offering a phantom update.
        /// Malformed JSON throws (JsonReaderException) and the caller catches it to fall back to the
        /// AssemblyInfo path - a broken API response must not be reported as "no update".
        ///
        /// This repo tags bare x.y.z ("0.0.1" for WinRestoreKit; "0.12" and "0.30.0" inherited from
        /// Appcopier); the "v" strip is tolerance, pinned by test. Note that a two-part tag like the
        /// inherited "0.12" would never compare equal to a three-part version, so new tags must be
        /// three-part. See .claude/skills/release/SKILL.md.
        /// </remarks>
        internal static string ParseLatestReleaseTag(string json)
        {
            JObject root = JObject.Parse(json);

            string tag = (string)root["tag_name"];

            if (string.IsNullOrEmpty(tag))
                return "";

            return tag[0] == 'v' || tag[0] == 'V' ? tag.Substring(1) : tag;
        }

        /// <summary>
        /// One client for the whole process. HttpClient is designed to be shared; a new one per call
        /// leaks sockets in TIME_WAIT, which is the documented WebClient-replacement trap.
        /// </summary>
        private static readonly HttpClient InetProbe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // Check Inet
        public static bool IsInet()
        {
            try
            {
                using (HttpRequestMessage request =
                           new HttpRequestMessage(HttpMethod.Get, "http://clients3.google.com/generate_204"))
                using (InetProbe.Send(request))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}