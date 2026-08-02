using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// WinRestoreKit is a Windows-only WinForms app targeting net8.0-windows. The .NET SDK normally
// emits this attribute automatically, but it does so only when GenerateAssemblyInfo is true - and
// that property MUST stay false here so this file survives verbatim as the update checker's raw
// fallback source (see WinRestoreKit.csproj). Without the attribute the CA1416 analyzer treats
// every Win32/WinForms call site as cross-platform and emits ~250 false warnings per build.
// Declaring it here restores the correct platform metadata and keeps CA1416 useful for genuine
// portability mistakes.
[assembly: SupportedOSPlatform("windows7.0")]

// Allgemeine Informationen über eine Assembly werden über die folgenden
// Attribute gesteuert. Ändern Sie diese Attributwerte, um die Informationen zu ändern,
// die einer Assembly zugeordnet sind.
[assembly: AssemblyTitle("WinRestoreKit")]
[assembly: AssemblyDescription("Back up, copy and restore Windows settings locally.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Nicolas Estrem")]
[assembly: AssemblyProduct("WinRestoreKit")]
[assembly: AssemblyCopyright("Original portions © 2023 Builtbybel; modifications © 2026 Nicolas Estrem")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Durch Festlegen von ComVisible auf FALSE werden die Typen in dieser Assembly
// für COM-Komponenten unsichtbar.  Wenn Sie auf einen Typ in dieser Assembly von
// COM aus zugreifen müssen, sollten Sie das ComVisible-Attribut für diesen Typ auf "True" festlegen.
[assembly: ComVisible(false)]

// Die folgende GUID bestimmt die ID der Typbibliothek, wenn dieses Projekt für COM verfügbar gemacht wird
[assembly: Guid("726aaf09-c2d4-4c5c-a61f-3dadae0b5cba")]

// Versionsinformationen für eine Assembly bestehen aus den folgenden vier Werten:
//
//      Hauptversion
//      Nebenversion
//      Buildnummer
//      Revision
//
// Sie können alle Werte angeben oder Standardwerte für die Build- und Revisionsnummern verwenden,
// indem Sie "*" wie unten gezeigt eingeben:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("0.0.1")]
[assembly: AssemblyFileVersion("0.0.1")]

// Most types in this assembly are internal. WinRestoreKit.Tests needs access to exercise the pure
// logic (e.g. Data.ParseLatestVersion, Program.GetCurrentVersionTostring) without going through
// the UI. Appended below the version attributes on purpose: when the GitHub Releases API call
// fails, the update checker falls back to downloading this file as raw text from
// nicolasestrem/WinRestoreKit and string-parses the AssemblyFileVersion line above, so that line's
// exact three-part format and the lines preceding it must never be disturbed.
[assembly: InternalsVisibleTo("WinRestoreKit.Tests")]
