using DataHelper;
using System.IO;

namespace Conf
{
    /// <summary>
    /// The SSH client's host configuration: <c>.ssh\config</c> and <c>.ssh\known_hosts</c>.
    /// </summary>
    /// <remarks>
    /// PRIVATE KEYS ARE DELIBERATELY NOT BACKED UP (user decision, 2026-07-21). WinRestoreKit writes
    /// its backups as ordinary unencrypted files in a folder beside the executable, which is the
    /// wrong home for key material: a copy of id_rsa there is a credential sitting in plaintext,
    /// surviving in every backup folder the user forgets to delete, and it defeats the passphrase
    /// prompt that protects the original. Keys are meant to be re-issued on a new machine, not
    /// carried to it by a settings tool.
    ///
    /// That exclusion is expressed BY CONSTRUCTION and not by a filter: FileModule copies the
    /// files listed below and never enumerates the directory, so a private key cannot be swept up
    /// by a path that happens to match. Adding an entry here is the only way to widen it, and
    /// DeveloperModuleTests pins this exact list so doing so fails a test that names this reason.
    ///
    /// Not restored: NTFS permissions. Windows OpenSSH refuses to use a PRIVATE KEY whose ACL is
    /// too permissive, but is tolerant about config and known_hosts, so the files this module
    /// actually carries are usable after a plain copy. This would need saying if the exclusion
    /// above were ever reversed.
    /// </remarks>
    public class ESsh : FileModule
    {
        public ESsh()
        {
            Title = "SSH client configuration";
            Info = "This will back up your SSH client settings: the host aliases and per-host options in .ssh\\config, and the recorded server fingerprints in .ssh\\known_hosts.\n\nYour private keys are deliberately NOT backed up. They would be stored unencrypted in the backup folder, which is not a safe place for them - generate new keys on a new PC instead.";

            // known_hosts is a security control, not a convenience file: it is what makes ssh warn
            // you that a server's identity changed. Overwriting it is therefore worth a line in
            // the text the user consents against, even though nothing is destroyed that a fresh
            // connection would not rebuild.
            WarningMessage = "Restoring replaces your record of known SSH server fingerprints (known_hosts) with the one from the backup. That record is what warns you if a server's identity changes, so any server you have connected to since the backup goes back to being unrecognised, and you will be asked to confirm its fingerprint again the next time you connect.";

            Files.Add(Path.Combine(Data.UserProfile, ".ssh", "config"));
            Files.Add(Path.Combine(Data.UserProfile, ".ssh", "known_hosts"));
        }

        // True for both: a machine whose owner has never used ssh has neither file, and ssh itself
        // creates known_hosts only on the first connection. Absent here means "not used yet", not
        // "broken", so it is a Skip rather than the red row a fault deserves.
        protected override bool AbsenceIsNormal(string file) => true;

        // No close requirement: ssh.exe reads these files per invocation and holds nothing open
        // between connections, so there is no process whose in-memory copy could overwrite a
        // restored file. Keeping the base HasBackupIn default of true is therefore correct.
    }
}
