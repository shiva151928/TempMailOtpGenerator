import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { ClipboardService } from '../../core/clipboard.service';
import { InboxStore } from '../../core/inbox.store';
import { NotificationService } from '../../core/notification.service';
import { relativeTime } from '../../core/time-format';

@Component({
  selector: 'app-otp-viewer',
  imports: [DatePipe],
  templateUrl: './otp-viewer.component.html',
  styleUrl: './otp-viewer.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OtpViewerComponent {
  protected readonly store = inject(InboxStore);
  private readonly clipboard = inject(ClipboardService);
  private readonly notifications = inject(NotificationService);

  protected readonly otp = this.store.latestOtp;
  protected readonly digits = computed(() => (this.otp()?.code ?? '').split(''));
  protected readonly spokenCode = computed(() => this.digits().join(' '));
  protected readonly olderCodes = computed(() => {
    const latestId = this.otp()?.messageId;
    return this.store.otpHistory().filter((item) => item.messageId !== latestId);
  });
  protected readonly placeholderDigits = [0, 1, 2, 3, 4, 5];

  protected readonly copiedCode = signal<string | null>(null);
  protected readonly highlight = signal(false);

  private copiedTimer: ReturnType<typeof setTimeout> | null = null;
  private highlightTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    // Briefly highlight the box when a new code arrives.
    effect(() => {
      const arrivedAt = this.store.otpArrivedAt();
      if (arrivedAt === 0) {
        return;
      }
      this.highlight.set(true);
      if (this.highlightTimer !== null) {
        clearTimeout(this.highlightTimer);
      }
      this.highlightTimer = setTimeout(() => this.highlight.set(false), 2400);
    });

    inject(DestroyRef).onDestroy(() => {
      if (this.highlightTimer !== null) {
        clearTimeout(this.highlightTimer);
      }
      if (this.copiedTimer !== null) {
        clearTimeout(this.copiedTimer);
      }
    });
  }

  protected relative(iso: string): string {
    return relativeTime(iso, this.store.now());
  }

  protected async copy(code: string): Promise<void> {
    const ok = await this.clipboard.copy(code);
    if (!ok) {
      this.notifications.error("Couldn't copy automatically. Select the code and copy it manually.");
      return;
    }

    this.copiedCode.set(code);
    this.notifications.success(`Code ${code} copied.`);
    if (this.copiedTimer !== null) {
      clearTimeout(this.copiedTimer);
    }
    this.copiedTimer = setTimeout(() => this.copiedCode.set(null), 2000);
  }
}
