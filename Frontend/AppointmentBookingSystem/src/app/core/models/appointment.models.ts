export enum AppointmentStatus {
  Pending = 1,
  Booked = 1,
  Completed = 2,
  Cancelled = 3,
  Rescheduled = 4,
  Approved = 5,
  Rejected = 6,
  RescheduleProposed = 7
}

export interface AvailableSlotDto {
  startTime: string; // "10:00"
  endTime: string;   // "10:30"
  displayText: string; // "10:00 AM – 10:30 AM"
  isAvailable: boolean;
  status: 'Available' | 'Booked' | 'Past' | 'Break' | string;
  reason?: string;
}

export interface ProviderDailyAvailabilityResponseDto {
  serviceProviderId: number;
  serviceProviderName: string;
  serviceType: string;
  date: string;
  dayName: string;
  isWorkingDay: boolean;
  workingHours: string;
  slotDurationMinutes: number;
  slots: AvailableSlotDto[];
}

export interface AppointmentDto {
  id: number;
  tenantId: number;
  appointmentNumber: string;
  serviceProviderId: number | null;
  serviceProviderName: string;
  serviceProviderType: string;
  serviceProviderProfilePictureUrl?: string;
  serviceId?: number;
  serviceName?: string;
  servicePrice?: number;
  serviceDurationMinutes?: number;
  isBroadcast?: boolean;
  customerId?: number;
  customerName: string;
  customerEmail: string;
  customerPhone: string;
  customerAddress?: string;
  reason?: string;
  appointmentDate: string;
  formattedDate: string;
  startTime: string;
  endTime: string;
  timeSlotText: string;
  status: AppointmentStatus;
  statusText: string;
  approvedAt?: string;
  rejectedAt?: string;
  rejectionReason?: string;
  cancellationReason?: string;
  cancelledByName?: string;
  cleanCancellationReason?: string;
  cancelledAt?: string;
  rescheduledFromAppointmentId?: number;
  proposedDate?: string;
  proposedDateText?: string;
  proposedStartTime?: string;
  proposedEndTime?: string;
  proposedTimeSlotText?: string;
  rescheduleNotes?: string;
  createdAt: string;
}

export interface BookAppointmentRequestDto {
  serviceProviderId?: number | null;
  serviceId?: number | null;
  appointmentDate: string; // "YYYY-MM-DD"
  startTime: string;       // "10:00"
  endTime: string;         // "10:30"
  customerName: string;
  customerEmail: string;
  customerPhone: string;
  customerAddress: string;
  reason?: string;
}

export interface RescheduleAppointmentRequestDto {
  newDate: string;     // "YYYY-MM-DD"
  newStartTime: string; // "10:00"
  newEndTime: string;   // "10:30"
  reason?: string;
  customerContact?: string;
}

export interface CancelAppointmentRequestDto {
  cancellationReason?: string;
  customerContact?: string;
}

export interface UpdateAppointmentStatusDto {
  status: AppointmentStatus;
  notes?: string;
  proposedDate?: string;
  proposedStartTime?: string;
  proposedEndTime?: string;
}

export interface AppointmentStatsDto {
  totalAppointments: number;
  todayAppointments: number;
  upcomingAppointments: number;
  completedAppointments: number;
  cancelledAppointments: number;
}
