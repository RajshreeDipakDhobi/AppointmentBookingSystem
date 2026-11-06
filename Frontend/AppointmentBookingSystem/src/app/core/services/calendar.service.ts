import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { environment } from '../../../environments/environment';
import { CalendarFilterParams, CalendarScheduleResponse } from '../models/calendar.models';
import { ApiResponse } from '../models/api-response.model';

@Injectable({
  providedIn: 'root'
})
export class CalendarService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/calendar`;

  /**
   * Retrieves the independent unified calendar schedule feed from the backend.
   */
  getCalendarSchedule(filter: CalendarFilterParams): Observable<CalendarScheduleResponse> {
    let params = new HttpParams();

    if (filter.view) params = params.set('view', filter.view);
    if (filter.date) params = params.set('date', filter.date);
    if (filter.startDate) params = params.set('startDate', filter.startDate);
    if (filter.endDate) params = params.set('endDate', filter.endDate);
    if (filter.providerIds) params = params.set('providerIds', filter.providerIds);
    if (filter.status !== undefined && filter.status !== null) params = params.set('status', filter.status.toString());
    if (filter.searchQuery?.trim()) params = params.set('searchQuery', filter.searchQuery.trim());
    if (filter.tenantId) params = params.set('tenantId', filter.tenantId.toString());

    return this.http.get<ApiResponse<CalendarScheduleResponse>>(`${this.apiUrl}/schedule`, { params })
      .pipe(
        map(res => {
          if (res && res.data) {
            return res.data;
          }
          return res as unknown as CalendarScheduleResponse;
        })
      );
  }
}

