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

Интеграционные тесты находятся в проекте [`PracticumTests`](PracticumTests/PracticumTests.csproj) и используют реальные реализации сервисов (без моков). Состоит из двух наборов:

- `EventServiceIntegrationTests` — покрывает операции `EventService`: создание, получение, обновление, удаление, фильтрацию по названию/датам, пагинацию, а также сценарии ошибок (`NotFoundException`, `ValidationException`). Это один `partial`-класс, разбитый по файлам по проверяемому поведению:
  - [`EventServiceIntegrationTests.cs`](PracticumTests/EventServiceIntegrationTests.cs) — только общий helper `CreateEventService()`;
  - [`EventServiceCrudTests.cs`](PracticumTests/EventServiceCrudTests.cs) — `Add` / `Get` / `Update` / `Delete` и их сценарии `NotFoundException`;
  - [`EventServiceFilteringTests.cs`](PracticumTests/EventServiceFilteringTests.cs) — фильтрация `GetAll` по названию и диапазону дат;
  - [`EventServicePaginationTests.cs`](PracticumTests/EventServicePaginationTests.cs) — пагинация `GetAll` и валидация `page` / `pageSize`;
  - [`EventServiceDateHandlingTests.cs`](PracticumTests/EventServiceDateHandlingTests.cs) — события с `EndAt < StartAt` всё равно сохраняются (на уровне сервиса дат не валидируются).
- [`BookingServiceIntegrationTests`](PracticumTests/BookingServiceIntegrationTests.cs) — покрывает операции `BookingService`: создание брони для существующего события (включая уменьшение `AvailableSeats` события — после одной брони и после каждой из серии), получение по ID, переходы статусов (`Confirm`/`Reject`, возврат места через `ReleaseSeats`), а также сценарии ошибок (`NotFoundException` при несуществующем/удалённом событии, `NoAvailableSeatsException` при исчерпании мест). Один файл.

В каждом файле тесты разбиты на группы через `#region`:
- **Успешные сценарии**;
- **Неуспешные сценарии**;
- **Конкурентность** (только в `BookingServiceIntegrationTests`) — тесты на реальном параллелизме (`Task.Run` + `Task.WhenAll`, не последовательные `await`): защита от овербукинга (N мест / больше N конкурентных запросов — ровно N успехов с уникальными `Id`, остальные `NoAvailableSeatsException`, `AvailableSeats = 0`) и уникальность `Id` при параллельных запросах в пределах вместимости.

Используемые пакеты: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Moq`, `coverlet.collector`.

## Формат ошибок API

Все ошибки возвращаются в единообразном формате JSON (RFC 7807 - Problem Details). Каждый экшен в `EventController` и `BookingController` помечен `[ProducesResponseType]` для всех кодов, которые он реально может вернуть (успех + доменные исключения) — без этого встроенный генератор `Microsoft.AspNetCore.OpenApi` не знает про ответы, брошенные из `GlobalExceptionHandlingMiddleware`, и в Swagger UI / `/openapi/v1.json` был бы виден только один код (обычно неверный «200 OK» вместо, например, реальных 201/202/204).

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Resource not found",
  "status": 404,
  "detail": "Ресурс \"Event\" с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

### Типы ошибок и статус-коды

| Статус | Название | Описание | Пример сценария |
|--------|----------|---------|-----------------|
| **404** | Resource not found | Ресурс не найден | `GET /events/{id}`, `GET /bookings/{id}` или `POST /events/{id}/book` — объект с указанным GUID не существует |
| **400** | Validation error | Ошибка валидации данных | `POST /events` с `endAt <= startAt` или `totalSeats <= 0`, `GET /events?page=0` |
| **409** | No available seats | На событии не осталось свободных мест | `POST /events/{id}/book`, когда `availableSeats = 0` |
| **500** | Internal server error | Ошибка сервера | Непредвиденная ошибка при обработке запроса |

### Примеры ошибок

**404 - Событие не найдено:**
```json
{
  "title": "Resource not found",
  "status": 404,
  "detail": "Ресурс \"Event\" с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**404 - Бронь не найдена:**
```json
{
  "title": "Resource not found",
  "status": 404,
  "detail": "Ресурс \"Booking\" с ID 3fa85f64-5717-4562-b3fc-2c963f66afa6 не найден",
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

**409 - Нет свободных мест:**
```json
{
  "title": "No available seats",
  "status": 409,
  "detail": "Нет свободных мест на это событие",
  "instance": "/events/3fa85f64-5717-4562-b3fc-2c963f66afa6/book"
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
      "endAt": "2024-06-15T17:00:00",
      "totalSeats": 100,
      "availableSeats": 100
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 10,
  "totalPages": 1
}
```

### 2. GET /events/{id}
```
curl -X 'GET' \
  'https://localhost:7008/events/3fa85f64-5717-4562-b3fc-2c963f66afa6' \
  -H 'accept: text/plain'
```

### 3. POST /events
`totalSeats` обязателен и должен быть больше 0; `availableSeats` при создании всегда равен `totalSeats`.
```
curl -X 'POST' \
  'https://localhost:7008/events' \
  -H 'accept: */*' \
  -H 'Content-Type: application/json' \
  -d '{
  "title": "string",
  "description": "string",
  "startAt": "2026-08-04T07:48:22.985Z",
  "endAt": "2026-10-04T07:48:22.985Z",
  "totalSeats": 100
}'
```

### 4. PUT /events/{id}
`totalSeats` обязателен. `availableSeats` пересчитывается автоматически: число уже занятых мест сохраняется, а результат ограничивается диапазоном `[0, totalSeats]` — клиент его не передаёт.
```
curl -X 'PUT' \
  'https://localhost:7008/events/3fa85f64-5717-4562-b3fc-2c963f66afa6' \
  -H 'accept: */*' \
  -H 'Content-Type: application/json' \
  -d '{
  "title": "string",
  "description": "string",
  "startAt": "2026-08-04T07:54:29.355Z",
  "endAt": "2026-10-04T07:54:29.355Z",
  "totalSeats": 100
}'
```

### 5. POST /events/{id}/book
Создаёт бронь для указанного события и атомарно резервирует одно место. Если событие с таким ID не существует — `404 Resource not found`. Если свободных мест не осталось — `409 No available seats`. Все три кода (`202`/`404`/`409`) явно описаны через `[ProducesResponseType]` и видны в Swagger UI / `/openapi/v1.json`.

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

## Вместимость события (Seats)

Модель [`Event`](PracticumApi/Models/Event.cs) хранит `totalSeats` (общее число мест, обязательно и должно быть больше 0) и `availableSeats` (свободные места; при создании равно `totalSeats`).

- События создаются через фабричный метод `Event.Create(...)`, который валидирует `totalSeats` и бросает `ValidationException` при `totalSeats <= 0`.
- `POST /events/{id}/book` вызывает `IEventService.ReserveSeats`, которая атомарно (под локом сервиса) проверяет наличие события и уменьшает `availableSeats`. Если мест не осталось — `409 No available seats` (`NoAvailableSeatsException`), что исключает овербукинг при конкурентных запросах.
- `IEventService.ReleaseSeats` возвращает место обратно (например, при отклонении брони); значение не может превысить `totalSeats`.
- `PUT /events/{id}` не позволяет `availableSeats` выйти за пределы `[0, totalSeats]` при изменении вместимости: число уже занятых мест сохраняется, а не переносится как есть.

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
| `Rejected` | Бронь отклонена — событие исчезло к моменту обработки, либо обработка завершилась неожиданной ошибкой (место при этом возвращается событию) |

Переходы между статусами инкапсулированы в методах [`Booking.Confirm()`](PracticumApi/Models/Booking.cs) и `Booking.Reject()` — оба выставляют `Status` и `ProcessedAt = DateTime.UtcNow`.

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

1. Сервис запускается вместе с приложением и каждые `PollingInterval` (**5 секунд**) опрашивает хранилище.
2. Выбираются все брони в статусе `Pending`.
3. Все найденные брони обрабатываются **параллельно** (`Task.WhenAll`) — при старте обработки каждая бронь сразу пишет в лог `Information`-сообщение (по нему видно, что несколько броней стартуют одновременно, а не по очереди), затем ждёт искусственную задержку `ProcessingDelay` (**2 секунды**), имитирующую обращение к внешней системе. Задержки идут одновременно, а не одна за другой.
4. Перед записью каждая задача захватывает `SemaphoreSlim(1, 1)` — сериализуется только сама запись (проверка события + `Confirm`/`Reject` + `Update`), задержка под замок не попадает, так что в один момент времени хранилище обновляет только одна задача:
   - если событие, на которое сделана бронь, к этому моменту удалено — бронь переводится в `Rejected` (`Booking.Reject()`), сохраняется, и пишется лог уровня `Warning`;
   - иначе бронь переводится в `Confirmed` (`Booking.Confirm()`) и сохраняется.
5. Если при обработке брони возникает непредвиденное исключение, бронь откатывается: `Booking.Reject()`, место возвращается событию через `IEventService.ReleaseSeats`, изменения сохраняются, пишется лог уровня `Error`.
6. При остановке приложения (`CancellationToken`) сервис корректно завершает работу; `OperationCanceledException` пробрасывается из обработки конкретной брони и не считается ошибкой.

**Особенности реализации:**

- Сервис создаёт собственную DI-область (`IServiceScopeFactory.CreateScope`) и получает из неё `IBookingService` и `IEventService` через `GetRequiredService`, поэтому не зависит напрямую от времени жизни этих сервисов.
- Ошибки при обработке не останавливают фоновый сервис — они логируются, после чего цикл продолжается.
- `OperationCanceledException` и `TaskCanceledException` при остановке приложения обрабатываются как штатное завершение.
- `SemaphoreSlim` освобождается в `Dispose()` сервиса.

Это означает, что бронь, созданная через `POST /events/{id}/book` в статусе `Pending`, вскоре будет автоматически подтверждена (статус `Confirmed`) — если только событие не исчезло или не произошла ошибка, тогда бронь станет `Rejected`.

---

## Архитектура обработки ошибок

Проект использует единообразную систему обработки ошибок, основанную на собственных исключениях:

### Собственные исключения (`PracticumApi.Exceptions`)

1. **`NotFoundException`** - выбрасывается, когда запрошенный ресурс не существует
   - Пример: `GET /events/{id}` когда события с указанным GUID нет
   - HTTP статус: 404

2. **`ValidationException`** - выбрасывается при ошибках валидации данных
   - Пример: попытка создать событие с `EndAt < StartAt` или `totalSeats <= 0`
   - HTTP статус: 400

3. **`NoAvailableSeatsException`** - выбрасывается, когда на событии не осталось свободных мест
   - Пример: `POST /events/{id}/book`, когда `availableSeats = 0`
   - HTTP статус: 409

Эти ошибки также переиспользуются в сервисе бронирований [`BookingService`](PracticumApi/Services/BookingService.cs):
- методы `Get`, `Update` и `Delete` выбрасывают `NotFoundException("Booking", id)` при обращении к несуществующему бронированию;
- `CreateBookingAsync` вызывает `IEventService.ReserveSeats`, которая атомарно бросает `NotFoundException` (событие не найдено) или `NoAvailableSeatsException` (мест не осталось);
- сервис регистрируется в DI через интерфейс [`IBookingService`](PracticumApi/Interfaces/IBookingService.cs).

### Обработка исключений

**Middleware:** `GlobalExceptionHandlingMiddleware` перехватывает все необработанные исключения и преобразует их в единообразный JSON ответ (RFC 7807 - Problem Details).

**Преимущества:**
- Единый формат ошибок для всех endpoints
- Централизованная обработка ошибок
- Логирование всех ошибок
- Гибкая расширяемость (легко добавить новые типы исключений)
