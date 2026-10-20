import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  DemoSimulation,
  GeneratedInbox,
  InboxMessages,
  InboxSession,
  InboxStatus,
  MessageDetail,
  OtpHistory,
  ProviderInfo,
} from './temp-mail.models';

/** The single place the frontend talks to the ASP.NET Core API. */
@Injectable({ providedIn: 'root' })
export class TempMailApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiBaseUrl}/temp-mail`;

  getProvider(): Observable<ProviderInfo> {
    return this.http.get<ProviderInfo>(`${this.baseUrl}/provider`);
  }

  generate(): Observable<GeneratedInbox> {
    return this.http.post<GeneratedInbox>(`${this.baseUrl}/generate`, null);
  }

  getStatus(session: InboxSession): Observable<InboxStatus> {
    return this.http.get<InboxStatus>(this.inboxUrl(session, 'status'), { headers: this.headers(session) });
  }

  getMessages(session: InboxSession): Observable<InboxMessages> {
    return this.http.get<InboxMessages>(this.inboxUrl(session, 'messages'), { headers: this.headers(session) });
  }

  refresh(session: InboxSession): Observable<InboxMessages> {
    return this.http.post<InboxMessages>(this.inboxUrl(session, 'refresh'), null, { headers: this.headers(session) });
  }

  getMessage(session: InboxSession, messageId: string): Observable<MessageDetail> {
    return this.http.get<MessageDetail>(this.inboxUrl(session, `messages/${encodeURIComponent(messageId)}`), {
      headers: this.headers(session),
    });
  }

  getOtp(session: InboxSession): Observable<OtpHistory> {
    return this.http.get<OtpHistory>(this.inboxUrl(session, 'otp'), { headers: this.headers(session) });
  }

  deleteInbox(session: InboxSession): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${encodeURIComponent(session.inboxId)}`, {
      headers: this.headers(session),
    });
  }

  simulateDemoOtp(session: InboxSession): Observable<DemoSimulation> {
    return this.http.post<DemoSimulation>(this.inboxUrl(session, 'demo/simulate-otp'), null, {
      headers: this.headers(session),
    });
  }

  private inboxUrl(session: InboxSession, path: string): string {
    return `${this.baseUrl}/${encodeURIComponent(session.inboxId)}/${path}`;
  }

  private headers(session: InboxSession): HttpHeaders {
    return new HttpHeaders({ 'X-Inbox-Token': session.accessToken });
  }
}
