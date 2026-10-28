import { Pipe, PipeTransform } from '@angular/core';
import { AppointmentStatus } from '../../core/models/appointment.models';
import { APPOINTMENT_STATUS_MAP } from '../../core/constants/app.constants';

@Pipe({
  name: 'appStatusBadge',
  standalone: true
})
export class AppStatusBadgePipe implements PipeTransform {
  transform(status: AppointmentStatus | number | null | undefined): string {
    if (status === null || status === undefined) return 'badge-secondary';
    const config = APPOINTMENT_STATUS_MAP[status as AppointmentStatus];
    return config ? config.badgeClass : 'badge-secondary';
  }
}
