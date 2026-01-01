import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export interface BackupRequest {
  SourceGithubUser: string;
  SourceGithubToken: string;
  TargetGithubUser: string;
  TargetGithubToken: string;
}

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
export class BackupProgressService {
  private apiUrl = 'https://localhost:7278/api/Main';

  constructor(private http: HttpClient) { }

  /* Start backup and return the jobId */
  startBackup(request: BackupRequest) {
    return this.http.post<any>(this.apiUrl, request).toPromise()
      .then(res => res.data.jobId); // jobId returned from backend
  }
}
