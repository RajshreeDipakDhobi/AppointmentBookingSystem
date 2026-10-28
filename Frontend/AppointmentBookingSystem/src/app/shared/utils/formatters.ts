/**
 * Reusable formatters & helper functions across both Customer & Admin modules.
 * Single source of truth — No duplicate implementations!
 */

export function getInitials(name: string): string {
  if (!name) return '??';
  const parts = name.trim().split(/\s+/);
  if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

export function formatTo12Hour(time24: string): string {
  if (!time24) return '';
  const [hoursStr, minutesStr] = time24.split(':');
  let hours = parseInt(hoursStr, 10);
  const minutes = minutesStr ? minutesStr.substring(0, 2) : '00';
  if (isNaN(hours)) return time24;
  const ampm = hours >= 12 ? 'PM' : 'AM';
  hours = hours % 12;
  hours = hours ? hours : 12;
  return `${hours.toString().padStart(2, '0')}:${minutes} ${ampm}`;
}

export function formatDatePretty(dateInput: string | Date): string {
  if (!dateInput) return '';
  const d = new Date(dateInput);
  if (isNaN(d.getTime())) return String(dateInput);
  return d.toLocaleDateString('en-US', {
    weekday: 'short',
    year: 'numeric',
    month: 'short',
    day: 'numeric'
  });
}

export function getStatusBadgeClass(status: string | number): string {
  const s = String(status).toLowerCase();
  if (s.includes('book') || s === '1' || s.includes('confirm')) return 'badge-confirmed';
  if (s.includes('complete') || s === '2') return 'badge-completed';
  if (s.includes('cancel') || s === '3') return 'badge-cancelled';
  if (s.includes('resched') || s === '4') return 'badge-rescheduled';
  return 'badge-neutral';
}
