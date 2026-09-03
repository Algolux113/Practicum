# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All commands run from the repository root (there is no `.sln` file; `dotnet` discovers both projects).

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

Controllers are thin and contain no error handling. Business rules live in the services, which throw domain exceptions from [`PracticumApi/Exceptions/`](PracticumApi/Exceptions): `NotFoundException` and `ValidationException`. [`GlobalExceptionHandlingMiddleware`](PracticumApi/Middlewares/GlobalExceptionHandlingMiddleware.cs) is registered **first** in the pipeline, catches everything, logs it, and maps it to an RFC 7807 `ProblemDetails` JSON response via its `MapStatusCode` switch (`NotFoundException` → 404, `ValidationException` → 400, else → 500).

**To add a new error type:** create the exception in `Exceptions/`, throw it from a service, and add a case to `MapStatusCode`. See [EXCEPTION_HANDLING.md](EXCEPTION_HANDLING.md) for a full walkthrough.

### Validation happens in two layers

- **Model binding:** [`EventDTO`](PracticumApi/Models/EventDTO.cs) implements `IValidatableObject` (`EndAt > StartAt`, `[Required]` fields). Failures are handled by the framework's automatic model-state validation.
- **Service logic:** [`EventService.GetAll`](PracticumApi/Services/EventService.cs) validates pagination (`page >= 1`, `1 <= pageSize <= 100`) and throws `ValidationException`, which the middleware turns into a 400.

### Bookings and background processing

- A booking is created via **`POST /events/{id}/book`** on [`EventController`](PracticumApi/Controllers/EventController.cs), *not* on `BookingController`. [`BookingController`](PracticumApi/Controllers/BookingController.cs) only exposes `GET /bookings/{id}`.
- [`BookingService.CreateBookingAsync`](PracticumApi/Services/BookingService.cs) calls `IEventService.Get(eventId)` first so a missing event surfaces as a 404. New bookings start as `Pending` (see [`BookingStatus`](PracticumApi/Models/BookingStatus.cs)).
- [`BookingProcessingService`](PracticumApi/Services/BookingProcessingService.cs) is a `BackgroundService` (registered with `AddHostedService`). Every **5 seconds** it scans for `Pending` bookings, waits **2 seconds** per booking to simulate an external system, then sets them to `Confirmed` with `ProcessedAt`. It creates its own DI scope via `IServiceScopeFactory` and never lets an exception kill the loop.

### Tests

Integration tests in `PracticumTests` use **real service instances, no mocks** (despite `Moq` being referenced). `BookingService` takes `IEventService` in its constructor, so booking tests construct both services together. Tests follow Arrange/Act/Assert and are split with `#region Успешные сценарии` / `#region Неуспешные сценарии`.

## Conventions

- **Russian** is used for commit messages, code comments, XML doc summaries, and exception messages (which become the `detail` field of API error responses). Match this when editing existing code.
- Services use C# primary constructors and collection expressions (`[]`).
- `bin/` and `obj/` are the only gitignored paths.
