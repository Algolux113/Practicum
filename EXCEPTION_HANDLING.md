# Обработка ошибок в PracticumApi

## Обзор

Система обработки ошибок построена на основе собственных исключений, которые выбрасываются из бизнес-логики (сервисы) и перехватываются middleware на уровне приложения.

## Структура

```
PracticumApi/
├── Exceptions/
│   ├── NotFoundException.cs          # Ресурс не найден
│   └── ValidationException.cs        # Ошибка валидации
├── Services/
│   ├── EventService.cs               # Сервис событий выбрасывает исключения
│   └── BookingService.cs             # Сервис бронирований также выбрасывает исключения
├── Controllers/
│   └── EventController.cs            # Контроллер полагается на middleware
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
        : base($"{resourceName} с ID {id} не найден") { }
}
```

**Примеры использования в EventService:**
```csharp
public Event Get(Guid id)
{
    var eventItem = Events.FirstOrDefault(x => x.Id == id);
    if (eventItem is null)
        throw new NotFoundException("Event", id);

    return eventItem;
}

public void Delete(Guid id)
{
    var eventItem = Events.FirstOrDefault(x => x.Id == id);
    if (eventItem is null)
        throw new NotFoundException("Event", id);

    Events.Remove(eventItem);
}
```

**Примеры использования в BookingService** (методы `Get`, `Update` и `Delete`):
```csharp
public Booking Get(Guid id)
{
    var booking = _bookings.FirstOrDefault(x => x.Id == id);
    if (booking is null)
        throw new NotFoundException("Booking", id);

    return booking;
}
```

**HTTP ответ:**
```
HTTP/1.1 404 Not Found
Content-Type: application/json

{
  "title": "Resource not found",
  "status": 404,
  "detail": "Event с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
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

**HTTP ответ:**
```
HTTP/1.1 400 Bad Request
Content-Type: application/json

{
  "title": "Validation error",
  "status": 400,
  "detail": "Дата окончания должна быть больше даты начала.",
  "instance": "/events"
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
        _logger.LogError(ex,
            "Unhandled exception. Method={Method}, Path={Path}, RequestId={RequestId}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.Request.Headers["x-request-id"]);

        // Если ответ уже начат, повторная запись невозможна
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        var (statusCode, title) = MapStatusCode(ex);

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        var error = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = ex.Message,
            Instance = httpContext.Request.Path
        };

        await httpContext.Response.WriteAsJsonAsync(error);
    }

    private static (int statusCode, string title) MapStatusCode(Exception ex)
        => ex switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation error"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
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
   - Сообщение: "Event с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден"

**Ответ:**
```json
HTTP/1.1 404 Not Found
Content-Type: application/json

{
  "title": "Resource not found",
  "status": 404,
  "detail": "Event с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
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
Content-Type: application/json

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

Тесты проверяют, что исключения выбрасываются в правильных сценариях (см. [`EventServiceIntegrationTests`](PracticumTests/EventServiceIntegrationTests.cs)):

```csharp
[Fact]
public void Get_WithInvalidId_ShouldThrowNotFoundException()
{
    // Arrange
    var eventService = CreateEventService();
    eventService.Add(new Event { Title = "Event", ... });

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

[Fact]
public void Get_WithInvalidId_ShouldThrowNotFoundException()
{
    // Пример для BookingService: бронирование с несуществующим ID
    var bookingService = new BookingService();
    Assert.Throws<NotFoundException>(() => bookingService.Get(Guid.NewGuid()));
}
```

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
   private static (int statusCode, string title) MapStatusCode(Exception ex)
       => ex switch
       {
           NotFoundException => (404, "Resource not found"),
           BusinessLogicException => (409, "Conflict"),
           _ => (500, "Internal server error")
       };
   ```

## Преимущества текущей архитектуры

✅ **Единообразный формат** - все ошибки возвращаются в одном формате (RFC 7807)  
✅ **Централизованная обработка** - вся логика обработки в одном месте  
✅ **Легкое логирование** - все ошибки логируются в одном месте (метод, путь, RequestId)  
✅ **Чистая архитектура** - контроллеры не содержат логику обработки ошибок  
✅ **Расширяемость** - просто добавьте новое исключение и его обработку  
✅ **Переиспользование** - одни и те же исключения используются как в `EventService`, так и в `BookingService`  
✅ **Тестируемость** - легко тестировать выброс исключений
