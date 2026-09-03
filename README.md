# Practicum
PracticumApi.
1. Клонировать или скачать репозиторий;
2. Открыть терминал в PracticumApi;
3. `dotnet build` -> `dotnet run --launch-profile https`;
4. Открыть в браузере `https://localhost:7008/swagger/`.

## Запуск тестов
Для запуска всех тестов выполните команду:
```
dotnet test
```

Для запуска тестов с подробным выводом:
```
dotnet test --verbosity normal
```

### Тестовый проект

Интеграционные тесты находятся в проекте [`PracticumTests`](PracticumTests/PracticumTests.csproj) и используют реальные реализации сервисов (без моков). Состоит из двух классов:

- [`EventServiceIntegrationTests`](PracticumTests/EventServiceIntegrationTests.cs) — покрывает операции `EventService`: создание, получение, обновление, удаление, фильтрацию по названию/датам, пагинацию, а также сценарии ошибок (`NotFoundException`, `ValidationException`).
- [`BookingServiceIntegrationTests`](PracticumTests/BookingServiceIntegrationTests.cs) — покрывает операции `BookingService`: создание брони для существующего события, получение по ID, изменение статуса (Confirm/Reject), а также сценарии ошибок (`NotFoundException` при бронировании несуществующего или удалённого события).

В обоих классах тесты разбиты на две группы через `#region`:
- **Успешные сценарии**;
- **Неуспешные сценарии**.

Используемые пакеты: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Moq`, `coverlet.collector`.

## Формат ошибок API

Все ошибки возвращаются в единообразном формате JSON (RFC 7807 - Problem Details):

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Resource not found",
  "status": 404,
  "detail": "Event с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

### Типы ошибок и статус-коды

| Статус | Название | Описание | Пример сценария |
|--------|----------|---------|-----------------|
| **404** | Resource not found | Ресурс не найден | `GET /events/{id}`, `GET /bookings/{id}` или `POST /events/{id}/book` — объект с указанным GUID не существует |
| **400** | Validation error | Ошибка валидации данных | `POST /events` с `endAt <= startAt`, `GET /events?page=0` |
| **500** | Internal server error | Ошибка сервера | Непредвиденная ошибка при обработке запроса |

### Примеры ошибок

**404 - Событие не найдено:**
```json
{
  "title": "Resource not found",
  "status": 404,
  "detail": "Event с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**404 - Бронь не найдена:**
```json
{
  "title": "Resource not found",
  "status": 404,
  "detail": "Booking с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
  "instance": "/bookings/3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**400 - Ошибка валидации:**
```json
{
  "title": "Validation error",
  "status": 400,
  "detail": "Дата окончания должна быть больше даты начала.",
  "instance": "/events"
}
```

---

## Endpoints

### 1. GET /events
Получить все события с опциональной фильтрацией и пагинацией.

**Параметры запроса:**
- `title` (string, опционально) - фильтрация по названию события (частичное совпадение, без учета регистра)
- `from` (DateTime, опционально) - фильтрация по дате начала (событие начинается не раньше указанной даты)
- `to` (DateTime, опционально) - фильтрация по дате окончания (событие заканчивается не позже указанной даты)
- `page` (integer, default: 1) - номер страницы (минимум: 1, если меньше → 400 Validation error)
- `pageSize` (integer, default: 10) - количество элементов на странице (диапазон: 1-100, за пределами → 400 Validation error)

**Валидация параметров пагинации:**
- `page` должен быть >= 1. При `page < 1` возвращается 400 ошибка
- `pageSize` должен быть в диапазоне 1-100. При нарушении возвращается 400 ошибка

**Примеры ошибок пагинации:**

```bash
# Ошибка: page = 0
curl -X 'GET' \
  'https://localhost:7008/events?page=0' \
  -H 'accept: text/plain'

# Ответ 400:
# {
#   "title": "Validation error",
#   "status": 400,
#   "detail": "Номер страницы должен быть больше или равен 1",
#   "instance": "/events?page=0"
# }

# Ошибка: pageSize = 150 (больше максимума)
curl -X 'GET' \
  'https://localhost:7008/events?pageSize=150' \
  -H 'accept: text/plain'

# Ответ 400:
# {
#   "title": "Validation error",
#   "status": 400,
#   "detail": "Размер страницы не может превышать 100",
#   "instance": "/events?pageSize=150"
# }
```

**Примеры корректного использования:**
```
# Получить все события на первой странице
curl -X 'GET' \
  'https://localhost:7008/events' \
  -H 'accept: text/plain'

# Получить события по названию с фильтрацией по датам
curl -X 'GET' \
  'https://localhost:7008/events?title=Conference&from=2024-01-01&to=2024-12-31&page=1&pageSize=10' \
  -H 'accept: text/plain'

# Получить события по названию
curl -X 'GET' \
  'https://localhost:7008/events?title=Workshop' \
  -H 'accept: text/plain'

# Получить события в диапазоне дат
curl -X 'GET' \
  'https://localhost:7008/events?from=2024-06-01&to=2024-12-31' \
  -H 'accept: text/plain'
```

**Ответ:**
```json
{
  "items": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "title": "Conference 2024",
      "description": "Annual tech conference",
      "startAt": "2024-06-15T09:00:00",
      "endAt": "2024-06-15T17:00:00"
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 10
}
```

### 2. GET /events/{id}
```
curl -X 'GET' \
  'https://localhost:7008/events/3fa85f64-5717-4562-b3fc-2c963f66afa6' \
  -H 'accept: text/plain'
```

### 3. POST /events
```
curl -X 'POST' \
  'https://localhost:7008/events' \
  -H 'accept: */*' \
  -H 'Content-Type: application/json' \
  -d '{
  "title": "string",
  "description": "string",
  "startAt": "2026-08-04T07:48:22.985Z",
  "endAt": "2026-10-04T07:48:22.985Z"
}'
```

### 4. PUT /events/{id}
```
curl -X 'PUT' \
  'https://localhost:7008/events/3fa85f64-5717-4562-b3fc-2c963f66afa6' \
  -H 'accept: */*' \
  -H 'Content-Type: application/json' \
  -d '{
  "title": "string",
  "description": "string",
  "startAt": "2026-08-04T07:54:29.355Z",
  "endAt": "2026-10-04T07:54:29.355Z"
}'
```

### 5. POST /events/{id}/book
Создаёт бронь для указанного события. Если событие с таким ID не существует — возвращается `404 Resource not found`.

```
curl -X 'POST' \
  'https://localhost:7008/events/3fa85f64-5717-4562-b3fc-2c963f66afa6/book' \
  -H 'accept: */*'
```

**Ответ:** `202 Accepted` с телом созданной брони:
```json
{
  "id": "e8f9d70a-1b2c-4d3e-8f4a-5b6c7d8e9f01",
  "eventId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Pending",
  "createdAt": "2026-08-26T07:48:22.985Z",
  "processedAt": null
}
```

### 6. DELETE /events/{id}
```
curl -X 'DELETE' \
  'https://localhost:7008/events/3fa85f64-5717-4562-b3fc-2c963f66afa6' \
  -H 'accept: */*'
```

---

## Бронирования (Bookings)

Бронирования реализованы через [`IBookingService`](PracticumApi/Interfaces/IBookingService.cs) и [`BookingService`](PracticumApi/Services/BookingService.cs) (хранилище в памяти, аналогично событиям). Контроллер [`BookingController`](PracticumApi/Controllers/BookingController.cs) обслуживает маршрут `/bookings`.

Бронирования в статусе `Pending` автоматически обрабатываются фоновым сервисом [`BookingProcessingService`](PracticumApi/Services/BookingProcessingService.cs): он периодически опрашивает хранилище и переводит ожидающие брони в статус `Confirmed`, имитируя обработку внешней системой. Подробнее — в разделе [Автоматическая обработка бронирований](#автоматическая-обработка-бронирований).

### Модель Booking

Поля модели [`Booking`](PracticumApi/Models/Booking.cs):

| Поле | Тип | Описание |
|------|-----|----------|
| `id` | `Guid` | Уникальный идентификатор брони (назначается автоматически) |
| `eventId` | `Guid` | Идентификатор события, на которое создана бронь |
| `status` | `BookingStatus` | Статус брони (см. ниже) |
| `createdAt` | `DateTime` | Время создания брони (UTC) |
| `processedAt` | `DateTime?` | Время обработки брони (если применимо) |

### Статусы брони (`BookingStatus`)

| Значение | Описание |
|----------|----------|
| `Pending` | Ожидает обработки (статус по умолчанию при создании). Автоматически переводится в `Confirmed` фоновым сервисом |
| `Confirmed` | Бронь подтверждена (перевод из `Pending` выполняет `BookingProcessingService`) |
| `Rejected` | Бронь отклонена |

### GET /bookings/{id}
Получить бронь по идентификатору. Если бронь не найдена — `404 Resource not found`.

```
curl -X 'GET' \
  'https://localhost:7008/bookings/3fa85f64-5717-4562-b3fc-2c963f66afa6' \
  -H 'accept: text/plain'
```

**Ответ:**
```json
{
  "id": "e8f9d70a-1b2c-4d3e-8f4a-5b6c7d8e9f01",
  "eventId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Pending",
  "createdAt": "2026-08-26T07:48:22.985Z",
  "processedAt": null
}
```

### Автоматическая обработка бронирований

Фоновый сервис [`BookingProcessingService`](PracticumApi/Services/BookingProcessingService.cs) наследует `BackgroundService` и регистрируется в DI через `AddHostedService<BookingProcessingService>()` (см. [`Program.cs`](PracticumApi/Program.cs:12)).

**Принцип работы:**

1. Сервис запускается вместе с приложением и каждые **5 секунд** опрашивает хранилище.
2. Выбираются все брони в статусе `Pending`.
3. Для каждой брони выполняется искусственная задержка **2 секунды**, имитирующая обращение к внешней системе.
4. Бронь переводится в статус `Confirmed`, устанавливается `ProcessedAt = DateTime.UtcNow`, и изменения сохраняются через `IBookingService.Update`.
5. При остановке приложения (`CancellationToken`) сервис корректно завершает работу.

**Особенности реализации:**

- Сервис создаёт собственную DI-область (`IServiceScopeFactory.CreateScope`) и получает `IBookingService` через `GetRequiredService`, поэтому он не зависит напрямую от времени жизни сервиса бронирований.
- Ошибки при обработке не останавливают фоновый сервис — они логируются, после чего цикл продолжается.
- `OperationCanceledException` и `TaskCanceledException` при остановке приложения обрабатываются как штатное завершение.

Это означает, что бронь, созданная через `POST /events/{id}/book` в статусе `Pending`, вскоре будет автоматически подтверждена (статус `Confirmed`).

---

## Архитектура обработки ошибок

Проект использует единообразную систему обработки ошибок, основанную на собственных исключениях:

### Собственные исключения (`PracticumApi.Exceptions`)

1. **`NotFoundException`** - выбрасывается, когда запрошенный ресурс не существует
   - Пример: `GET /events/{id}` когда события с указанным GUID нет
   - HTTP статус: 404

2. **`ValidationException`** - выбрасывается при ошибках валидации данных
   - Пример: попытка создать событие с `EndAt < StartAt`
   - HTTP статус: 400

Обе ошибки также переиспользуются в сервисе бронирований [`BookingService`](PracticumApi/Services/BookingService.cs):
- методы `Get`, `Update` и `Delete` выбрасывают `NotFoundException("Booking", id)` при обращении к несуществующему бронированию;
- сервис регистрируется в DI через интерфейс [`IBookingService`](PracticumApi/Interfaces/IBookingService.cs).

### Обработка исключений

**Middleware:** `GlobalExceptionHandlingMiddleware` перехватывает все необработанные исключения и преобразует их в единообразный JSON ответ (RFC 7807 - Problem Details).

**Преимущества:**
- Единый формат ошибок для всех endpoints
- Централизованная обработка ошибок
- Логирование всех ошибок
- Гибкая расширяемость (легко добавить новые типы исключений)
