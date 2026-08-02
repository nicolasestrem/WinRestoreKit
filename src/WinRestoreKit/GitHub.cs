using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace WinRestoreKit
{
    internal class Stargazers
    {
        // Event to be subscribed on Mainform
        public event EventHandler<int> StargazersCountFetched;

        // Attributes to indicate that class is a data contract
        [Serializable]
        [DataContract]
        public class GitHubRepository
        {
            [DataMember(Name = "stargazers_count")] // matches GitHub API response
            public int StargazersCount { get; set; }
        }

        public async Task FetchStargazersAsync()
        {
            string repositoryOwner = "nicolasestrem";
            string repositoryName = "WinRestoreKit";

            string apiUrl = $"https://api.github.com/repos/{repositoryOwner}/{repositoryName}";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // This reports WinRestoreKit's own stars, deliberately excluding the original project's historical count.
                    client.DefaultRequestHeaders.Add("User-Agent", DataHelper.Data.UserAgent);

                    // Make GET request
                    Stream responseStream = await client.GetStreamAsync(apiUrl);

                    // Deserialize JSON using DataContractJsonSerializer
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GitHubRepository));
                    GitHubRepository repoInfo = (GitHubRepository)serializer.ReadObject(responseStream);

                    // Extract and display stargazers count
                    int stargazersCount = repoInfo.StargazersCount;

                    // Notify subscribers (MainForm) about the fetched stargazers count
                    StargazersCountFetched?.Invoke(this, stargazersCount);
                }
            }
            catch (Exception ex)
            {
                // Handle exception
                Console.WriteLine($"{ex.Message}");

                // Notify subscribers about  exceptions
                StargazersCountFetched?.Invoke(this, -1);
            }
        }
    }
}