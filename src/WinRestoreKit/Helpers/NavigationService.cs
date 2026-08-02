using System.Collections.Generic;
using System.Windows.Forms;

namespace WinRestoreKit
{
    /// <summary>
    /// Owns which view is in the content host, and how to get back.
    /// </summary>
    /// <remarks>
    /// Replaces the static ViewHelper.SwitchView, which could express exactly one thing: clear the
    /// panel and add a control. Its "back" was a hardcoded return to the one main page, found by
    /// scanning Application.OpenForms - so the restore wizard PR 7 adds, where step 2 must return to
    /// step 1 and step 1 to wherever the user entered from, has no way to be written against it.
    ///
    /// An instance rather than a static because there is one host panel owned by one form, and the
    /// static version's mutable public fields (mainForm, INavPage) were reachable and writable from
    /// any view in the app.
    ///
    /// Views are NOT disposed when navigated away from. The rail entries are constructed once by
    /// MainForm and reused, which is what makes ConfPageView's checkbox selection and log pane
    /// survive a trip to Home and back - the same lifetime MainForm.configPage already had.
    /// </remarks>
    internal sealed class NavigationService
    {
        private readonly Panel host;
        private readonly Stack<Control> back = new Stack<Control>();

        internal NavigationService(Panel host)
        {
            this.host = host;
        }

        /// <summary>
        /// Where <see cref="Pop"/> lands when there is nothing to go back to.
        /// </summary>
        /// <remarks>
        /// A page entered from the rail has an empty back stack, but it still has a Back button -
        /// About and Restore both do. Without a root, that button would be dead on exactly the paths
        /// a user is most likely to take. Set once by MainForm to Home.
        /// </remarks>
        internal Control Root { get; set; }

        internal Control Current { get; private set; }

        /// <summary>
        /// Shows a view as a new root, discarding any back history.
        /// </summary>
        /// <remarks>
        /// This is rail navigation: picking Home or Back up is not "deeper", so keeping the previous
        /// page on a stack would make Back mean something the user did not do.
        /// </remarks>
        internal void Show(Control view)
        {
            back.Clear();
            Render(view, Views.ViewEntry.Fresh);
        }

        /// <summary>Shows a view and remembers the current one for <see cref="Pop"/>.</summary>
        internal void Push(Control view)
        {
            if (Current != null && Current != view)
                back.Push(Current);

            Render(view, Views.ViewEntry.Fresh);
        }

        /// <summary>
        /// Returns to the previous view, or to <see cref="Root"/> when there is none.
        /// </summary>
        internal void Pop()
        {
            if (back.Count > 0)
            {
                Render(back.Pop(), Views.ViewEntry.Back);
                return;
            }

            if (Root != null)
                Render(Root, Views.ViewEntry.Back);
        }

        private void Render(Control view, Views.ViewEntry entry)
        {
            if (view == null)
                return;

            Current = view;

            host.SuspendLayout();

            // Remove rather than Clear: Clear does not dispose, but it does drop every child, and
            // the views here are long-lived instances that get added back later. Removing only what
            // is displayed keeps that explicit.
            for (int i = host.Controls.Count - 1; i >= 0; i--)
                host.Controls.RemoveAt(i);

            view.Dock = DockStyle.Fill;
            host.Controls.Add(view);

            host.ResumeLayout(true);

            // Rebuilt from disk on every visit, so a backup that finished a moment ago is on screen
            // when the user gets back to Home rather than one navigation later. The entry kind lets
            // a view tell "came back to what they were doing" from "started this again".
            (view as Views.IRefreshableView)?.RefreshView(entry);
        }
    }
}
