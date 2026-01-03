using GithubBackupMigrator.Server.Models;
using GithubBackupMigrator.Server.Services.Helpers;

namespace GithubBackupMigrator.Server.Services
{
    public interface IBackupService
    {
        Task StartBackupService(string jobId, BackupRequest model);
    }

    public class BackupService : IBackupService
    {
        private readonly SignalRHelper _srh;
        private readonly LogHelper _logH;
        private readonly GithubCommandHelper _gch;

        //private readonly string workDir = @"C:\repo-backups";
        //static readonly string logFile = @"C:\repo-backups\repo-backup-log.txt";

        private readonly string workDir;

        public BackupService(SignalRHelper srh, LogHelper logH, GithubCommandHelper gch)
        {
            _srh = srh;
            _logH = logH;
            _gch = gch;

            // Base folder of your backend project
            string baseDir = AppContext.BaseDirectory;

            // Backup folder inside project folder
            workDir = Path.Combine(baseDir, "Backups");
        }

        public async Task StartBackupService(string jobId, BackupRequest model)
        {
            try
            {
                _logH.Log("INFO: Initializing backup service...");

                if (!Directory.Exists(workDir))
                {
                    Directory.CreateDirectory(workDir);
                    _logH.Log($"INFO: Created working directory: {workDir}");
                }

                await _srh.SendStatusUpdate(jobId, "STARTED", "Backup process started");

                _logH.Log($"INFO: Fetching repositories from source user: {model.SourceGithubUser}");
                var sourceRepos = await _gch.GetGithubRepos(model.SourceGithubUser, model.SourceGithubToken);
                _logH.Log($"INFO: Found {sourceRepos.Length} repositories in source account");

                _logH.Log($"INFO: Fetching repositories from target user: {model.TargetGithubUser}");
                var targetRepos = await _gch.GetGithubRepos(model.TargetGithubUser, model.TargetGithubToken);
                _logH.Log($"INFO: Found {targetRepos.Length} repositories in target account");

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
                        await _srh.SendProgress(jobId, repo, current, total, "Processing");

                        bool repoExistsInTarget = targetRepos.Contains(repo);

                        if (repoExistsInTarget)
                        {
                            // Get latest commit hashes from both repos
                            string sourceLatestCommit = await _gch.GetLatestCommitHash(model.SourceGithubUser, repo, model.SourceGithubToken);
                            string targetLatestCommit = await _gch.GetLatestCommitHash(model.TargetGithubUser, repo, model.TargetGithubToken);

                            if (sourceLatestCommit == targetLatestCommit)
                            {
                                //Console.WriteLine($"{repo} is already up to date. Skipping...");
                                _logH.Log($"SKIPPED: {repo} - Already up to date");
                                skipped++;
                                await _srh.SendProgress(jobId, repo, current, total, "Skipped", "Already up to date");
                                await _srh.SendSummaryUpdate(jobId, current, total, success, failed, skipped);
                                continue;
                            }
                            else
                            {
                                //Console.WriteLine($"\n{repo} has updates. Syncing...");
                                _logH.Log($"UPDATING: {repo} - Source: {sourceLatestCommit?.Substring(0, 7)}, Target: {targetLatestCommit?.Substring(0, 7)}");
                            }
                        }
                        else
                        {
                            _logH.Log($"NEW REPO: {repo}");
                        }

                        // Clone or update local mirror
                        if (Directory.Exists(repoDir))
                        {
                            await _srh.SendProgress(jobId, repo, current, total, "Updating", "Updating local mirror...");
                            _logH.Log($"DEBUG: Updating local mirror for '{repo}'");
                            _gch.RunGitCommand("remote update --prune", repoDir);
                        }
                        else
                        {
                            await _srh.SendProgress(jobId, repo, current, total, "Cloning", "Cloning from source...");
                            _logH.Log($"DEBUG: Cloning repository '{repo}' from source");
                            _gch.RunGitCommand($"clone --mirror {sourceUrl} \"{repoDir}\"");
                        }

                        if (!repoExistsInTarget)
                        {
                            await _srh.SendProgress(jobId, repo, current, total, "Creating", "Creating repository in target...");
                            _logH.Log($"DEBUG: Creating repository '{repo}' in target account");
                            await _gch.CreateTargetGithubRepo(repo, model.TargetGithubToken);
                        }

                        // Set up target remote if not already set
                        try
                        {
                            _gch.RunGitCommand("remote get-url target", repoDir);
                        }
                        catch
                        {
                            _logH.Log($"DEBUG: Adding target remote for '{repo}'");
                            _gch.RunGitCommand($"remote add target {targetUrl}", repoDir);
                        }

                        await _srh.SendProgress(jobId, repo, current, total, "Pushing", "Pushing to target...");
                        _logH.Log($"DEBUG: Pushing '{repo}' to target repository");
                        _gch.RunGitCommand("push target --mirror", repoDir);

                        await _srh.SendProgress(jobId, repo, current, total, "Completed");
                        _logH.Log($"SUCCESS: {repo}");
                        success++;
                        await _srh.SendSummaryUpdate(jobId, current, total, success, failed, skipped);
                    }
                    catch (Exception ex)
                    {
                        await _srh.SendProgress(jobId, repo, current, total, $"Failed: {ex.Message}");
                        _logH.Log($"FAILED: {repo} - {ex.Message}");
                        failed++;
                        await _srh.SendSummaryUpdate(jobId, current, total, success, failed, skipped);
                    }
                }

                _logH.Log($"INFO: Backup process completed. Total: {total}, Success: {success}, Failed: {failed}, Skipped: {skipped}");
                await _srh.SendStatusUpdate(jobId, "COMPLETED", $"Backup finished - {success} successful, {failed} failed, {skipped} skipped");

                await _srh.SendFinishUpdate(jobId, total, success, failed, skipped);
            }
            catch (Exception ex)
            {
                _logH.Log($"CRITICAL ERROR: {ex.Message}\nStack Trace: {ex.StackTrace}");
                await _srh.SendFinishUpdate(isError: true, ex: ex);
            }
        }        
    }
}
