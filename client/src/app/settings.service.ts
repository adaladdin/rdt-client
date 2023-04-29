import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Profile } from './models/profile.model';
import { Setting } from './models/setting.model';

@Injectable({
  providedIn: 'root',
})
export class SettingsService {
  constructor(private http: HttpClient) {}

  public get(): Observable<Setting[]> {
    return this.http.get<Setting[]>(`/Api/Settings`);
  }

  public update(settings: Setting[]): Observable<void> {
    return this.http.put<void>(`/Api/Settings`, settings);
  }

  public getProfile(): Observable<Profile> {
    return this.http.get<Profile>(`/Api/Settings/Profile`);
  }

  public testPath(path: string): Observable<void> {
    return this.http.post<void>(`/Api/Settings/TestPath`, { path });
  }

  public testPlex(token: string): Observable<string> {
    return this.http.post<string>(`/Api/Settings/TestPlex`, { token })
  }

  public testDownloadSpeed(): Observable<number> {
    return this.http.get<number>(`/Api/Settings/TestDownloadSpeed`);
  }

  public testWriteSpeed(): Observable<number> {
    return this.http.get<number>(`/Api/Settings/TestWriteSpeed`);
  }

  public testAria2cConnection(url: string, secret: string): Observable<{ version: string }> {
    return this.http.post<{ version: string }>(`/Api/Settings/TestAria2cConnection`, {
      url,
      secret,
    });
  }
}
