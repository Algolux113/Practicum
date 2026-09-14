# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All commands run from the repository root (`Practicum.slnx` ties both projects together, so `dotnet` picks them up automatically).

- **Build:** `dotnet build`
- **Run the API:** `dotnet run --launch-profile https --project PracticumApi`
  - HTTPS: `https://localhost:7008`, HTTP: `http://localhost:5267`
  - Swagger UI (Development only): `https://localhost:7008/swagger/`, OpenAPI doc: `/openapi/v1.json`
- **Run all tests:** `dotnet test`
- **Run one test:** `dotnet test --filter "FullyQualifiedName~EventServiceIntegrationTests.Get_WithInvalidId_ShouldThrowNotFoundException"`
- **Run one test class:** `dotnet test --filter "FullyQualifiedName~BookingServiceIntegrationTests"`

Target framework is `net10.0` (SDK 10.x). `PracticumApi.http` contains sample requests for editor-based HTTP clients.

## Architecture

ASP.NET Core Web API (`PracticumApi`) with an xUnit test project (`PracticumTests`). Two domains: **Events** and **Bookings**. There is **no database** — both services keep data in an in-memory `List<T>` and are registered as singletons in [Program.cs](PracticumApi/Program.cs). Data does not survive a restart.

### Request → service → exception → middleware flow

Controllers are thin and contain no error handling. Business rules live in the services, which throw domain exceptions from [`PracticumApi/Exceptions/`](PracticumApi/Exceptions): `NotFoundException`, `ValidationException`, and `NoAvailableSeatsException`. [`GlobalExceptionHandlingMiddleware`](PracticumApi/Middlewares/GlobalExceptionHandlingMiddleware.cs) is registered **first** in the pipeline, catches everything, logs it, and maps it to an RFC 7807 `ProblemDetails` JSON response via its `MapStatusCode` switch (`NotFoundException` → 404, `ValidationException` → 400, `NoAvailableSeatsException` → 409, else → 500).

**To add a new error type:** create the exception in `Exceptions/`, throw it from a service, and add a case to `MapStatusCode`. See [EXCEPTION_HANDLING.md](EXCEPTION_HANDLING.md) for a full walkthrough.

### Validation happens in two layers

- **Model binding:** [`EventDTO`](PracticumApi/Models/EventDTO.cs) implements `IValidatableObject` (`EndAt > StartAt`, `[Required]` fields). Failures go through a custom `InvalidModelStateResponseFactory` in [Program.cs](PracticumApi/Program.cs) that returns the **same** `ProblemDetails` shape as the middleware (`title` "Validation error", 400, joined messages in `detail`).
- **Service logic:** [`EventService.GetAll`](PracticumApi/Services/EventService.cs) validates pagination (`page >= 1`, `1 <= pageSize <= 100`) and throws `ValidationException`, which the middleware turns into a 400.

`AddJsonOptions` registers `JsonStringEnumConverter`, so `BookingStatus` serializes as `"Pending"` / `"Confirmed"` / `"Rejected"`, not integers.

### Seat capacity

- [`Event`](PracticumApi/Models/Event.cs) tracks `TotalSeats` and `AvailableSeats`. Events are built via the static factory `Event.Create(...)`, which throws `ValidationException` if `totalSeats <= 0`; `AvailableSeats` starts equal to `TotalSeats`.
- `Event.TryReserveSeats(count = 1)` / `Event.ReleaseSeats(count = 1)` mutate `AvailableSeats` on the entity itself (the latter clamps to `TotalSeats`). `Event.RecalculateAvailableSeats(newTotalSeats)` is the pure helper `EventController.Update` uses so changing `TotalSeats` can't push `AvailableSeats` out of `[0, TotalSeats]`.
- [`IEventService.ReserveSeats`/`ReleaseSeats`](PracticumApi/Interfaces/IEventService.cs) are the **atomic** entry points: [`EventService`](PracticumApi/Services/EventService.cs) does the lookup and the seat mutation under the same `_sync` lock, so concurrent reservations can't both pass the seat check and oversell the event. `ReserveSeats` throws `NotFoundException` if the event is missing and `NoAvailableSeatsException` if `count > AvailableSeats`.

### Bookings and background processing

- A booking is created via **`POST /events/{id}/book`**, handled by [`BookingController`](PracticumApi/Controllers/BookingController.cs) (via an absolute `[HttpPost("/events/{id:guid}/book")]` route, so the URL stays under `/events`), *not* on `EventController`. `BookingController` also exposes `GET /bookings/{id}`.
- [`BookingService.CreateBookingAsync`](PracticumApi/Services/BookingService.cs) calls `IEventService.ReserveSeats(eventId)` first — this both checks the event exists (404 if not) and atomically reserves a seat (409 `NoAvailableSeatsException` if none left). New bookings start as `Pending` (see [`BookingStatus`](PracticumApi/Models/BookingStatus.cs)). [`Booking.Confirm()`/`Booking.Reject()`](PracticumApi/Models/Booking.cs) set `Status` and `ProcessedAt`.
- [`BookingProcessingService`](PracticumApi/Services/BookingProcessingService.cs) is a `BackgroundService` (registered with `AddHostedService`). Every **5 seconds** it scans for `Pending` bookings and processes them **concurrently** (`Task.WhenAll`), each waiting **2 seconds** to simulate an external system before writing. A `SemaphoreSlim(1, 1)` serializes the write step so only one task updates storage at a time. Per booking: if the event no longer exists, the booking is rejected and a `Warning` is logged; otherwise it's confirmed; any other exception rejects the booking, releases its seat via `IEventService.ReleaseSeats`, and logs an `Error`. It creates its own DI scope via `IServiceScopeFactory`, honors `OperationCanceledException`, and never lets an unexpected exception kill the loop.

### Tests

Integration tests in `PracticumTests` use **real service instances, no mocks** (despite `Moq` being referenced). `BookingService` takes `IEventService` in its constructor, so booking tests construct both services together. Tests follow Arrange/Act/Assert and are split with `#region Успешные сценарии` / `#region Неуспешные сценарии` (and, in `BookingServiceIntegrationTests`, a third `#region Конкурентность` for seat-overbooking races).

`EventServiceIntegrationTests` is one `partial class` spread across several files, grouped by behavior under test — add a new `EventService` test to the file that matches its concern:

- [EventServiceIntegrationTests.cs](PracticumTests/EventServiceIntegrationTests.cs) — the shared `CreateEventService()` helper only
- [EventServiceCrudTests.cs](PracticumTests/EventServiceCrudTests.cs) — `Add` / `Get` / `Update` / `Delete` and their `NotFoundException` cases
- [EventServiceFilteringTests.cs](PracticumTests/EventServiceFilteringTests.cs) — `GetAll` filtering by title and date range
- [EventServicePaginationTests.cs](PracticumTests/EventServicePaginationTests.cs) — `GetAll` paging and `page` / `pageSize` validation
- [EventServiceDateHandlingTests.cs](PracticumTests/EventServiceDateHandlingTests.cs) — events with `EndAt < StartAt` still persist (no service-level date validation)

`BookingServiceIntegrationTests` remains a single file. Its `CreateTestEvent(eventService, totalSeats = 100)` helper builds events via `Event.Create`, so tests can dial in capacity (e.g. `totalSeats: 1`) to exercise `NoAvailableSeatsException` and overbooking races.

## Conventions

- **Russian** is used for commit messages, code comments, XML doc summaries, and exception messages (which become the `detail` field of API error responses). Match this when editing existing code.
- Services use C# primary constructors and collection expressions (`[]`).
- `bin/` and `obj/` are the only gitignored paths.
