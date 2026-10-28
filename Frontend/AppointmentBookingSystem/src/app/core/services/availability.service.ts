import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { 
  ProviderAvailabilityDto, 
  ProviderAvailabilityOverviewResponseDto,
  SaveWeeklyAvailabilityRequestDto, 
  SaveDateSpecificAvailabilityRequestDto,
  TimeSlotDto 
} from '../models/availability.models';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class AvailabilityService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/availability`;

  getAvailabilityOverview(): Observable<ProviderAvailabilityOverviewResponseDto> {
    return this.http.get<ProviderAvailabilityOverviewResponseDto>(`${this.apiUrl}/overview`);
  }

  getProviderWeeklyAvailability(providerId: number): Observable<ProviderAvailabilityDto[]> {
    return this.http.get<ProviderAvailabilityDto[]>(`${this.apiUrl}/providers/${providerId}`);
  }

  saveWeeklyAvailability(providerId: number, dto: SaveWeeklyAvailabilityRequestDto): Observable<ProviderAvailabilityDto[]> {
    return this.http.post<ProviderAvailabilityDto[]>(`${this.apiUrl}/providers/${providerId}`, dto);
  }

  getProviderSpecificDates(providerId: number): Observable<ProviderAvailabilityDto[]> {
    return this.http.get<ProviderAvailabilityDto[]>(`${this.apiUrl}/providers/${providerId}/specific-dates`);
  }

  getAvailabilityForDate(providerId: number, date: string): Observable<ProviderAvailabilityDto> {
    return this.http.get<ProviderAvailabilityDto>(`${this.apiUrl}/providers/${providerId}/date-schedule?date=${date}`);
  }

  saveDateSpecificAvailability(providerId: number, dto: SaveDateSpecificAvailabilityRequestDto): Observable<ProviderAvailabilityDto> {
    return this.http.post<ProviderAvailabilityDto>(`${this.apiUrl}/providers/${providerId}/date-schedule`, dto);
  }

  getPreviewSlots(
    startTime = '10:00', 
    endTime = '17:00', 
    slotDurationMinutes = 30,
    breakStartTime?: string | null,
    breakEndTime?: string | null,
    blockedSlots?: string[] | null
  ): Observable<TimeSlotDto[]> {
    let url = `${this.apiUrl}/preview-slots?startTime=${startTime}&endTime=${endTime}&slotDurationMinutes=${slotDurationMinutes}`;
    if (breakStartTime) url += `&breakStartTime=${breakStartTime}`;
    if (breakEndTime) url += `&breakEndTime=${breakEndTime}`;
    if (blockedSlots && blockedSlots.length > 0) {
      url += `&blockedSlots=${encodeURIComponent(blockedSlots.join(','))}`;
    }
    return this.http.get<TimeSlotDto[]>(url);
  }

  deleteAvailability(id: number): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }
}

