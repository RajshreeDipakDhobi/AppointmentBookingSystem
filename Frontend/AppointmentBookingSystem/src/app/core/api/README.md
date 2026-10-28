# Generated API Clients

This directory contains the auto-generated Angular TypeScript API services generated via NSwag from the ASP.NET Core backend OpenAPI spec.

## How to generate/update API client manually:
1. Ensure the Backend API is running:
   ```bash
   dotnet run --project Backend/AppointmentBookingSystem/AppointmentBooking.API
   ```
   (Verify `http://localhost:5077/openapi/v1.json` is accessible in browser)

2. In the `FrontEnd` directory, run:
   ```bash
   npm run generate:api
   ```

3. NSwag will generate `api-client.ts` in this folder with:
   - Typed DTO Interfaces
   - Angular `@Injectable()` Client classes (`AuthClient`, etc.)
   - Full Cookie/Session credentials support (`withCredentials: true`)
