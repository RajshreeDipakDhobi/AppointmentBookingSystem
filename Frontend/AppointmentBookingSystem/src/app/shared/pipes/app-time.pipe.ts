import { Pipe, PipeTransform } from '@angular/core';
import { formatTo12Hour } from '../utils/formatters';

@Pipe({
  name: 'appTime',
  standalone: true
})
export class AppTimePipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    if (!value) return '—';
    return formatTo12Hour(value);
  }
}
