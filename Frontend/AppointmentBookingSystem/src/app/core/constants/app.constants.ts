import { AppointmentStatus, AppointmentDto } from '../models/appointment.models';

export interface StatusConfig {
  label: string;
  badgeClass: string;
  dotColor: string;
}

export const APPOINTMENT_STATUS_MAP: Record<AppointmentStatus, StatusConfig> = {
  [AppointmentStatus.Booked]: {
    label: 'Booked',
    badgeClass: 'badge-booked',
    dotColor: '#818cf8'
  },
  [AppointmentStatus.Completed]: {
    label: 'Completed',
    badgeClass: 'badge-completed',
    dotColor: '#34d399'
  },
  [AppointmentStatus.Cancelled]: {
    label: 'Cancelled',
    badgeClass: 'badge-cancelled',
    dotColor: '#fb7185'
  },
  [AppointmentStatus.Rescheduled]: {
    label: 'Rescheduled',
    badgeClass: 'badge-rescheduled',
    dotColor: '#fbbf24'
  },
  [AppointmentStatus.Approved]: {
    label: 'Confirmed',
    badgeClass: 'badge-approved',
    dotColor: '#10b981'
  },
  [AppointmentStatus.Rejected]: {
    label: 'Rejected',
    badgeClass: 'badge-rejected',
    dotColor: '#ef4444'
  },
  [AppointmentStatus.RescheduleProposed]: {
    label: 'Reschedule Proposed',
    badgeClass: 'badge-reschedule-proposed',
    dotColor: '#f59e0b'
  }
};

export const AVATAR_GRADIENTS: string[] = [
  'linear-gradient(135deg, #6366f1 0%, #a855f7 100%)',
  'linear-gradient(135deg, #06b6d4 0%, #3b82f6 100%)',
  'linear-gradient(135deg, #ec4899 0%, #f43f5e 100%)',
  'linear-gradient(135deg, #10b981 0%, #059669 100%)',
  'linear-gradient(135deg, #f59e0b 0%, #d97706 100%)',
  'linear-gradient(135deg, #8b5cf6 0%, #ec4899 100%)'
];

export function getAvatarGradient(name?: string | null): string {
  const str = name?.trim() || 'Provider';
  let hash = 0;
  for (let i = 0; i < str.length; i++) {
    hash = str.charCodeAt(i) + ((hash << 5) - hash);
  }
  const index = Math.abs(hash) % AVATAR_GRADIENTS.length;
  return AVATAR_GRADIENTS[index];
}

export function getInitials(name?: string | null): string {
  if (!name || !name.trim()) return 'SP';
  const parts = name.trim().split(/\s+/);
  if (parts.length >= 2) {
    return (parts[0][0] + parts[1][0]).toUpperCase();
  }
  return name.trim().slice(0, 2).toUpperCase();
}

// Universal Email & 10-Digit Mobile Validation Constants & Helpers
export const EMAIL_REGEX = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;
export const PHONE_10_DIGIT_REGEX = /^\d{10}$/;

export function isValidEmail(email?: string | null): boolean {
  if (!email || !email.trim()) return false;
  return EMAIL_REGEX.test(email.trim());
}

export function isValidPhone(phone?: string | null): boolean {
  if (!phone) return false;
  const digits = phone.toString().replace(/\D/g, '');
  return digits.length === 10;
}

export function filter10Digits(val?: string | null): string {
  if (!val) return '';
  return val.toString().replace(/\D/g, '').slice(0, 10);
}

/**
 * Accurately determines if an appointment's scheduled start time has passed,
 * matching backend C# logic:
 * appointment.AppointmentDate.Date < now.Date || 
 * (appointment.AppointmentDate.Date == now.Date && appointment.StartTime <= now.TimeOfDay)
 */
export function isAppointmentPast(apt: { appointmentDate?: string | Date; startTime?: string } | null | undefined): boolean {
  if (!apt || !apt.appointmentDate) return false;

  const rawDate = apt.appointmentDate;
  let year = 0;
  let month = 0;
  let day = 0;

  if (typeof rawDate === 'string') {
    const datePart = rawDate.includes('T') ? rawDate.split('T')[0] : rawDate;
    const parts = datePart.split('-').map(Number);
    year = parts[0];
    month = parts[1];
    day = parts[2];
  } else if (rawDate instanceof Date) {
    year = rawDate.getFullYear();
    month = rawDate.getMonth() + 1;
    day = rawDate.getDate();
  }

  if (!year || !month || !day) return false;

  const now = new Date();
  const currentYear = now.getFullYear();
  const currentMonth = now.getMonth() + 1;
  const currentDay = now.getDate();

  // 1. Check Date (strictly earlier year, month, or day)
  if (year < currentYear) return true;
  if (year > currentYear) return false;

  if (month < currentMonth) return true;
  if (month > currentMonth) return false;

  if (day < currentDay) return true;
  if (day > currentDay) return false;

  // 2. Date is TODAY: compare StartTime with current time
  if (apt.startTime) {
    const timeParts = apt.startTime.split(':').map(Number);
    const startHour = timeParts[0] ?? 0;
    const startMinute = timeParts[1] ?? 0;
    const startSecond = timeParts[2] ?? 0;

    const currentHour = now.getHours();
    const currentMinute = now.getMinutes();
    const currentSecond = now.getSeconds();

    if (startHour < currentHour) return true;
    if (startHour > currentHour) return false;

    if (startMinute < currentMinute) return true;
    if (startMinute > currentMinute) return false;

    if (startSecond <= currentSecond) return true;
  }

  return false;
}

/**
 * Reschedule Policy: Appointments can only be rescheduled at least 1 hour prior
 * to their scheduled start time, and only if active (Booked or Approved).
 */
export function canRescheduleAppointment(apt: AppointmentDto | null | undefined): boolean {
  if (!apt) return false;
  if (apt.status !== AppointmentStatus.Booked && apt.status !== AppointmentStatus.Approved) {
    return false;
  }

  const rawDate: any = apt.appointmentDate;
  let year = 0, month = 0, day = 0;
  if (typeof rawDate === 'string') {
    const datePart = rawDate.includes('T') ? rawDate.split('T')[0] : rawDate;
    const parts = datePart.split('-').map(Number);
    year = parts[0];
    month = parts[1];
    day = parts[2];
  } else if (rawDate instanceof Date) {
    year = rawDate.getFullYear();
    month = rawDate.getMonth() + 1;
    day = rawDate.getDate();
  }
  if (!year || !month || !day) return false;

  let startHour = 0, startMinute = 0;
  if (apt.startTime) {
    const timeParts = apt.startTime.split(':').map(Number);
    startHour = timeParts[0] ?? 0;
    startMinute = timeParts[1] ?? 0;
  }

  const appointmentStart = new Date(year, month - 1, day, startHour, startMinute, 0);
  const now = new Date();

  // Reschedule threshold: now must be strictly before 1 hour of start time
  const oneHourBeforeStart = new Date(appointmentStart.getTime() - 60 * 60 * 1000);
  return now.getTime() < oneHourBeforeStart.getTime();
}

/**
 * Checks if an appointment can be modified/rescheduled.
 */
export function canModifyAppointment(apt: AppointmentDto | null | undefined): boolean {
  return canRescheduleAppointment(apt);
}

/**
 * Cancellation Policy: Appointments can only be cancelled at least 1 hour prior
 * to their scheduled start time.
 */
export function canCancelAppointment(apt: AppointmentDto | null | undefined): boolean {
  if (!apt) return false;
  if (apt.status === AppointmentStatus.Completed || apt.status === AppointmentStatus.Cancelled) {
    return false;
  }

  const rawDate: any = apt.appointmentDate;
  let year = 0, month = 0, day = 0;
  if (typeof rawDate === 'string') {
    const datePart = rawDate.includes('T') ? rawDate.split('T')[0] : rawDate;
    const parts = datePart.split('-').map(Number);
    year = parts[0];
    month = parts[1];
    day = parts[2];
  } else if (rawDate instanceof Date) {
    year = rawDate.getFullYear();
    month = rawDate.getMonth() + 1;
    day = rawDate.getDate();
  }
  if (!year || !month || !day) return false;

  let startHour = 0, startMinute = 0;
  if (apt.startTime) {
    const timeParts = apt.startTime.split(':').map(Number);
    startHour = timeParts[0] ?? 0;
    startMinute = timeParts[1] ?? 0;
  }

  const appointmentStart = new Date(year, month - 1, day, startHour, startMinute, 0);
  const now = new Date();

  // Cancellation threshold: now must be strictly before 1 hour of start time
  const oneHourBeforeStart = new Date(appointmentStart.getTime() - 60 * 60 * 1000);
  return now.getTime() < oneHourBeforeStart.getTime();
}
