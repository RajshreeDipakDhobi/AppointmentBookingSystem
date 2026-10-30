import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ServiceDto, CreateServiceRequestDto, UpdateServiceRequestDto } from '../models/service.models';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class ServiceCatalogService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/services`;

  getAllServices(tenantId?: number): Observable<ServiceDto[]> {
    let params = new HttpParams();
    if (tenantId) params = params.set('tenantId', tenantId);
    return this.http.get<ServiceDto[]>(this.apiUrl, { params });
  }

  getActiveServices(tenantId?: number): Observable<ServiceDto[]> {
    let params = new HttpParams();
    if (tenantId) params = params.set('tenantId', tenantId);
    return this.http.get<ServiceDto[]>(`${this.apiUrl}/active`, { params });
  }

  getServiceById(id: number): Observable<ServiceDto> {
    return this.http.get<ServiceDto>(`${this.apiUrl}/${id}`);
  }

  createService(dto: CreateServiceRequestDto): Observable<ServiceDto> {
    return this.http.post<ServiceDto>(this.apiUrl, dto);
  }

  updateService(id: number, dto: UpdateServiceRequestDto): Observable<ServiceDto> {
    return this.http.put<ServiceDto>(`${this.apiUrl}/${id}`, dto);
  }

  toggleServiceStatus(id: number): Observable<{ id: number; message: string }> {
    return this.http.patch<{ id: number; message: string }>(`${this.apiUrl}/${id}/toggle-status`, {});
  }

  deleteService(id: number): Observable<{ id: number; message: string }> {
    return this.http.delete<{ id: number; message: string }>(`${this.apiUrl}/${id}`);
  }
}

