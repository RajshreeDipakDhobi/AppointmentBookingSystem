export interface ProviderAvailabilityDto {
  id: number;
  serviceProviderId: number;
  serviceProviderName: string;
  dayOfWeek: number; // 0 = Sunday, 1 = Monday, ..., 6 = Saturday
  dayName: string;
  specificDate?: string | null; // "YYYY-MM-DD"
  formattedSpecificDate?: string | null;
  startTime?: string | null; // "10:00"
  endTime?: string | null;   // "17:00"
  breakStartTime?: string | null; // e.g. "13:00"
  breakEndTime?: string | null;   // e.g. "14:00"
  slotDurationMinutes: number; // e.g. 30
  blockedSlots?: string | null;
  blockedSlotList?: string[];
  isAvailable: boolean;
  totalCalculatedSlots: number;
}

export interface SaveProviderAvailabilityDto {
  dayOfWeek: number;
  startTime: string;
  endTime: string;
  breakStartTime?: string | null;
  breakEndTime?: string | null;
  slotDurationMinutes: number;
  blockedSlots?: string[];
  isAvailable: boolean;
}

export interface SaveWeeklyAvailabilityRequestDto {
  serviceProviderId: number;
  schedules: SaveProviderAvailabilityDto[];
}

export interface SaveDateSpecificAvailabilityRequestDto {
  serviceProviderId: number;
  date: string; // "YYYY-MM-DD"
  startTime?: string;
  endTime?: string;
  breakStartTime?: string | null;
  breakEndTime?: string | null;
  slotDurationMinutes?: number;
  blockedSlots?: string[];
  isAvailable?: boolean;
}

export interface TimeSlotDto {
  startTime: string;
  endTime: string;
  displayText?: string;
  isAvailable: boolean;
  isBreak?: boolean;
  isBlocked?: boolean;
  reason?: string;
}

export interface DayScheduleSummaryDto {
  dayOfWeek: number;
  dayName: string;
  shortName: string;
  isAvailable: boolean;
  startTime?: string | null;
  endTime?: string | null;
  breakStartTime?: string | null;
  breakEndTime?: string | null;
  slotDurationMinutes: number;
  blockedSlots?: string | null;
  blockedSlotList?: string[];
  totalCalculatedSlots: number;
  timeDisplay: string;
  breakDisplay: string;
}

export interface ProviderScheduleOverviewItemDto {
  providerId: number;
  providerName: string;
  specialization?: string | null;
  email?: string | null;
  phoneNumber?: string | null;
  profilePictureUrl?: string | null;
  isConfigured: boolean;
  activeDaysCount: number;
  activeDaysSummary: string;
  workingHoursSummary: string;
  breakSummary: string;
  slotDurationMinutes: number;
  totalWeeklySlots: number;
  days: DayScheduleSummaryDto[];
}

export interface ProviderAvailabilityOverviewResponseDto {
  totalProviders: number;
  configuredProvidersCount: number;
  totalWeeklySlots: number;
  activeDaysTotal: number;
  providers: ProviderScheduleOverviewItemDto[];
}
