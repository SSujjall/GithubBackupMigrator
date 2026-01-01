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

  progressList: BackupProgress[] = [];
  summary: BackupSummary | null = null;
  statusMessage: string = '';
  finishedMessage: string = '';
  errorMessage: string = '';

  constructor(
    private backupService: BackupProgressService,
    private signalRService: SignalrService
  ) {
    // Subscribe to SignalR updates
    this.signalRService.progress$.subscribe(progress => {
      if (progress) {
        const index = this.progressList.findIndex(p => p.repo === progress.repo);
        if (index >= 0) this.progressList[index] = progress;
        else this.progressList.push(progress);
      }
    });

    this.signalRService.summary$.subscribe(summary => this.summary = summary);
    this.signalRService.status$.subscribe(status => this.statusMessage = status?.Message || '');
    this.signalRService.finished$.subscribe(finished => this.finishedMessage = finished?.message || '');
    this.signalRService.error$.subscribe(err => this.errorMessage = err?.message || '');
  }

  async startBackup() {
    const payload: BackupRequest = {
      SourceGithubUser: this.sourceUser,
      SourceGithubToken: this.sourceToken,
      TargetGithubUser: this.targetUser,
      TargetGithubToken: this.targetToken
    };

    try {
      const jobId = await this.backupService.startBackup(payload);
      console.log('Backup started, jobId:', jobId);
      this.signalRService.connect(jobId);
    } catch (err) {
      console.error('Backup failed:', err);
      this.errorMessage = 'Failed to start backup';
    }
  }
}
