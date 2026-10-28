import { Pipe, PipeTransform } from '@angular/core';
import { formatDatePretty } from '../utils/formatters';

@Pipe({
  name: 'appDate',
  standalone: true
})
export class AppDatePipe implements PipeTransform {
  transform(value: string | Date | null | undefined): string {
    if (!value) return '—';
    if (value instanceof Date) {
      return formatDatePretty(value.toISOString());
    }
    return formatDatePretty(value);
  }
}
