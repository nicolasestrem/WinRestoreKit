using WinRestoreKit;
using System;
using System.Collections.Generic;
using Xunit;

namespace WinRestoreKit.Tests
{
    public class ExplorerRestartTests
    {
        private static readonly CloseResult[] Closes =
        {
            CloseResult.NotRunning, CloseResult.Exited, CloseResult.StillRunning, CloseResult.AccessDenied
        };

        private static readonly ShellOutcome[] Shells =
        {
            ShellOutcome.RestartedByWindows, ShellOutcome.Restarted,
            ShellOutcome.FailedToStart, ShellOutcome.NotAttempted
        };

        // --- The decision ---

        // A close that did not clear the old shell must never lead to a start. explorer.exe launched
        // against a running shell opens a stray file-browser window rather than becoming the shell,
        // which is the N-shells bug this method was rewritten to remove, reappearing at N=2.
        [Theory]
        [InlineData(CloseResult.NotRunning, false, null, ShellOutcome.Restarted)]
        [InlineData(CloseResult.Exited, false, null, ShellOutcome.Restarted)]
        [InlineData(CloseResult.NotRunning, true, null, ShellOutcome.RestartedByWindows)]
        [InlineData(CloseResult.Exited, true, null, ShellOutcome.RestartedByWindows)]
        [InlineData(CloseResult.Exited, false, "access denied", ShellOutcome.FailedToStart)]
        [InlineData(CloseResult.NotRunning, false, "access denied", ShellOutcome.FailedToStart)]
        [InlineData(CloseResult.StillRunning, false, null, ShellOutcome.NotAttempted)]
        [InlineData(CloseResult.StillRunning, true, null, ShellOutcome.NotAttempted)]
        [InlineData(CloseResult.AccessDenied, false, null, ShellOutcome.NotAttempted)]
        [InlineData(CloseResult.AccessDenied, true, null, ShellOutcome.NotAttempted)]
        public void DecideShellOutcome_Table(CloseResult close, bool running, string startError, ShellOutcome expected)
            => Assert.Equal(expected, ExplorerRestartResult.DecideShellOutcome(close, running, startError));

        // A start error that arrived from a caught exception with an empty Message must not be read
        // as "the start succeeded".
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void DecideShellOutcome_BlankStartError_CountsAsNoError(string startError)
            => Assert.Equal(ShellOutcome.Restarted,
                   ExplorerRestartResult.DecideShellOutcome(CloseResult.Exited, false, startError));

        // A close that did not clear the shell must not be reported as an automatic Windows restart:
        // the shell that is running is the OLD one, still alive.
        [Theory]
        [InlineData(CloseResult.StillRunning)]
        [InlineData(CloseResult.AccessDenied)]
        public void DecideShellOutcome_UnclearedClose_IsNeverAttributedToWindows(CloseResult close)
            => Assert.NotEqual(ShellOutcome.RestartedByWindows,
                   ExplorerRestartResult.DecideShellOutcome(close, true, null));

        [Theory]
        [InlineData(CloseResult.NotRunning, false, true)]
        [InlineData(CloseResult.Exited, false, true)]
        [InlineData(CloseResult.NotRunning, true, false)]
        [InlineData(CloseResult.Exited, true, false)]
        [InlineData(CloseResult.StillRunning, false, false)]
        [InlineData(CloseResult.StillRunning, true, false)]
        [InlineData(CloseResult.AccessDenied, false, false)]
        [InlineData(CloseResult.AccessDenied, true, false)]
        public void ShouldStartShell_Table(CloseResult close, bool running, bool expected)
            => Assert.Equal(expected, ExplorerRestartResult.ShouldStartShell(close, running));

        // The two functions have to agree about whether a start happened. If ShouldStartShell says
        // no, DecideShellOutcome must not report an outcome that only a start could produce.
        [Fact]
        public void ShouldStartShell_AndDecideShellOutcome_AgreeOnEveryPair()
        {
            foreach (CloseResult close in Closes)
            {
                foreach (bool running in new[] { false, true })
                {
                    bool starts = ExplorerRestartResult.ShouldStartShell(close, running);

                    // null start error = the start succeeded, or none was attempted. Only the first
                    // of those may report Restarted.
                    ShellOutcome outcome = ExplorerRestartResult.DecideShellOutcome(close, running, null);

                    Assert.Equal(starts, outcome == ShellOutcome.Restarted);
                }
            }
        }

        // --- Describe ---

        [Fact]
        public void Describe_EveryCloseAndShellPair_IsADistinctSentence()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (CloseResult close in Closes)
            {
                foreach (ShellOutcome shell in Shells)
                {
                    string sentence = new ExplorerRestartResult(close, shell).Describe();

                    Assert.False(string.IsNullOrWhiteSpace(sentence));
                    Assert.EndsWith(".", sentence, StringComparison.Ordinal);
                    Assert.True(seen.Add(sentence),
                        "Duplicate sentence for " + close + " / " + shell + ": " + sentence);
                }
            }

            Assert.Equal(Closes.Length * Shells.Length, seen.Count);
        }

        // The user has to be told what went wrong, not merely that something did - this string is
        // the entire content of the failure dialog.
        [Fact]
        public void Describe_FailedToStart_CarriesTheError()
        {
            string s = new ExplorerRestartResult(CloseResult.Exited, ShellOutcome.FailedToStart,
                "The system cannot find the file specified").Describe();

            Assert.Contains("The system cannot find the file specified", s, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(CloseResult.NotRunning)]
        [InlineData(CloseResult.Exited)]
        [InlineData(CloseResult.StillRunning)]
        [InlineData(CloseResult.AccessDenied)]
        public void Describe_FailedToStart_NeverClaimsAShellCameBack(CloseResult close)
        {
            string s = new ExplorerRestartResult(close, ShellOutcome.FailedToStart, "boom").Describe();

            Assert.Contains("could not be started", s, StringComparison.Ordinal);
            Assert.DoesNotContain("was restarted", s, StringComparison.Ordinal);
        }

        // Fail closed: an enum member added later must produce "I do not know" rather than
        // inheriting whichever branch happened to be listed first.
        [Fact]
        public void Describe_UnknownShellOutcome_ClaimsNothing()
        {
            string s = new ExplorerRestartResult(CloseResult.Exited, (ShellOutcome)99).Describe();

            Assert.Contains("could not be determined", s, StringComparison.Ordinal);
            Assert.DoesNotContain("was started", s, StringComparison.Ordinal);
            Assert.DoesNotContain("was restarted", s, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(ShellOutcome.RestartedByWindows)]
        [InlineData(ShellOutcome.Restarted)]
        [InlineData(ShellOutcome.FailedToStart)]
        [InlineData(ShellOutcome.NotAttempted)]
        public void Describe_UnknownCloseResult_StillProducesASentence(ShellOutcome shell)
        {
            string s = new ExplorerRestartResult((CloseResult)99, shell).Describe();

            Assert.False(string.IsNullOrWhiteSpace(s));
            Assert.EndsWith(".", s, StringComparison.Ordinal);
        }

        [Fact]
        public void Describe_UnknownCloseResult_DoesNotDescribeHowTheShellClosed()
        {
            string s = new ExplorerRestartResult((CloseResult)99, ShellOutcome.Restarted).Describe();

            Assert.DoesNotContain("was closed", s, StringComparison.Ordinal);
            Assert.DoesNotContain("was not running", s, StringComparison.Ordinal);
        }

        // --- The carried close result ---

        [Fact]
        public void BlankError_IsNormalisedToNull()
            => Assert.Null(new ExplorerRestartResult(CloseResult.Exited, ShellOutcome.Restarted, "   ").Error);

        [Fact]
        public void CloseAndShellAreCarriedThrough()
        {
            ExplorerRestartResult r = new ExplorerRestartResult(
                CloseResult.AccessDenied, ShellOutcome.NotAttempted, "denied");

            Assert.Equal(CloseResult.AccessDenied, r.Close);
            Assert.Equal(ShellOutcome.NotAttempted, r.Shell);
            Assert.Equal("denied", r.Error);
        }
    }
}
