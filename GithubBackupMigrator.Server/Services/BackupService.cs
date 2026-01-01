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
        //private readonly string workDir = @"C:\repo-backups";
        //static readonly string logFile = @"C:\repo-backups\repo-backup-log.txt";

        private readonly string workDir;
        private readonly string logFile;

        public BackupService(IHubContext<SignalRHub> hub)
        {
            _hub = hub;

            // Base folder of your backend project
            string baseDir = AppContext.BaseDirectory;

            // Backup folder inside project folder
            workDir = Path.Combine(baseDir, "Backups");
            logFile = Path.Combine(baseDir, "Backups", "repo-backup-log.txt");
        }

        public async Task StartBackupService(string jobId, BackupRequest model)
        {
            try
            {
                Log("INFO: Initializing backup service...");

                if (!Directory.Exists(workDir))
                {
                    Directory.CreateDirectory(workDir);
                    Log($"INFO: Created working directory: {workDir}");
                }

                await SendStatusUpdate(jobId, "STARTED", "Backup process started");

                Log($"INFO: Fetching repositories from source user: {model.SourceGithubUser}");
                var sourceRepos = await GetGithubRepos(model.SourceGithubUser, model.SourceGithubToken);
                Log($"INFO: Found {sourceRepos.Length} repositories in source account");

                Log($"INFO: Fetching repositories from target user: {model.TargetGithubUser}");
                var targetRepos = await GetGithubRepos(model.TargetGithubUser, model.TargetGithubToken);
                Log($"INFO: Found {targetRepos.Length} repositories in target account");

                int total = sourceRepos.Length;
                int current = 0;
                int success = 0;
                int failed = 0;
                int skipped = 0;

                foreach (var repo in sourceRepos)
                {
                    current++;

                    string repoDir = Path.Combine(workDir, repo);

                    string sourceUrl = string.IsNullOrEmpty(model.SourceGithubToken)
                        ? $"https://github.com/{model.SourceGithubUser}/{repo}.git"
                        : $"https://{model.SourceGithubToken}@github.com/{model.SourceGithubUser}/{repo}.git";

                    string targetUrl = $"https://{model.TargetGithubToken}@github.com/{model.TargetGithubUser}/{repo}.git";

                    try
                    {
                        await SendProgress(jobId, repo, current, total, "Processing");

                        bool repoExistsInTarget = targetRepos.Contains(repo);

                        if (repoExistsInTarget)
                        {
                            // Get latest commit hashes from both repos
                            string sourceLatestCommit = await GetLatestCommitHash(model.SourceGithubUser, repo, model.SourceGithubToken);
                            string targetLatestCommit = await GetLatestCommitHash(model.TargetGithubUser, repo, model.TargetGithubToken);

                            if (sourceLatestCommit == targetLatestCommit)
                            {
                                //Console.WriteLine($"{repo} is already up to date. Skipping...");
                                Log($"SKIPPED: {repo} - Already up to date");
                                skipped++;
                                await SendProgress(jobId, repo, current, total, "Skipped", "Already up to date");
                                await SendSummaryUpdate(jobId, current, total, success, failed, skipped);
                                continue;
                            }
                            else
                            {
                                //Console.WriteLine($"\n{repo} has updates. Syncing...");
                                Log($"UPDATING: {repo} - Source: {sourceLatestCommit?.Substring(0, 7)}, Target: {targetLatestCommit?.Substring(0, 7)}");
                            }
                        }
                        else
                        {
                            Log($"NEW REPO: {repo}");
                        }

                        // Clone or update local mirror
                        if (Directory.Exists(repoDir))
                        {
                            await SendProgress(jobId, repo, current, total, "Updating", "Updating local mirror...");
                            Log($"DEBUG: Updating local mirror for '{repo}'");
                            RunGitCommand("remote update --prune", repoDir);
                        }
                        else
                        {
                            await SendProgress(jobId, repo, current, total, "Cloning", "Cloning from source...");
                            Log($"DEBUG: Cloning repository '{repo}' from source");
                            RunGitCommand($"clone --mirror {sourceUrl} \"{repoDir}\"");
                        }

                        if (!repoExistsInTarget)
                        {
                            await SendProgress(jobId, repo, current, total, "Creating", "Creating repository in target...");
                            Log($"DEBUG: Creating repository '{repo}' in target account");
                            await CreateTargetGithubRepo(repo, model.TargetGithubToken);
                        }

                        // Set up target remote if not already set
                        try
                        {
                            RunGitCommand("remote get-url target", repoDir);
                        }
                        catch
                        {
                            Log($"DEBUG: Adding target remote for '{repo}'");
                            RunGitCommand($"remote add target {targetUrl}", repoDir);
                        }

                        await SendProgress(jobId, repo, current, total, "Pushing", "Pushing to target...");
                        Log($"DEBUG: Pushing '{repo}' to target repository");
                        RunGitCommand("push target --mirror", repoDir);

                        await SendProgress(jobId, repo, current, total, "Completed");
                        Log($"SUCCESS: {repo}");
                        success++;
                        await SendSummaryUpdate(jobId, current, total, success, failed, skipped);
                    }
                    catch (Exception ex)
                    {
                        await SendProgress(jobId, repo, current, total, $"Failed: {ex.Message}");
                        Log($"FAILED: {repo} - {ex.Message}");
                        failed++;
                        await SendSummaryUpdate(jobId, current, total, success, failed, skipped);
                    }
                }

                Log($"INFO: Backup process completed. Total: {total}, Success: {success}, Failed: {failed}, Skipped: {skipped}");
                await SendStatusUpdate(jobId, "COMPLETED", $"Backup finished - {success} successful, {failed} failed, {skipped} skipped");

                await _hub.Clients.Group(jobId)
                    .SendAsync("BackupFinished", new 
                    { 
                        total, 
                        success, 
                        failed, 
                        skipped,
                        message = "Backup process completed successfully"
                    });
            }
            catch (Exception ex)
            {
                Log($"CRITICAL ERROR: {ex.Message}\nStack Trace: {ex.StackTrace}");

                await _hub.Clients.Group(jobId)
                    .SendAsync("BackupError", new
                    {
                        message = "Backup process failed",
                        error = ex.Message
                    });
            }
        }

        private async Task<string> GetLatestCommitHash(string user, string repo, string token)
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

        private async Task CreateTargetGithubRepo(string repo, string token)
        {
            using var client = CreateClient(token);
            var payload = JsonSerializer.Serialize(new { name = repo, @private = true });
            var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.github.com/user/repos", content);
            if (!response.IsSuccessStatusCode &&
                response.StatusCode != System.Net.HttpStatusCode.UnprocessableEntity)
            {
                throw new Exception($"Failed to create repo: {response.StatusCode}");
            }
        }

        private void RunGitCommand(string command, string workingDir = "")
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

        private async Task SendProgress(string jobId, string repo, int current, int total, string status, string message = "")
        {
            await _hub.Clients.Group(jobId).SendAsync("BackupProgress", new
            {
                Repo = repo,
                Current = current,
                Total = total,
                Status = status,
                Message = message,
                Timestamp = DateTime.UtcNow
            });
        }

        private async Task SendStatusUpdate(string jobId, string status, string message)
        {
            await _hub.Clients.Group(jobId).SendAsync("BackupStatus", new
            {
                Status = status,
                Message = message,
                Timestamp = DateTime.UtcNow
            });
        }

        private async Task SendSummaryUpdate(string jobId, int current, int total, int success, int failed, int skipped)
        {
            await _hub.Clients.Group(jobId).SendAsync("BackupSummary", new
            {
                Current = current,
                Total = total,
                Success = success,
                Failed = failed,
                Skipped = skipped,
                Percentage = total > 0 ? (int)((double)current / total * 100) : 0,
                Timestamp = DateTime.UtcNow
            });
        }

        private void Log(string message)
        {
            try
            {
                // Console output
                Console.WriteLine(message);

                // File logging
                string logDir = Path.GetDirectoryName(logFile);
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                File.AppendAllText(logFile, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC: {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }
}
