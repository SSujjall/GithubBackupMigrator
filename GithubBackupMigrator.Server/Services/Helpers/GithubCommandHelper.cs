using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GithubBackupMigrator.Server.Services.Helpers
{
    public class GithubCommandHelper
    {
        public async Task<string> GetLatestCommitHash(string user, string repo, string token)
        {
            try
            {
                using var client = CreateClient(token);

                string url = $"https://api.github.com/repos/{user}/{repo}/commits?per_page=1";
                var response = await client.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);

                var commits = doc.RootElement.EnumerateArray().ToList();
                if (commits.Count > 0)
                {
                    return commits[0].GetProperty("sha").GetString();
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not get latest commit for {user}/{repo}: {ex.Message}");
                return null;
            }
        }

        public HttpClient CreateClient(string token)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitBackupApi", "1.0"));
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("token", token);
            return client;
        }

        public async Task<string[]> GetGithubRepos(string user, string token)
        {
            using var client = CreateClient(token);
            string url = !string.IsNullOrEmpty(token)
                ? $"https://api.github.com/user/repos?per_page=100&affiliation=owner&sort=updated&direction=desc"
                : $"https://api.github.com/users/{user}/repos?per_page=100&type=owner&sort=updated&direction=desc";

            var response = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var repoNames = doc.RootElement
                .EnumerateArray()
                .Select(x => x.GetProperty("name").GetString()!)
                .ToArray();

            // Console log total repo count
            Console.WriteLine($"Total repositories for user '{user}': {repoNames.Length}");

            return repoNames;
        }

        public async Task CreateTargetGithubRepo(string repo, string token)
        {
            using var client = CreateClient(token);
            var payload = JsonSerializer.Serialize(new { name = repo, @public = true });
            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.github.com/user/repos", content);
            if (!response.IsSuccessStatusCode &&
                response.StatusCode != System.Net.HttpStatusCode.UnprocessableEntity)
            {
                throw new Exception($"Failed to create repo: {response.StatusCode}");
            }
        }

        public void RunGitCommand(string command, string workingDir = "")
        {
            var processInfo = new ProcessStartInfo("git", command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrEmpty(workingDir) ? Environment.CurrentDirectory : workingDir
            };

            using var process = Process.Start(processInfo);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new Exception($"Git command failed: {error}");

            //if (!string.IsNullOrEmpty(output))
            //    Console.WriteLine(output);
        }
    }
}
