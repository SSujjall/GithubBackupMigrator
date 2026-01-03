using GithubBackupMigrator.Server.Models;
using GithubBackupMigrator.Server.Services.Helpers;
using LibGit2Sharp;

namespace GithubBackupMigrator.Server.Services
{
    public class BackupServiceV2 : IBackupService
    {
        private readonly SignalRHelper _srh;
        private readonly LogHelper _logH;
        private readonly GithubCommandHelper _gch;

        private readonly string workDir;

        public BackupServiceV2(SignalRHelper srh, LogHelper logH, GithubCommandHelper gch)
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
                                _logH.Log($"SKIPPED: {repo} - Already up to date");
                                skipped++;
                                await _srh.SendProgress(jobId, repo, current, total, "Skipped", "Already up to date");
                                await _srh.SendSummaryUpdate(jobId, current, total, success, failed, skipped);
                                continue;
                            }
                            else
                            {
                                _logH.Log($"UPDATING: {repo} - Source: {sourceLatestCommit?.Substring(0, 7)}, Target: {targetLatestCommit?.Substring(0, 7)}");
                            }
                        }
                        else
                        {
                            _logH.Log($"NEW REPO: {repo}");
                        }

                        // Clone or update local mirror using LibGit2Sharp
                        if (Directory.Exists(repoDir) && Repository.IsValid(repoDir))
                        {
                            await _srh.SendProgress(jobId, repo, current, total, "Updating", "Updating local mirror...");
                            _logH.Log($"DEBUG: Updating local mirror for '{repo}'");
                            await Task.Run(() => UpdateMirrorRepository(repoDir, model.SourceGithubToken));
                        }
                        else
                        {
                            await _srh.SendProgress(jobId, repo, current, total, "Cloning", "Cloning from source...");
                            _logH.Log($"DEBUG: Cloning repository '{repo}' from source");

                            // Clean up directory if it exists but is not a valid repo
                            if (Directory.Exists(repoDir))
                            {
                                Directory.Delete(repoDir, true);
                            }

                            await Task.Run(() => CloneMirrorRepository(sourceUrl, repoDir, model.SourceGithubToken));
                        }

                        if (!repoExistsInTarget)
                        {
                            await _srh.SendProgress(jobId, repo, current, total, "Creating", "Creating repository in target...");
                            _logH.Log($"DEBUG: Creating repository '{repo}' in target account");
                            await _gch.CreateTargetGithubRepo(repo, model.TargetGithubToken);
                        }

                        await _srh.SendProgress(jobId, repo, current, total, "Pushing", "Pushing to target...");
                        _logH.Log($"DEBUG: Pushing '{repo}' to target repository");
                        await Task.Run(() => PushToTarget(repoDir, targetUrl, model.TargetGithubToken));

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


        private void CloneMirrorRepository(string sourceUrl, string repoDir, string token)
        {
            var cloneOptions = new CloneOptions
            {
                IsBare = true // Mirror clone is a bare repository
            };
            cloneOptions.FetchOptions.CredentialsProvider = (_url, _user, _cred) =>
                new UsernamePasswordCredentials
                {
                    Username = token,
                    Password = string.Empty
                };

            Repository.Clone(sourceUrl, repoDir, cloneOptions);
        }

        private void UpdateMirrorRepository(string repoDir, string token)
        {
            using (var repo = new Repository(repoDir))
            {
                var fetchOptions = new FetchOptions
                {
                    Prune = true
                };
                fetchOptions.CredentialsProvider = (_url, _user, _cred) =>
                    new UsernamePasswordCredentials
                    {
                        Username = token,
                        Password = string.Empty
                    };

                // Fetch from all remotes
                foreach (var remote in repo.Network.Remotes)
                {
                    var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                    Commands.Fetch(repo, remote.Name, refSpecs, fetchOptions, string.Empty);
                }
            }
        }

        private void PushToTarget(string repoDir, string targetUrl, string token)
        {
            using (var repo = new Repository(repoDir))
            {
                // Check if target remote exists, add it if not
                var targetRemote = repo.Network.Remotes["target"];
                if (targetRemote == null)
                {
                    repo.Network.Remotes.Add("target", targetUrl);
                    targetRemote = repo.Network.Remotes["target"];
                }
                else if (targetRemote.Url != targetUrl)
                {
                    // Update remote URL if it changed
                    repo.Network.Remotes.Update("target", r => r.Url = targetUrl);
                    targetRemote = repo.Network.Remotes["target"];
                }

                var pushOptions = new PushOptions();
                pushOptions.CredentialsProvider = (_url, _user, _cred) =>
                    new UsernamePasswordCredentials
                    {
                        Username = token,
                        Password = string.Empty
                    };

                // Push all refs (mirror push)
                var pushRefSpecs = new List<string>();

                // Push all branches
                foreach (var branch in repo.Branches)
                {
                    pushRefSpecs.Add($"+{branch.CanonicalName}:{branch.CanonicalName}");
                }

                // Push all tags
                foreach (var tag in repo.Tags)
                {
                    pushRefSpecs.Add($"+{tag.CanonicalName}:{tag.CanonicalName}");
                }

                if (pushRefSpecs.Any())
                {
                    repo.Network.Push(targetRemote, pushRefSpecs, pushOptions);
                }

                // Note: Complete mirror behavior would require deleting refs on target
                // that don't exist in source, which requires additional logic
            }
        }
    }
}
