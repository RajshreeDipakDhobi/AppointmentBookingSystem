import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'appPhone',
  standalone: true
})
export class AppPhonePipe implements PipeTransform {
  transform(value?: string | null): string {
    if (!value || !value.trim()) return '-';

    const digits = value.replace(/\D/g, '');
    if (digits.length === 10) {
      return `+91 ${digits.slice(0, 5)} ${digits.slice(5)}`;
    }
    if (digits.length === 12 && digits.startsWith('91')) {
      const ten = digits.slice(2);
      return `+91 ${ten.slice(0, 5)} ${ten.slice(5)}`;
    }

    return value;
  }
}
