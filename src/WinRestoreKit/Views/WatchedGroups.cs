using Conf;
using System.Collections.Generic;
using System.Linq;

namespace Views
{
    internal sealed class WatchedGroupSummary
    {
        internal WatchedGroupSummary(string name, string count)
        {
            Name = name;
            Count = count;
        }

        internal string Name { get; }

        internal string Count { get; }
    }

    internal static class WatchedGroups
    {
        internal static IReadOnlyList<WatchedGroupSummary> GetCurrent()
        {
            return ModuleCatalog.CreateAll()
                .GroupBy(registration => registration.Category)
                .Select(group => new WatchedGroupSummary(
                    group.Key,
                    $"{group.Count()} items"))
                .ToList();
        }
    }
}
