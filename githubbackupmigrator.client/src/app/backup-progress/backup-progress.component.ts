import { Component } from '@angular/core';
import { BackupProgress, BackupSummary, SignalrService } from '../signalr-service/signalr.service';
import { HttpClient } from '@microsoft/signalr';
import { BackupProgressService } from './backup-progress.service';

interface BackupRequest {
  SourceGithubUser: string;
  SourceGithubToken: string;
  TargetGithubUser: string;
  TargetGithubToken: string;
}

@Component({
  selector: 'app-backup-progress',
  templateUrl: './backup-progress.component.html',
  styleUrls: ['./backup-progress.component.css']
})
export class BackupProgressComponent {
  sourceUser: string = '';
  sourceToken: string = '';
  targetUser: string = '';
  targetToken: string = '';

  progressList: BackupProgress[] = []; // All repo progress
  completedTasks: BackupProgress[] = []; // Only Completed/Skipped/Failed
  summary: BackupSummary = {                // Single progress bar data
    current: 0,
    total: 0,
    success: 0,
    failed: 0,
    skipped: 0,
    percentage: 0,
    timestamp: ''
  };

  statusMessage: string = '';
  finishedMessage: string = '';
  errorMessage: string = '';

  constructor(
    private backupService: BackupProgressService,
    private signalRService: SignalrService
  ) {
    // Subscribe to SignalR updates
    this.signalRService.progress$.subscribe(progress => {
      if (!progress) return;

      // Update or add in full progressList
      const index = this.progressList.findIndex(p => p.repo === progress.repo);
      if (index >= 0) this.progressList[index] = progress;
      else this.progressList.push(progress);

      // Add to completedTasks if finished/skipped/failed
      if (progress.status === 'Completed' || progress.status === 'Skipped' || progress.status.startsWith('Failed')) {
        const compIndex = this.completedTasks.findIndex(p => p.repo === progress.repo);
        if (compIndex >= 0) this.completedTasks[compIndex] = progress;
        else this.completedTasks.push(progress);

        // Update single progress bar
        this.summary.current = this.completedTasks.length;
        this.summary.percentage = this.summary.total > 0
          ? Math.floor((this.summary.current / this.summary.total) * 100)
          : 0;

        // Optional: update success/fail/skipped counts if backend doesn't send
        if (progress.status === 'Completed') this.summary.success++;
        else if (progress.status === 'Skipped') this.summary.skipped++;
        else if (progress.status.startsWith('Failed')) this.summary.failed++;

        // Timestamp for progress
        this.summary.timestamp = progress.timestamp || new Date().toISOString();
      }
    });

    // Subscribe to summary updates from backend
    this.signalRService.summary$.subscribe(summary => {
      if (summary) {
        this.summary.total = summary.total || this.summary.total;
        this.summary.success = summary.success || this.summary.success;
        this.summary.failed = summary.failed || this.summary.failed;
        this.summary.skipped = summary.skipped || this.summary.skipped;
        this.summary.timestamp = summary.timestamp || new Date().toISOString();
      }
    });

    // Status, finished, and error messages
    this.signalRService.status$.subscribe(status => this.statusMessage = status?.Message || '');
    this.signalRService.finished$.subscribe(finished => this.finishedMessage = finished?.message || '');
    this.signalRService.error$.subscribe(err => this.errorMessage = err?.message || '');
  }

  async startBackup() {
    // Clear previous state
    this.errorMessage = '';
    this.finishedMessage = '';
    this.statusMessage = '';
    this.progressList = [];
    this.completedTasks = [];
    this.summary = {
      current: 0,
      total: 0,
      success: 0,
      failed: 0,
      skipped: 0,
      percentage: 0,
      timestamp: ''
    };

    const payload: BackupRequest = {
      SourceGithubUser: this.sourceUser,
      SourceGithubToken: this.sourceToken,
      TargetGithubUser: this.targetUser,
      TargetGithubToken: this.targetToken
    };

    try {
      const jobId = await this.backupService.startBackup(payload);
      console.log('Backup started, jobId:', jobId);

      // Connect to SignalR for this job
      this.signalRService.connect(jobId);
    } catch (err) {
      console.error('Backup failed:', err);
      this.errorMessage = 'Failed to start backup';
    }
  }
}
