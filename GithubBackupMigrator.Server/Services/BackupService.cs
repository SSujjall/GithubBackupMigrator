using GithubBackupMigrator.Server.Hubs;
using GithubBackupMigrator.Server.Models;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GithubBackupMigrator.Server.Services
{
    public interface IBackupService
    {
        Task StartBackupService(string jobId, BackupRequest model);
    }

    public class BackupService : IBackupService
    {
        private readonly IHubContext<SignalRHub> _hub;
        private readonly string workDir = @"\repo-backups";
        private readonly string logFile = @"\repo-backups\repo-backup-log.txt";

        public BackupService(IHubContext<SignalRHub> hub)
        {
            _hub = hub;
        }

        public async Task StartBackupService(string jobId, BackupRequest model)
        {
            if (!Directory.Exists(workDir))
            {
                Directory.CreateDirectory(workDir);
            }

            var sourceRepos = await GetGithubRepos(model.SourceGithubUser, model.SourceGithubToken);
            var targetRepos = await GetGithubRepos(model.TargetGithubUser, model.TargetGithubToken);

            int total = sourceRepos.Length;
            int current = 0;

            foreach (var repo in sourceRepos)
            {
                current++;

                try
                {
                    await SendProgress(jobId, repo, current, total, "Processing");

                    string repoDir = Path.Combine(workDir, repo);
                    bool existsInTarget = targetRepos.Contains(repo);

                    string sourceUrl = string.IsNullOrEmpty(model.SourceGithubToken)
                        ? $"https://github.com/{model.SourceGithubUser}/{repo}.git"
                        : $"https://{model.SourceGithubToken}@github.com/{model.SourceGithubUser}/{repo}.git";

                    string targetUrl = $"https://{model.TargetGithubToken}@github.com/{model.TargetGithubUser}/{repo}.git";

                    if (Directory.Exists(repoDir))
                        RunGitCommand("remote update --prune", repoDir);
                    else
                        RunGitCommand($"clone --mirror {sourceUrl} \"{repoDir}\"");

                    if (!existsInTarget)
                        await CreateTargetGithubRepo(repo, model.TargetGithubToken);

                    try
                    {
                        RunGitCommand("remote get-url target", repoDir);
                    }
                    catch
                    {
                        RunGitCommand($"remote add target {targetUrl}", repoDir);
                    }

                    RunGitCommand("push target --mirror", repoDir);
                    await SendProgress(jobId, repo, current, total, "Completed");
                }
                catch (Exception ex)
                {
                    await SendProgress(jobId, repo, current, total, $"Failed: {ex.Message}");
                }
            }

            await _hub.Clients.Group(jobId)
            .SendAsync("BackupFinished", new { total });
        }

        private HttpClient CreateClient(string token)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitBackupApi", "1.0"));
            if (!string.IsNullOrEmpty(token))
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("token", token);
            return client;
        }

        private async Task<string[]> GetGithubRepos(string user, string token)
        {
            using var client = CreateClient(token);
            string url = string.IsNullOrEmpty(token)
                ? $"https://api.github.com/users/{user}/repos?per_page=100&type=owner"
                : $"https://api.github.com/user/repos?per_page=100&affiliation=owner";

            var response = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement
                .EnumerateArray()
                .Select(x => x.GetProperty("name").GetString()!)
                .ToArray();
        }

        private async Task CreateTargetGithubRepo(string repo, string token)
        {
            using var client = CreateClient(token);
            var payload = JsonSerializer.Serialize(new { name = repo, @private = true });
            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.github.com/user/repos", content);
            if (!response.IsSuccessStatusCode &&
                response.StatusCode != System.Net.HttpStatusCode.UnprocessableEntity)
            {
                throw new Exception("Failed to create repo");
            }
        }

        private static void RunGitCommand(string command, string workingDir = "")
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

        private async Task SendProgress(string jobId, string repo, int current, int total, string status)
        {
            await _hub.Clients.Group(jobId).SendAsync("BackupProgress", new BackupProgress
            {
                Repo = repo,
                Current = current,
                Total = total,
                Status = status
            });
        }
    }
}
