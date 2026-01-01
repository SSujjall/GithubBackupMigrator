import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject } from 'rxjs';

export interface BackupProgress {
  repo: string;
  current: number;
  total: number;
  status: string;
  message?: string;
  timestamp: string;
}

export interface BackupSummary {
  current: number;
  total: number;
  success: number;
  failed: number;
  skipped: number;
  percentage: number;
  timestamp: string;
}


@Injectable({
  providedIn: 'root'
})
export class SignalrService {
  private hubConnection!: signalR.HubConnection;

  public progress$ = new BehaviorSubject<BackupProgress | null>(null);
  public summary$ = new BehaviorSubject<BackupSummary | null>(null);
  public status$ = new BehaviorSubject<any>(null);
  public finished$ = new BehaviorSubject<any>(null);
  public error$ = new BehaviorSubject<any>(null);

  private hubUrl = 'https://localhost:7278/backupProgress';

  /** Connect to SignalR and join job group */
  connect(jobId: string) {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(this.hubUrl, { withCredentials: false })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.start()
      .then(() => {
        console.log('SignalR connected');
        this.hubConnection.invoke('JoinJob', jobId)
          .catch(err => console.error('JoinJob error:', err));
      })
      .catch(err => console.error('SignalR connection error:', err));

    // Listen to backend events
    this.hubConnection.on('BackupProgress', (data: BackupProgress) => this.progress$.next(data));
    this.hubConnection.on('BackupSummary', (data: BackupSummary) => this.summary$.next(data));
    this.hubConnection.on('BackupStatus', (data: any) => this.status$.next(data));
    this.hubConnection.on('BackupFinished', (data: any) => this.finished$.next(data));
    this.hubConnection.on('BackupError', (data: any) => this.error$.next(data));
  }
}
