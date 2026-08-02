using System;
using System.Collections.Generic;

namespace WinRestoreKit
{
    public enum RestoreTargetKind
    {
        RegistryKey,
        Folder,
        File,
        Command
    }

    /// <summary>
    /// One thing a module's restore writes to: a registry key, a folder, a file, or an external
    /// command.
    /// </summary>
    /// <remarks>
    /// Declarative only. Nothing here performs or checks an operation - it is the text the user is
    /// shown before consenting, so it must describe what the restore will touch rather than what it
    /// found. A target the user cannot recognise is the same defect as a missing one.
    /// </remarks>
    public sealed class RestoreTarget
    {
        /// <summary>
        /// The text shown for a module that declared nothing.
        /// </summary>
        /// <remarks>
        /// Deliberately a visible wart rather than an empty block. A future author who forgets to
        /// declare produces a line in the dialog users read before consenting, which is noticed;
        /// a module that silently contributes no lines reads exactly like one that touches nothing.
        /// </remarks>
        public const string UndeclaredMarker = "(this item does not declare what it overwrites)";

        /// <summary>
        /// The text shown in place of a single declaration entry that came through as null.
        /// </summary>
        /// <remarks>
        /// Deliberately a DIFFERENT sentence from <see cref="UndeclaredMarker"/>. "The module
        /// declared nothing at all" and "one entry of the module's declaration is broken" are
        /// different facts, and the dialog the user consents against must not conflate them: the
        /// second means the module's other lines are real and one thing it overwrites is unnamed.
        /// The ctor of this type rejects an empty path, so only a null LIST ENTRY reaches this -
        /// which is a bug in the module, and it fails closed and loudly rather than silently
        /// dropping the line and understating what the restore touches.
        /// </remarks>
        public const string UnnamedTargetMarker = "(this item overwrites something it does not name)";

        public RestoreTargetKind Kind { get; }

        /// <summary>Never null, never empty. A key path, a folder path, or a command description.</summary>
        public string Path { get; }

        private RestoreTarget(RestoreTargetKind kind, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A restore target must name what it touches.", nameof(path));

            Kind = kind;
            Path = path;
        }

        public static RestoreTarget RegistryKey(string path)
            => new RestoreTarget(RestoreTargetKind.RegistryKey, path);

        public static RestoreTarget Folder(string path)
            => new RestoreTarget(RestoreTargetKind.Folder, path);

        /// <summary>One file this module's restore overwrites.</summary>
        /// <remarks>
        /// Separate from <see cref="Folder"/> because the two name different things: a directory
        /// the restore writes into, versus one file it replaces. Naming
        /// "folder: C:\Users\me\.ssh" for a restore that only ever touches two files inside it
        /// invites the reader to picture the folder as the unit at risk, and the folder is where
        /// their private keys are.
        ///
        /// Careful about what the folder label does NOT mean: Utils.CopyFolder MERGES - it
        /// overwrites the files the backup contains and leaves everything else in place. An
        /// earlier draft of this comment justified the File kind by claiming the folder label
        /// reads as "everything under here is replaced", which would have been this file asserting
        /// a property of folder restores that the code does not have. The distinction is worth
        /// drawing on precision, not on a scariness the implementation cannot back.
        /// </remarks>
        public static RestoreTarget File(string path)
            => new RestoreTarget(RestoreTargetKind.File, path);

        public static RestoreTarget Command(string description)
            => new RestoreTarget(RestoreTargetKind.Command, description);

        public bool IsUndeclared
            => Kind == RestoreTargetKind.Command && Path == UndeclaredMarker;

        /// <summary>
        /// The declaration BackupBase falls back to when a module supplies none.
        /// </summary>
        /// <remarks>
        /// The module name is required so a caller cannot reach this by accident from a context that
        /// does not know which module it is describing; GetType().Name is never empty, so a throw
        /// here means the call site is wrong, not that a module is unusual.
        /// </remarks>
        public static IReadOnlyList<RestoreTarget> Undeclared(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
                throw new ArgumentException("An undeclared marker must name its module.", nameof(moduleName));

            return new[] { Command(UndeclaredMarker) };
        }
    }
}
