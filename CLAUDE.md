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
- Every action in `BookingController` and [`EventController`](PracticumApi/Controllers/EventController.cs) carries `[ProducesResponseType]` for each status code it can actually return (success + the domain exceptions it can throw, e.g. `Book` documents 202/404/409). The built-in `Microsoft.AspNetCore.OpenApi` generator has no way to know about responses thrown from `GlobalExceptionHandlingMiddleware` or returned via a non-default helper like `Accepted(...)`/`NoContent()`, so without the explicit attribute a status code silently won't show up in `/openapi/v1.json` / Swagger UI (and a wrong default like "200 OK" can appear even where the real code is 202/201/204). Keep this in sync when adding a new action or a new exception path.
- [`BookingService.CreateBookingAsync`](PracticumApi/Services/BookingService.cs) builds the `Booking` object first (no shared state touched), then enters a `lock (_sync)` that atomically calls `IEventService.ReserveSeats(eventId)` (404 if the event is missing, 409 `NoAvailableSeatsException` if no seats left) and adds the booking to storage — kept as one critical section so no other `CreateBookingAsync` call can interleave between the seat check and the booking being recorded, while keeping the lock scope minimal (no unrelated work inside it). New bookings start as `Pending` (see [`BookingStatus`](PracticumApi/Models/BookingStatus.cs)). [`Booking.Confirm()`/`Booking.Reject()`](PracticumApi/Models/Booking.cs) set `Status` and `ProcessedAt`.
- [`BookingProcessingService`](PracticumApi/Services/BookingProcessingService.cs) is a `BackgroundService` (registered with `AddHostedService`). Every `PollingInterval` (**5 seconds**) it scans for `Pending` bookings and processes them **concurrently** (`Task.WhenAll`); each logs an `Information` line as soon as it starts (so the log visibly shows several bookings starting together, not one after another) and then waits `ProcessingDelay` (**2 seconds**) to simulate an external system before writing. A `SemaphoreSlim(1, 1)` serializes just the write step (event-existence check + `Confirm`/`Reject` + `Update`) so only one task updates storage at a time, while the delay itself runs unserialized. Per booking: if the event no longer exists, the booking is rejected and a `Warning` is logged; otherwise it's confirmed; any other exception rejects the booking, releases its seat via `IEventService.ReleaseSeats`, and logs an `Error`. It creates its own DI scope via `IServiceScopeFactory`, honors `OperationCanceledException`, and never lets an unexpected exception kill the loop.

### Tests

Integration tests in `PracticumTests` use **real service instances, no mocks** (despite `Moq` being referenced). `BookingService` takes `IEventService` in its constructor, so booking tests construct both services together. Tests follow Arrange/Act/Assert and are split with `#region Успешные сценарии` / `#region Неуспешные сценарии` (and, in `BookingServiceIntegrationTests`, a third `#region Конкурентность` for seat-overbooking races).

`EventServiceIntegrationTests` is one `partial class` spread across several files, grouped by behavior under test — add a new `EventService` test to the file that matches its concern:

- [EventServiceIntegrationTests.cs](PracticumTests/EventServiceIntegrationTests.cs) — the shared `CreateEventService()` helper only
- [EventServiceCrudTests.cs](PracticumTests/EventServiceCrudTests.cs) — `Add` / `Get` / `Update` / `Delete` and their `NotFoundException` cases
- [EventServiceFilteringTests.cs](PracticumTests/EventServiceFilteringTests.cs) — `GetAll` filtering by title and date range
- [EventServicePaginationTests.cs](PracticumTests/EventServicePaginationTests.cs) — `GetAll` paging and `page` / `pageSize` validation
- [EventServiceDateHandlingTests.cs](PracticumTests/EventServiceDateHandlingTests.cs) — events with `EndAt < StartAt` still persist (no service-level date validation)

`BookingServiceIntegrationTests` remains a single file. Its `CreateTestEvent(eventService, totalSeats = 100)` helper builds events via `Event.Create`, so tests can dial in capacity (e.g. `totalSeats: 1`) to exercise `NoAvailableSeatsException` and overbooking races. Its `#region Конкурентность` tests use `Task.Run` + `Task.WhenAll` (real thread-pool parallelism, not sequential `await`s in a loop) — `TryCreateBookingAsync` returns the created `Booking?` (`null` on `NoAvailableSeatsException`) rather than a bare `bool`, so the overbooking test can also assert the successful bookings' `Id`s are unique, not just their count.

## Conventions

- **Russian** is used for commit messages, code comments, XML doc summaries, and exception messages (which become the `detail` field of API error responses). Match this when editing existing code.
- Services use C# primary constructors and collection expressions (`[]`).
- `bin/` and `obj/` are the only gitignored paths.
