import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ClipboardService } from '../../core/clipboard.service';
import { InboxStore } from '../../core/inbox.store';
import { NotificationService } from '../../core/notification.service';

type BodyView = 'formatted' | 'text';

/**
 * Shows one message. HTML is sanitized on the server and then rendered inside a sandboxed iframe
 * (no scripts, no same-origin access) with a Content-Security-Policy that blocks all remote loads.
 */
@Component({
  selector: 'app-email-details',
  imports: [DatePipe],
  templateUrl: './email-details.component.html',
  styleUrl: './email-details.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EmailDetailsComponent {
  protected readonly store = inject(InboxStore);
  private readonly domSanitizer = inject(DomSanitizer);
  private readonly clipboard = inject(ClipboardService);
  private readonly notifications = inject(NotificationService);

  protected readonly view = signal<BodyView>('formatted');
  protected readonly copied = signal(false);

  protected readonly message = this.store.selectedMessage;
  protected readonly hasHtml = computed(() => !!this.message()?.sanitizedHtml);
  protected readonly hasText = computed(() => !!this.message()?.textBody);
  protected readonly showFormatted = computed(() => this.hasHtml() && (this.view() === 'formatted' || !this.hasText()));

  protected readonly frameDocument = computed<SafeHtml | null>(() => {
    const html = this.message()?.sanitizedHtml;
    if (!html) {
      return null;
    }
    // The HTML was sanitized by the API; the iframe sandbox and CSP are a second layer of defense.
    return this.domSanitizer.bypassSecurityTrustHtml(buildFrameDocument(html));
  });

  constructor() {
    // Start every newly opened message in the formatted view.
    effect(() => {
      this.store.selectedMessageId();
      this.view.set('formatted');
      this.copied.set(false);
    });
  }

  protected async copyCode(code: string): Promise<void> {
    const ok = await this.clipboard.copy(code);
    if (ok) {
      this.copied.set(true);
      this.notifications.success(`Code ${code} copied.`);
    } else {
      this.notifications.error("Couldn't copy automatically. Select the code and copy it manually.");
    }
  }
}

function buildFrameDocument(sanitizedHtml: string): string {
  return `<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:">
<meta name="referrer" content="no-referrer">
<base target="_blank">
<style>
  body { margin: 16px; font-family: -apple-system, 'Segoe UI', Roboto, Arial, sans-serif; color: #0f1e3d; font-size: 15px; line-height: 1.5; word-wrap: break-word; }
  img { max-width: 100%; height: auto; }
  img:not([src]) { display: inline-block; padding: 2px 6px; border: 1px dashed #c3cee2; border-radius: 4px; color: #52617f; font-size: 12px; }
  table { max-width: 100%; }
  a { color: #1440c8; }
</style>
</head>
<body>${sanitizedHtml}</body>
</html>`;
}
