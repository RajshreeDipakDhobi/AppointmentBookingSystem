import { Directive, EventEmitter, HostListener, Input, Output } from '@angular/core';

@Directive({
  selector: '[appCopy]',
  standalone: true
})
export class CopyToClipboardDirective {
  @Input('appCopy') textToCopy: string = '';
  @Output() copied = new EventEmitter<string>();

  @HostListener('click', ['$event'])
  onClick(event: MouseEvent): void {
    event.stopPropagation();
    if (!this.textToCopy) return;

    if (navigator?.clipboard?.writeText) {
      navigator.clipboard.writeText(this.textToCopy)
        .then(() => this.onSuccess())
        .catch(() => this.fallbackCopy());
    } else {
      this.fallbackCopy();
    }
  }

  private fallbackCopy(): void {
    try {
      const el = document.createElement('textarea');
      el.value = this.textToCopy;
      el.setAttribute('readonly', '');
      el.style.position = 'fixed';
      el.style.left = '-9999px';
      document.body.appendChild(el);
      el.select();
      document.execCommand('copy');
      document.body.removeChild(el);
      this.onSuccess();
    } catch {
      // ignore
    }
  }

  private onSuccess(): void {
    this.copied.emit(this.textToCopy);
  }
}
