import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'info' | 'warning';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

/** Success and error notifications shown by NotificationComponent. */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private nextId = 1;
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();

  readonly toasts = signal<Toast[]>([]);

  success(message: string): void {
    this.show('success', message, 3500);
  }

  info(message: string): void {
    this.show('info', message, 3500);
  }

  warning(message: string): void {
    this.show('warning', message, 6000);
  }

  error(message: string): void {
    this.show('error', message, 8000);
  }

  dismiss(id: number): void {
    const timer = this.timers.get(id);
    if (timer !== undefined) {
      clearTimeout(timer);
      this.timers.delete(id);
    }

    this.toasts.update((toasts) => toasts.filter((toast) => toast.id !== id));
  }

  private show(kind: ToastKind, message: string, durationMs: number): void {
    const current = this.toasts();
    const last = current[current.length - 1];
    if (last && last.kind === kind && last.message === message) {
      return; // Avoid stacking the same notification.
    }

    const id = this.nextId++;
    const kept = current.slice(-3);
    for (const dropped of current.slice(0, current.length - kept.length)) {
      this.dismiss(dropped.id);
    }

    this.toasts.set([...kept, { id, kind, message }]);
    this.timers.set(
      id,
      setTimeout(() => this.dismiss(id), durationMs),
    );
  }
}
