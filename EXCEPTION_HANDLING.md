# Обработка ошибок в PracticumApi

## Обзор

Система обработки ошибок построена на основе собственных исключений, которые выбрасываются из бизнес-логики (сервисы) и перехватываются middleware на уровне приложения.

## Структура

```
PracticumApi/
├── Exceptions/
│   ├── NotFoundException.cs          # Ресурс не найден
│   ├── ValidationException.cs        # Ошибка валидации
│   └── NoAvailableSeatsException.cs  # Нет свободных мест на событии
├── Services/
│   ├── EventService.cs               # Сервис событий выбрасывает исключения
│   ├── BookingService.cs             # Сервис бронирований также выбрасывает исключения
│   └── BookingProcessingService.cs   # Фоновый сервис (BackgroundService) подтверждает/отклоняет Pending-брони
├── Controllers/
│   ├── EventController.cs            # Контроллер событий полагается на middleware
│   └── BookingController.cs          # Контроллер бронирований полагается на middleware
└── Middlewares/
    └── GlobalExceptionHandlingMiddleware.cs  # Обработка всех исключений
```

## Собственные исключения

### NotFoundException

Выбрасывается когда запрошенный ресурс не существует. Идентификатор ресурса имеет тип `Guid`.

```csharp
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string resourceName, Guid id)
        : base($"Ресурс \"{resourceName}\" с ID {id} не найден") { }
}
```

**Примеры использования в EventService** (`_sync` — `ReaderWriterLockSlim`: `Get` берёт read-lock и не блокирует другие чтения, `Delete` — эксклюзивный write-lock):
```csharp
public Event Get(Guid id)
{
    _sync.EnterReadLock();
    try
    {
        var eventItem = _events.FirstOrDefault(x => x.Id == id);
        if (eventItem is null)
            throw new NotFoundException("Event", id);

        return eventItem;
    }
    finally
    {
        _sync.ExitReadLock();
    }
}

public void Delete(Guid id)
{
    _sync.EnterWriteLock();
    try
    {
        var eventItem = _events.FirstOrDefault(x => x.Id == id);
        if (eventItem is null)
            throw new NotFoundException("Event", id);

        _events.Remove(eventItem);
    }
    finally
    {
        _sync.ExitWriteLock();
    }
}
```

**Примеры использования в BookingService** (методы `Get`, `Update` и `Delete`):
```csharp
public Booking Get(Guid id)
{
    lock (_sync)
    {
        var booking = _bookings.FirstOrDefault(x => x.Id == id);
        if (booking is null)
            throw new NotFoundException("Booking", id);

        return booking;
    }
}
```

**Бронирование несуществующего события** (`CreateBookingAsync`):
```csharp
public Task<Booking> CreateBookingAsync(Guid eventId)
{
    // Объект брони не трогает общее состояние, поэтому собирается до входа в lock —
    // критическая секция ниже держит замок минимально необходимое время.
    var booking = new Booking
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        Status = BookingStatus.Pending,
        CreatedAt = DateTime.UtcNow,
    };

    // Резерв места и добавление брони — атомарная пара под одним замком, иначе
    // между ними мог бы вклиниться другой вызов CreateBookingAsync.
    // NotFoundException — если события нет, NoAvailableSeatsException — если мест не осталось.
    lock (_sync)
    {
        _eventService.ReserveSeats(eventId);
        _bookings.Add(booking);
    }

    return Task.FromResult(booking);
}
```
При вызове `POST /events/{id}/book` с несуществующим `id` middleware вернёт:
```
HTTP/1.1 404 Not Found

{
  "title": "Resource not found",
  "status": 404,
  "detail": "Ресурс \"Event\" с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6/book"
}
```

**HTTP ответ:**
```
HTTP/1.1 404 Not Found
Content-Type: application/problem+json

{
  "title": "Resource not found",
  "status": 404,
  "detail": "Ресурс \"Event\" с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

### ValidationException

Выбрасывается при ошибках валидации данных.

```csharp
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
    public ValidationException(string fieldName, string message)
        : base($"Ошибка валидации поля '{fieldName}': {message}") { }
}
```

**Примеры использования:**

1. **Валидация в сервисе (логика бизнеса):**
```csharp
if (eventItem.EndAt <= eventItem.StartAt)
    throw new ValidationException("EndAt",
        "Дата окончания должна быть больше даты начала.");
```

2. **Валидация параметров пагинации:**
```csharp
// В EventService.GetAll()
if (page < 1)
    throw new ValidationException(nameof(page),
        "Номер страницы должен быть больше или равен 1");

if (pageSize < 1)
    throw new ValidationException(nameof(pageSize),
        "Размер страницы должен быть больше или равен 1");

if (pageSize > 100)
    throw new ValidationException(nameof(pageSize),
        "Размер страницы не может превышать 100");
```

3. **Валидация на уровне DTO:**
```csharp
public class EventDTO : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (EndAt <= StartAt)
        {
            yield return new ValidationResult(
                "Дата окончания должна быть больше даты начала.",
                [nameof(EndAt)]);
        }
    }
}
```

Ошибки уровня DTO (атрибуты `[Required]`, `IValidatableObject`) не проходят через
middleware — их перехватывает конвейер MVC. Чтобы формат ответа совпадал с доменными
`ValidationException`, в [`Program.cs`](PracticumApi/Program.cs) переопределён
`ApiBehaviorOptions.InvalidModelStateResponseFactory`: он собирает сообщения об ошибках
в одно поле `detail` и возвращает тот же `ProblemDetails` (`title` = "Validation error",
статус 400, `Content-Type: application/problem+json`).

**HTTP ответ:**
```
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Validation error",
  "status": 400,
  "detail": "Дата окончания должна быть больше даты начала.",
  "instance": "/events"
}
```

### NoAvailableSeatsException

Выбрасывается, когда на событии не осталось свободных мест.

```csharp
public class NoAvailableSeatsException() : Exception("Нет свободных мест на это событие")
{
}
```

**Пример использования** (`IEventService.ReserveSeats` в [`EventService`](PracticumApi/Services/EventService.cs)):
```csharp
public void ReserveSeats(Guid id, int count = 1)
{
    // Резерв места мутирует AvailableSeats, поэтому нужен эксклюзивный write-lock:
    // иначе два читателя могли бы одновременно пройти TryReserveSeats и увести
    // AvailableSeats в минус.
    _sync.EnterWriteLock();
    try
    {
        var eventItem = _events.FirstOrDefault(x => x.Id == id);
        if (eventItem is null)
            throw new NotFoundException("Event", id);

        if (!eventItem.TryReserveSeats(count))
            throw new NoAvailableSeatsException();
    }
    finally
    {
        _sync.ExitWriteLock();
    }
}
```

`BookingService.CreateBookingAsync` вызывает `ReserveSeats` перед созданием брони, поэтому `POST /events/{id}/book` на событие без свободных мест вернёт `409 Conflict`:

**HTTP ответ:**
```
HTTP/1.1 409 Conflict
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8",
  "title": "No available seats",
  "status": 409,
  "detail": "Нет свободных мест на это событие",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6/book"
}
```

## Middleware обработки исключений

**GlobalExceptionHandlingMiddleware** перехватывает все необработанные исключения:

```csharp
public class GlobalExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            await HandleException(httpContext, ex);
        }
    }

    private async Task HandleException(HttpContext httpContext, Exception ex)
    {
        var (statusCode, title, type) = MapStatusCode(ex);

        var requestId = httpContext.Request.Headers.TryGetValue("x-request-id", out var header)
            && !string.IsNullOrWhiteSpace(header)
                ? header.ToString()
                : httpContext.TraceIdentifier;

        // 4xx — ожидаемые доменные ошибки, логируем их как Warning без стека;
        // стек трасс приберегаем для настоящих 5xx.
        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(ex,
                "Unhandled exception. Method={Method}, Path={Path}, RequestId={RequestId}",
                httpContext.Request.Method, httpContext.Request.Path, requestId);
        }
        else
        {
            _logger.LogWarning(
                "Handled domain exception ({StatusCode}). Method={Method}, Path={Path}, RequestId={RequestId}, Detail={Detail}",
                statusCode, httpContext.Request.Method, httpContext.Request.Path, requestId, ex.Message);
        }

        // Если ответ уже начат, повторная запись невозможна
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        var error = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = type,
            Detail = ex.Message,
            Instance = httpContext.Request.Path
        };

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(error, options: null, contentType: "application/problem+json");
    }

    private static (int statusCode, string title, string type) MapStatusCode(Exception ex)
        => ex switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found",
                "https://tools.ietf.org/html/rfc7231#section-6.5.4"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation error",
                "https://tools.ietf.org/html/rfc7231#section-6.5.1"),
            NoAvailableSeatsException => (StatusCodes.Status409Conflict, "No available seats",
                "https://tools.ietf.org/html/rfc7231#section-6.5.8"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error",
                "https://tools.ietf.org/html/rfc7231#section-6.6.1")
        };
}
```

## Поток обработки ошибок

```
1. Клиент отправляет запрос
   ↓
2. Контроллер вызывает сервис
   ↓
3. Сервис выбрасывает исключение (NotFoundException, ValidationException, и т.д.)
   ↓
4. Исключение проходит вверх по стеку
   ↓
5. GlobalExceptionHandlingMiddleware перехватывает исключение
   ↓
6. Middleware логирует ошибку (метод, путь, RequestId)
   ↓
7. Middleware маппит исключение на HTTP статус-код и заголовок (title)
   ↓
8. Middleware отправляет JSON ответ клиенту (RFC 7807)
```

## Пример: Получение несуществующего события

**Запрос:**
```bash
curl -X GET "https://localhost:7008/events/3fa85f64-5717-4562-b3fc-2c963f66afa6"
```

**Обработка:**
1. `EventController.Get(id)` вызывает `_eventService.Get(id)`
2. `EventService.Get(id)` не находит событие и выбрасывает:
   ```csharp
   throw new NotFoundException("Event", id); // id типа Guid
   ```
3. Исключение поднимается в контроллер и выше
4. `GlobalExceptionHandlingMiddleware` перехватывает его
5. Middleware определяет:
   - Тип: `NotFoundException` → статус 404
   - Сообщение: `Ресурс "Event" с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден`

**Ответ:**
```json
HTTP/1.1 404 Not Found
Content-Type: application/problem+json

{
  "title": "Resource not found",
  "status": 404,
  "detail": "Ресурс \"Event\" с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

## Пример: Некорректные параметры пагинации

**Запрос:**
```bash
# Попытка получить события с page=0
curl -X GET "https://localhost:7008/events?page=0"
```

**Обработка:**
1. `EventController.GetAll(page: 0, ...)` вызывает `_eventService.GetAll(page: 0, ...)`
2. `EventService.GetAll()` проверяет параметры в начале метода:
   ```csharp
   if (page < 1)
       throw new ValidationException(nameof(page),
           "Номер страницы должен быть больше или равен 1");
   ```
3. Выбрасывается исключение `ValidationException`
4. `GlobalExceptionHandlingMiddleware` перехватывает его
5. Middleware определяет:
   - Тип: `ValidationException` → статус 400
   - Сообщение: "Номер страницы должен быть больше или равен 1"

**Ответ:**
```json
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "title": "Validation error",
  "status": 400,
  "detail": "Номер страницы должен быть больше или равен 1",
  "instance": "/events?page=0"
}
```

**Также валидируются:**
- `pageSize = 0` → "Размер страницы должен быть больше или равен 1"
- `pageSize = 101` → "Размер страницы не может превышать 100"
- `pageSize = -5` → "Размер страницы должен быть больше или равен 1"

## Тестирование

Интеграционные тесты проверяют, что исключения выбрасываются в правильных сценариях:

- `EventServiceIntegrationTests` — покрывает `NotFoundException` (получение/обновление/удаление несуществующего события) и `ValidationException` (некорректные параметры пагинации, а также `Event.RecalculateAvailableSeats` при попытке уменьшить `TotalSeats` ниже числа занятых мест). Это один `partial`-класс, разбитый по файлам: сценарии `NotFoundException` и `RecalculateAvailableSeats` — в [`EventServiceCrudTests.cs`](PracticumTests/EventServiceCrudTests.cs), сценарии `ValidationException` по пагинации — в [`EventServicePaginationTests.cs`](PracticumTests/EventServicePaginationTests.cs).
- [`BookingServiceIntegrationTests`](PracticumTests/BookingServiceIntegrationTests.cs) — покрывает `NotFoundException` при создании брони для несуществующего или удалённого события и при получении брони по несуществующему ID, а также `NoAvailableSeatsException` при исчерпании мест (последовательно и в конкурентных сценариях).

**Примеры из `EventServiceIntegrationTests`** (`Get_WithInvalidId_...` — в `EventServiceCrudTests.cs`, `GetAll_WithPageZero_...` — в `EventServicePaginationTests.cs`):

```csharp
[Fact]
public void Get_WithInvalidId_ShouldThrowNotFoundException()
{
    // Arrange
    var eventService = CreateEventService();
    eventService.Add(new Event { Title = "Event", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });

    // Act & Assert
    Assert.Throws<NotFoundException>(() => eventService.Get(Guid.NewGuid()));
}

[Fact]
public void GetAll_WithPageZero_ShouldThrowValidationException()
{
    // Arrange
    var eventService = CreateEventService();

    // Act & Assert
    var ex = Assert.Throws<ValidationException>(() => eventService.GetAll(page: 0));
    Assert.Contains("Номер страницы должен быть больше или равен 1", ex.Message);
}
```

**Примеры из `BookingServiceIntegrationTests`:**

> Примечание: `BookingService` принимает `IEventService` в конструкторе, поэтому тесты создают оба сервиса совместно. Хелпер `CreateTestEvent(eventService, totalSeats = 100)` создаёт событие через `Event.Create` с заданной вместимостью.

```csharp
[Fact]
public async Task CreateBookingAsync_ForNonExistentEvent_ShouldThrowNotFoundException()
{
    // Arrange
    var (bookingService, _) = CreateServices();

    // Act & Assert
    await Assert.ThrowsAsync<NotFoundException>(() =>
        bookingService.CreateBookingAsync(Guid.NewGuid()));
}

[Fact]
public async Task Get_WithInvalidId_ShouldThrowNotFoundException()
{
    // Arrange
    var (bookingService, eventService) = CreateServices();
    var eventId = CreateTestEvent(eventService);
    await bookingService.CreateBookingAsync(eventId);

    // Act & Assert
    Assert.Throws<NotFoundException>(() => bookingService.Get(Guid.NewGuid()));
}

[Fact]
public async Task CreateBookingAsync_WhenNoSeatsRemaining_ShouldThrowNoAvailableSeatsException()
{
    // Arrange
    var (bookingService, eventService) = CreateServices();
    var eventId = CreateTestEvent(eventService, totalSeats: 1);
    await bookingService.CreateBookingAsync(eventId);

    // Act & Assert
    await Assert.ThrowsAsync<NoAvailableSeatsException>(() =>
        bookingService.CreateBookingAsync(eventId));
}
```

`BookingServiceIntegrationTests` также содержит регион `#region Конкурентность` с тестами на гонки при резервировании мест: N конкурентных запросов на событие с ограниченной вместимостью должны дать ровно `totalSeats` успешных броней и `NoAvailableSeatsException` для остальных, без превышения `AvailableSeats` ниже нуля.

## Расширение системы

Чтобы добавить новый тип ошибки:

1. **Создайте новое исключение:**
   ```csharp
   // Exceptions/BusinessLogicException.cs
   public class BusinessLogicException : Exception
   {
       public BusinessLogicException(string message) : base(message) { }
   }
   ```

2. **Выбросьте его из сервиса:**
   ```csharp
   throw new BusinessLogicException("Описание ошибки");
   ```

3. **Добавьте обработку в middleware:**
   ```csharp
   private static (int statusCode, string title, string type) MapStatusCode(Exception ex)
       => ex switch
       {
           NotFoundException => (404, "Resource not found", "https://tools.ietf.org/html/rfc7231#section-6.5.4"),
           BusinessLogicException => (409, "Conflict", "https://tools.ietf.org/html/rfc7231#section-6.5.8"),
           _ => (500, "Internal server error", "https://tools.ietf.org/html/rfc7231#section-6.6.1")
       };
   ```

4. **Пометьте экшены, которые могут его бросить, `[ProducesResponseType]`:**
   ```csharp
   [HttpPost]
   [ProducesResponseType(typeof(Booking), StatusCodes.Status202Accepted)]
   [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
   public async Task<IActionResult> Book(Guid id) { ... }
   ```
   Middleware работает вне MVC-конвейера, поэтому генератор OpenAPI (`Microsoft.AspNetCore.OpenApi`, `AddOpenApi()`/`MapOpenApi()` в [`Program.cs`](PracticumApi/Program.cs)) о нём не знает — без явного атрибута новый код ответа не попадёт в `/openapi/v1.json` и не будет виден в Swagger UI, даже если middleware уже умеет его возвращать.

> `NoAvailableSeatsException` (см. выше) — реальный пример именно такого расширения: собственное исключение + бросок из `EventService.ReserveSeats` + кейс в `MapStatusCode` → `409 Conflict` + `[ProducesResponseType]` на `BookingController.Book`.

## Преимущества текущей архитектуры

✅ **Единообразный формат** - все ошибки возвращаются в одном формате (RFC 7807)  
✅ **Централизованная обработка** - вся логика обработки в одном месте  
✅ **Легкое логирование** - все ошибки логируются в одном месте (метод, путь, RequestId)  
✅ **Чистая архитектура** - контроллеры не содержат логику обработки ошибок  
✅ **Расширяемость** - просто добавьте новое исключение и его обработку  
✅ **Переиспользование** - одни и те же исключения используются как в `EventService`, так и в `BookingService`  
✅ **Тестируемость** - легко тестировать выброс исключений
