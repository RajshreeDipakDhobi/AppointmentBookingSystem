import { AppointmentItemDto } from './appointment.models';

export interface CalendarFilterParams {
  view?: 'day' | 'week' | 'month';
  date?: string; // YYYY-MM-DD
  startDate?: string;
  endDate?: string;
  providerIds?: string; // Comma-separated IDs e.g. "1,2,5"
  status?: number;
  searchQuery?: string;
  tenantId?: number;
}

export interface CalendarPermissions {
  canViewAllProviders: boolean;
  canBook: boolean;
  canManageAppointments: boolean;
  canEditAvailability: boolean;
}

export interface CalendarAppointmentItem {
  id: number;
  appointmentNumber: string;
  bookingGroupId?: string;
  serviceProviderId: number;
  serviceProviderName: string;
  serviceProviderProfilePictureUrl?: string;
  customerId?: number;
  customerName: string;
  customerPhone?: string;
  customerEmail?: string;
  serviceId: number;
  serviceName: string;
  serviceNames?: string[];
  items?: AppointmentItemDto[];
  totalServicesCount: number;
  appointmentDate: string; // YYYY-MM-DD
  startTime: string; // HH:mm
  endTime: string; // HH:mm
  durationMinutes: number;
  price: number;
  status: number;
  statusText: string;
  reason?: string;
  isMultiService: boolean;
  canManage: boolean;
  isBroadcastRequest?: boolean;
}

export interface CalendarDaySchedule {
  dayOfWeek: number;
  dayName: string;
  isAvailable: boolean;
  startTime?: string;
  endTime?: string;
  breakStartTime?: string;
  breakEndTime?: string;
  slotDurationMinutes: number;
}

export interface CalendarProviderSchedule {
  providerId: number;
  userId?: number;
  name: string;
  email?: string;
  phoneNumber?: string;
  profilePictureUrl?: string;
  specialistType?: string;
  status: number;
  serviceIds?: number[];
  serviceId?: number;

  isAvailableToday: boolean;
  workingHoursText: string;
  startTime?: string;
  endTime?: string;
  breakStartTime?: string;
  breakEndTime?: string;
  startMinutes: number;
  endMinutes: number;
  breakStartMinutes: number;
  breakEndMinutes: number;

  days?: CalendarDaySchedule[];
  appointments: CalendarAppointmentItem[];
}

export interface CalendarScheduleResponse {
  selectedDate: string;
  view: 'day' | 'week' | 'month';
  rangeStartDate: string;
  rangeEndDate: string;
  tenantId: number;
  gridStartHour: number;
  gridEndHour: number;
  providers: CalendarProviderSchedule[];
  allAppointments: CalendarAppointmentItem[];
  permissions: CalendarPermissions;
}

