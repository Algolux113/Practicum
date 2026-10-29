# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All commands run from the repository root (`Practicum.slnx` includes the API and both test projects).

- **Build:** `dotnet build`
- **Run the API:** `dotnet run --launch-profile https --project Presentation`
  - HTTPS: `https://localhost:7008`, HTTP: `http://localhost:5267`
  - Swagger UI (Development only): `https://localhost:7008/swagger/`, OpenAPI doc: `/openapi/v1.json`
- **Run service tests (EF Core InMemory):** `dotnet test Tests`
- **Run PostgreSQL integration tests:** `dotnet test IntegrationTests` (requires Docker)
- **Run all tests:** `dotnet test` (requires Docker for the Testcontainers suite)
- **Run one test:** `dotnet test --filter "FullyQualifiedName~EventServiceIntegrationTests.Get_WithInvalidId_ShouldThrowNotFoundException"`
- **Run one test class:** `dotnet test --filter "FullyQualifiedName~BookingServiceIntegrationTests"`
- **Add a migration:** `dotnet ef migrations add <MigrationName> --project Infrastructure --startup-project Presentation`

Target framework is `net10.0` (SDK 10.x). `Presentation.http` contains sample requests for editor-based HTTP clients.

## Architecture

ASP.NET Core Web API (`Presentation`) with PostgreSQL persistence through EF Core. `AppDbContext` maps the `Event` and `Booking` entities to `events` and `bookings`; `bookings.event_id` references `events.id`. The initial migration is in `Infrastructure/Migrations`, and `Program.cs` applies pending migrations with `Migrate()` at startup. The PostgreSQL server and target database must exist before starting the API.

`IEventRepository` and `IBookingRepository` encapsulate EF Core data access. Their implementations live in `Infrastructure/DataAccess/Repositories`; register both as **scoped**. Services own domain behavior and use repositories rather than accessing `AppDbContext`/`DbSet` directly. Both repositories in one request scope share the same scoped context, allowing booking creation and seat reservation to be saved together.

### Request → service → exception → middleware flow

Controllers are thin and contain no error handling. Business rules live in the services, which throw domain exceptions from [`Domain/Exceptions/`](Domain/Exceptions): `NotFoundException`, `ValidationException`, and `NoAvailableSeatsException`. [`GlobalExceptionHandlingMiddleware`](Presentation/Middlewares/GlobalExceptionHandlingMiddleware.cs) is registered **first** in the pipeline, catches everything, logs it, and maps it to an RFC 7807 `ProblemDetails` JSON response via its `MapStatusCode` switch (`NotFoundException` → 404, `ValidationException` → 400, `NoAvailableSeatsException` → 409, else → 500).

**To add a new error type:** create the exception in `Exceptions/`, throw it from a service, and add a case to `MapStatusCode`. See [EXCEPTION_HANDLING.md](EXCEPTION_HANDLING.md) for a full walkthrough.

### Validation happens in two layers

- **Model binding:** [`EventDTO`](Application/DTOs/EventDTO.cs) implements `IValidatableObject` (`EndAt > StartAt`, `[Required]` fields). Failures go through a custom `InvalidModelStateResponseFactory` in [Program.cs](Presentation/Program.cs) that returns the **same** `ProblemDetails` shape as the middleware (`title` "Validation error", 400, joined messages in `detail`).
- **Service logic:** [`EventService.GetAll`](Application/Services/EventService.cs) validates pagination (`page >= 1`, `1 <= pageSize <= 100`) and throws `ValidationException`, which the middleware turns into a 400.

`AddJsonOptions` registers `JsonStringEnumConverter`, so `BookingStatus` serializes as `"Pending"` / `"Confirmed"` / `"Rejected"`, not integers.

### Seat capacity

- [`Event`](Domain/Models/Event.cs) tracks `TotalSeats` and `AvailableSeats`. Events are built via the static factory `Event.Create(...)`, which throws `ValidationException` if `totalSeats <= 0`; `AvailableSeats` starts equal to `TotalSeats`.
- `Event.TryReserveSeats(count = 1)` / `Event.ReleaseSeats(count = 1)` mutate `AvailableSeats` on the entity itself (the latter clamps to `TotalSeats`). `Event.Update(...)` owns changes to event details and capacity: it preserves the number of already-taken seats and throws `ValidationException` (400) if `newTotalSeats` is below that count, so shrinking capacity can't silently leave more `Pending` bookings than the event now has room for (`BookingProcessingService` would otherwise confirm them all and oversell the event).
- `Event.TryReserveSeats` / `ReleaseSeats` mutate seat counts on the entity; `Event.Update(...)` preserves taken seats and rejects capacity below the occupied count.
- [`UnitOfWork`](Infrastructure/DataAccess/UnitOfWork.cs) uses shadow UUID concurrency tokens on events and bookings. PostgreSQL checks versions on UPDATE; conflicts clear pending state and retry the whole operation up to 20 times, honoring cancellation. Exhausted retries become HTTP 409. No process-wide semaphore is used. Callbacks must not perform external side effects.

### Bookings and background processing

- A booking is created via **`POST /events/{id}/book`**, handled by [`BookingController`](Presentation/Controllers/BookingController.cs) (via an absolute `[HttpPost("/events/{id:guid}/book")]` route, so the URL stays under `/events`), *not* on `EventController`. `BookingController` also exposes `GET /bookings/{id}`.
- Every action in `BookingController` and [`EventController`](Presentation/Controllers/EventController.cs) carries `[ProducesResponseType]` for each status code it can actually return (success + the domain exceptions it can throw, e.g. `Book` documents 202/404/409). The built-in `Microsoft.AspNetCore.OpenApi` generator has no way to know about responses thrown from `GlobalExceptionHandlingMiddleware` or returned via a non-default helper like `Accepted(...)`/`NoContent()`, so without the explicit attribute a status code silently won't show up in `/openapi/v1.json` / Swagger UI (and a wrong default like "200 OK" can appear even where the real code is 202/201/204). Keep this in sync when adding a new action or a new exception path.
- [`BookingService.CreateBookingAsync`](Application/Services/BookingService.cs) loads the event for update through `IEventRepository`, reserves a seat and adds the booking through `IBookingRepository` inside `IUnitOfWork.ExecuteAsync`; Infrastructure owns saving and failure cleanup. New bookings start as `Pending`; `Booking.Confirm()` / `Booking.Reject()` set `Status` and `ProcessedAt`.
- [`BookingProcessingService`](Infrastructure/Hosting/BookingProcessingService.cs) is a `BackgroundService`. It uses `IServiceScopeFactory` to get `IBookingRepository` for each poll and a separate scoped `IBookingService` for each booking processed concurrently. On failure, recovery uses another fresh scope and rejects the booking, returning the seat. The loop honors cancellation and logs unexpected errors without stopping.

### Tests

`Tests` checks services using EF Core InMemory and isolated DI registrations. `IntegrationTests` uses Testcontainers PostgreSQL 16 (Docker required) to verify migrations and every repository method. Both follow Arrange/Act/Assert. Service tests are grouped by behavior; repository tests are in `EventRepositoryIntegrationTests` and `BookingRepositoryIntegrationTests`, and migration checks are in `MigrationIntegrationTests`.

`EventServiceIntegrationTests` is one `partial class` spread across several files, grouped by behavior under test — add a new `EventService` test to the file that matches its concern:

- [EventServiceIntegrationTests.cs](Tests/EventServiceIntegrationTests.cs) — the shared `CreateEventService()` helper only
- [EventServiceCrudTests.cs](Tests/EventServiceCrudTests.cs) — `Add` / `Get` / `Update` / `Delete` and their `NotFoundException` cases
- [EventServiceFilteringTests.cs](Tests/EventServiceFilteringTests.cs) — `GetAll` filtering by title and date range
- [EventServicePaginationTests.cs](Tests/EventServicePaginationTests.cs) — `GetAll` paging and `page` / `pageSize` validation
- [EventServiceDateHandlingTests.cs](Tests/EventServiceDateHandlingTests.cs) — events with `EndAt < StartAt` still persist (no service-level date validation)

`BookingServiceIntegrationTests` covers sequential service behavior with InMemory. Concurrency and transaction rollback are tested against PostgreSQL in `IntegrationTests/BookingConcurrencyTests.cs`, including 30 requests across two independent DI providers and stale-version rejection. InMemory does not provide transactional rollback for partial writes.

## Conventions

- **Russian** is used for commit messages, code comments, XML doc summaries, and exception messages (which become the `detail` field of API error responses). Match this when editing existing code.
- Services use C# primary constructors and collection expressions (`[]`).
- `bin/` and `obj/` are the only gitignored paths.
