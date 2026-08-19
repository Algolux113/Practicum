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
│   └── EventService.cs               # Сервис выбрасывает исключения
├── Controllers/
│   └── EventController.cs            # Контроллер полагается на middleware
└── Middlewares/
    └── GlobalExceptionHandlingMiddleware.cs  # Обработка всех исключений
```

## Собственные исключения

### NotFoundException

Выбрасывается когда запрошенный ресурс не существует.

```csharp
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string resourceName, int id) 
        : base($"{resourceName} с ID {id} не найден") { }
}
```

**Примеры использования в EventService:**
```csharp
public Event Get(int id)
{
    var eventItem = Events.FirstOrDefault(x => x.Id == id);
    if (eventItem is null)
        throw new NotFoundException("Event", id);
    
    return eventItem;
}

public void Delete(int id)
{
    var eventItem = Events.FirstOrDefault(x => x.Id == id);
    if(eventItem is null)
        throw new NotFoundException("Event", id);
    
    Events.Remove(eventItem);
}
```

**HTTP ответ:**
```
HTTP/1.1 404 Not Found
Content-Type: application/json

{
  "title": "Resource not found",
  "status": 404,
  "detail": "Event с ID 999 не найден",
  "instance": "/events/999"
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
public class GlobalExceptionHandlingMiddleware
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
        _logger.LogError(ex, "Unhandled exception at {Path}", httpContext.Request.Path);
        
        var (statusCode, title) = MapStatusCode(ex);
        
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
6. Middleware логирует ошибку
   ↓
7. Middleware маппит исключение на HTTP статус-код и titre
   ↓
8. Middleware отправляет JSON ответ клиенту
```

## Пример: Получение несуществующего события

**Запрос:**
```bash
curl -X GET "https://localhost:7008/events/999"
```

**処理:**
1. `EventController.Get(999)` вызывает `_eventService.Get(999)`
2. `EventService.Get(999)` не находит событие и выбрасывает:
   ```csharp
   throw new NotFoundException("Event", 999);
   ```
3. Исключение поднимается в контроллер и выше
4. `GlobalExceptionHandlingMiddleware` перехватывает его
5. Middleware определяет:
   - Тип: `NotFoundException` → статус 404
   - Сообщение: "Event с ID 999 не найден"

**Ответ:**
```json
HTTP/1.1 404 Not Found
Content-Type: application/json

{
  "title": "Resource not found",
  "status": 404,
  "detail": "Event с ID 999 не найден",
  "instance": "/events/999"
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

Тесты проверяют, что исключения выбрасываются в правильных сценариях:

```csharp
[Fact]
public void Get_WithInvalidId_ShouldThrowNotFoundException()
{
    // Arrange
    var eventService = new EventService();
    eventService.Add(new Event { Title = "Event", ... });

    // Act & Assert
    Assert.Throws<NotFoundException>(() => eventService.Get(999));
}

[Fact]
public void GetAll_WithPageZero_ShouldThrowValidationException()
{
    // Arrange
    var eventService = new EventService();

    // Act & Assert
    var ex = Assert.Throws<ValidationException>(() => eventService.GetAll(page: 0));
    Assert.Contains("Номер страницы должен быть больше или равен 1", ex.Message);
}
```
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
✅ **Легкое логирование** - все ошибки логируются в одном месте  
✅ **Чистая архитектура** - контроллеры не содержат логику обработки ошибок  
✅ **Расширяемость** - просто добавьте новое исключение и его обработку  
✅ **Тестируемость** - легко тестировать выброс исключений  
